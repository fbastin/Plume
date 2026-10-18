using System;
using System.Collections.Generic;

namespace Plume
{
    internal sealed class Prediction
    {
        public string Text;        // ce qui sera inséré (casse ajustée)
        public double Score;
        public bool Fuzzy;         // proposé malgré une faute d'orthographe
        public bool Abbreviation;

        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>
    /// Moteur de prédiction : combine trigrammes, bigrammes, fréquence des mots,
    /// recherche phonétique (fautes d'orthographe) et ce que l'utilisateur a appris.
    /// </summary>
    internal sealed class Predictor
    {
        public readonly LanguageModel Model;
        public readonly UserModel User;
        public bool SpellingHelp = true;
        public int MinLearnCount = 2;

        readonly int[] topIds;
        readonly int[] row0 = new int[64], row1 = new int[64], row2 = new int[64];

        public Predictor(LanguageModel model, UserModel user)
        {
            Model = model;
            User = user;
            var ids = new List<int>();
            for (int i = LanguageModel.FirstWordId; i < model.Count; i++)
                ids.Add(i);
            ids.Sort((a, b) => model.Freq[b].CompareTo(model.Freq[a]));
            topIds = ids.GetRange(0, Math.Min(150, ids.Count)).ToArray();
        }

        sealed class Candidate
        {
            public int Id = -1;       // -1 : mot appris absent du lexique
            public string Word;       // forme canonique
            public double Mult = 1;   // pénalité orthographique
            public bool Fuzzy;
        }

        public List<Prediction> Predict(TextContext ctx, int max)
        {
            var result = new List<Prediction>();
            if (ctx.InNumber || max <= 0)
                return result;
            string partial = ctx.Partial ?? "";
            string typed = partial.ToLowerInvariant();
            var lm = Model;

            int p1 = lm.IdOf(ctx.Prev1), p2 = lm.IdOf(ctx.Prev2);
            string prevLower = ctx.Prev1 == null ? null : ctx.Prev1.ToLowerInvariant();

            var bi = new Dictionary<int, int>();
            double biTotal = 1;
            if (p1 >= 0)
            {
                for (int k = lm.BiStart[p1]; k < lm.BiStart[p1 + 1]; k++)
                    bi[lm.BiWord[k]] = lm.BiCount[k];
                biTotal = Math.Max(1, lm.CtxTotal[p1]);
            }
            var tri = new Dictionary<int, int>();
            int ts, te, tt;
            double triTotal = 1;
            bool hasTri = lm.TrigramRange(p2, p1, out ts, out te, out tt);
            if (hasTri)
            {
                for (int k = ts; k < te; k++)
                    tri[lm.TriWord[k]] = lm.TriCount[k];
                triTotal = Math.Max(1, tt);
            }

            var cands = new Dictionary<string, Candidate>(StringComparer.Ordinal);

            if (partial.Length == 0)
            {
                foreach (int id in tri.Keys)
                    AddId(cands, id, 1, false);
                int added = 0;
                for (int k = p1 >= 0 ? lm.BiStart[p1] : 0; p1 >= 0 && k < lm.BiStart[p1 + 1] && added < 150; k++, added++)
                    AddId(cands, lm.BiWord[k], 1, false);
                foreach (int id in topIds)
                    AddId(cands, id, 1, false);
                foreach (string w in User.Continuations(prevLower))
                    AddLearned(cands, w);
            }
            else
            {
                foreach (int id in tri.Keys)
                    if (id >= LanguageModel.FirstWordId && TextUtil.MatchPrefix(typed, lm.Lower[id]))
                        AddId(cands, id, 1, false);
                foreach (int id in bi.Keys)
                    if (id >= LanguageModel.FirstWordId && TextUtil.MatchPrefix(typed, lm.Lower[id]))
                        AddId(cands, id, 1, false);

                int s, e;
                lm.PrefixRange(TextUtil.Fold(partial), out s, out e);
                var range = new List<int>();
                for (int i = s; i < e; i++)
                {
                    int id = lm.SortedIds[i];
                    if (TextUtil.MatchPrefix(typed, lm.Lower[id]))
                        range.Add(id);
                }
                if (range.Count > 200)
                {
                    range.Sort((a, b) => lm.Freq[b].CompareTo(lm.Freq[a]));
                    range.RemoveRange(200, range.Count - 200);
                }
                foreach (int id in range)
                    AddId(cands, id, 1, false);

                foreach (var kv in User.WordsMatching(typed, 1))
                    AddLearned(cands, kv.Key);

                if (SpellingHelp && TextUtil.Fold(partial).Length >= 3)
                    AddFuzzy(cands, partial);
            }

            var scored = new List<Prediction>();
            foreach (var c in cands.Values)
            {
                string lower = c.Word.ToLowerInvariant();
                if (User.IsBlocked(lower))
                    continue;
                double score;
                if (c.Id >= 0)
                {
                    double pu = lm.Freq[c.Id] / lm.FreqTotal;
                    int n2, n3;
                    bi.TryGetValue(c.Id, out n2);
                    tri.TryGetValue(c.Id, out n3);
                    if (hasTri)
                        score = 0.55 * n3 / triTotal + 0.35 * n2 / biTotal + 0.10 * pu;
                    else if (p1 >= 0)
                        score = 0.80 * n2 / biTotal + 0.20 * pu;
                    else
                        score = pu;
                }
                else
                {
                    // Mot nouveau : seulement après quelques utilisations.
                    if (User.Count(lower) < MinLearnCount)
                        continue;
                    score = 0;
                }
                score += UserScore(prevLower, lower);
                score *= c.Mult;
                scored.Add(new Prediction { Text = c.Word, Score = score, Fuzzy = c.Fuzzy });
            }
            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            string expansion = partial.Length > 0 ? User.Expand(partial) : null;
            if (expansion != null)
                scored.Insert(0, new Prediction { Text = expansion, Score = double.MaxValue, Abbreviation = true });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in scored)
            {
                if (result.Count >= max)
                    break;
                p.Text = p.Abbreviation ? TextUtil.ApplyCase(p.Text, "", ctx.SentenceStart)
                                        : TextUtil.ApplyCase(p.Text, partial, ctx.SentenceStart);
                if (p.Text == partial || !seen.Add(p.Text))
                    continue;
                result.Add(p);
            }
            return result;
        }

        double UserScore(string prevLower, string lower)
        {
            if (User.Total == 0)
                return 0;
            double su = 0.4 * User.Count(lower) / (User.Total + 200.0);
            if (prevLower != null)
            {
                int nb = User.Bigram(prevLower, lower);
                if (nb > 0)
                    su += 0.6 * nb / (User.Count(prevLower) + 3.0);
            }
            if (User.RecentCount(lower) > 0)
                su += 0.0005;
            return 0.5 * su;
        }

        void AddId(Dictionary<string, Candidate> cands, int id, double mult, bool fuzzy)
        {
            if (id < LanguageModel.FirstWordId)
                return;
            string w = Model.Words[id];
            Candidate c;
            if (cands.TryGetValue(w, out c))
            {
                if (mult > c.Mult)
                {
                    c.Mult = mult;
                    c.Fuzzy = fuzzy;
                }
                return;
            }
            cands[w] = new Candidate { Id = id, Word = w, Mult = mult, Fuzzy = fuzzy };
        }

        void AddLearned(Dictionary<string, Candidate> cands, string form)
        {
            int id = Model.IdOf(form);
            if (id >= 0)
            {
                AddId(cands, id, 1, false);
                return;
            }
            if (!cands.ContainsKey(form))
                cands[form] = new Candidate { Word = form };
        }

        /// <summary>Ajoute les mots qui « sonnent » comme ce qui est tapé ou en sont très proches.</summary>
        void AddFuzzy(Dictionary<string, Candidate> cands, string partial)
        {
            var lm = Model;
            string key = TextUtil.Phonetic(partial, lm.Lang);
            if (key.Length < 2 || key.Length > 40)
                return;
            int[] bucket;
            if (!lm.PhonBuckets.TryGetValue(key[0], out bucket))
                return;
            int maxDist = key.Length >= 6 ? 2 : 1;
            var found = new List<KeyValuePair<int, double>>();
            foreach (int id in bucket)
            {
                string pk = lm.Phon[id];
                double mult;
                // Les fautes coûtent cher : un mot bien commencé passe avant, sauf s'il est très rare.
                if (pk.StartsWith(key, StringComparison.Ordinal))
                {
                    mult = 0.1;
                }
                else
                {
                    int d = TextUtil.PrefixDistance(key, pk, maxDist, row0, row1, row2);
                    if (d > maxDist)
                        continue;
                    mult = d == 1 ? 0.003 : 0.0003;
                }
                found.Add(new KeyValuePair<int, double>(id, mult));
            }
            found.Sort((a, b) => (lm.Freq[b.Key] * b.Value).CompareTo(lm.Freq[a.Key] * a.Value));
            for (int i = 0; i < found.Count && i < 40; i++)
                AddId(cands, found[i].Key, found[i].Value, true);
        }
    }
}
