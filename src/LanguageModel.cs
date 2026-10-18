using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Plume
{
    /// <summary>
    /// Modèle de langue chargé depuis data/&lt;langue&gt;.plm (produit par tools/build_lm.py) :
    /// lexique pondéré, bigrammes et trigrammes élagués.
    /// </summary>
    internal sealed class LanguageModel
    {
        public string Lang;
        public string[] Words;        // forme canonique (« Paris », « je »)
        public string[] Lower;        // minuscules (même chaîne que Words si identique, pour économiser la mémoire)
        public string[] Folded;       // sans accents (même chaîne que Lower si identique)
        public string[] Phon;         // clé phonétique
        public int[] Freq;            // fréquence pondérée (probabilité × 1e9)
        public int[] CtxTotal;        // nombre d'occurrences du mot suivi d'un autre mot
        public double FreqTotal;

        // Bigrammes : suites du mot i dans BiWord/BiCount[BiStart[i] .. BiStart[i+1]).
        public int[] BiStart;
        public int[] BiWord;
        public int[] BiCount;

        // Trigrammes : contexte (a, b) -> plage dans TriWord/TriCount. Les contextes sont retrouvés par
        // recherche dichotomique dans triKeys (trié) ; un dictionnaire prendrait deux à quatre fois plus de mémoire.
        long[] triKeys;
        int[] triContext;             // contexte correspondant à triKeys[k]
        int[] triStart;
        int[] triTotal;
        public int[] TriWord;
        public int[] TriCount;

        // Index trié par clé sans accents pour la recherche par préfixe.
        public int[] SortedIds;
        public string[] SortedKeys;
        // Index phonétique : mots regroupés par première lettre de la clé.
        public Dictionary<char, int[]> PhonBuckets;
        // Clés phonétiques complètes, pour reconnaître un mot connu mal orthographié.
        public HashSet<string> PhonSet;

        readonly Dictionary<string, int> idByLower = new Dictionary<string, int>(StringComparer.Ordinal);

        public const int IdSentence = 0, IdComma = 1, IdNumber = 2, FirstWordId = 3;

        public int Count
        {
            get { return Words.Length; }
        }

        public static LanguageModel Load(string path)
        {
            // Lecture au fil de la décompression : le fichier décompressé (près de 20 Mo) n'est jamais en mémoire.
            var lm = new LanguageModel();
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var r = new BinaryReader(new BufferedStream(gz, 1 << 16), Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "PLM1")
                    throw new InvalidDataException("Format de modèle inconnu : " + path);
                lm.Lang = Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
                int v = r.ReadInt32();
                lm.Words = new string[v];
                lm.Lower = new string[v];
                lm.Freq = new int[v];
                lm.CtxTotal = new int[v];
                for (int i = 0; i < v; i++)
                {
                    string w = Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
                    lm.Words[i] = w;
                    string lower = w.ToLowerInvariant();
                    lm.Lower[i] = lower == w ? w : lower;
                    lm.Freq[i] = r.ReadInt32();
                    lm.CtxTotal[i] = r.ReadInt32();
                    lm.FreqTotal += lm.Freq[i];
                    if (!lm.idByLower.ContainsKey(lm.Lower[i]))
                        lm.idByLower.Add(lm.Lower[i], i);
                }

                lm.BiStart = new int[v + 1];
                var biWord = new List<int>(v * 8);
                var biCount = new List<int>(v * 8);
                for (int i = 0; i < v; i++)
                {
                    lm.BiStart[i] = biWord.Count;
                    int n = r.ReadInt32();
                    for (int k = 0; k < n; k++)
                    {
                        biWord.Add(r.ReadInt32());
                        biCount.Add(r.ReadInt32());
                    }
                }
                lm.BiStart[v] = biWord.Count;
                lm.BiWord = biWord.ToArray();
                lm.BiCount = biCount.ToArray();

                int nCtx = r.ReadInt32();
                lm.triStart = new int[nCtx + 1];
                lm.triKeys = new long[nCtx];
                lm.triContext = new int[nCtx];
                lm.triTotal = new int[nCtx];
                var triWord = new List<int>(nCtx * 8);
                var triCount = new List<int>(nCtx * 8);
                for (int c = 0; c < nCtx; c++)
                {
                    int a = r.ReadInt32(), b = r.ReadInt32();
                    lm.triTotal[c] = r.ReadInt32();
                    int n = r.ReadInt32();
                    lm.triKeys[c] = Key(a, b);
                    lm.triContext[c] = c;
                    lm.triStart[c] = triWord.Count;
                    for (int k = 0; k < n; k++)
                    {
                        triWord.Add(r.ReadInt32());
                        triCount.Add(r.ReadInt32());
                    }
                }
                lm.triStart[nCtx] = triWord.Count;
                lm.TriWord = triWord.ToArray();
                lm.TriCount = triCount.ToArray();
                Array.Sort(lm.triKeys, lm.triContext);
            }
            lm.BuildIndexes();
            return lm;
        }

        void BuildIndexes()
        {
            int v = Words.Length;
            Folded = new string[v];
            Phon = new string[v];
            var buckets = new Dictionary<char, List<int>>();
            PhonSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < v; i++)
            {
                string folded = TextUtil.Fold(Words[i]);
                Folded[i] = folded == Lower[i] ? Lower[i] : folded;
                Phon[i] = i < FirstWordId ? "" : TextUtil.Phonetic(Words[i], Lang);
                if (Phon[i].Length > 0)
                {
                    PhonSet.Add(Phon[i]);
                    List<int> list;
                    if (!buckets.TryGetValue(Phon[i][0], out list))
                        buckets[Phon[i][0]] = list = new List<int>();
                    list.Add(i);
                }
            }
            PhonBuckets = new Dictionary<char, int[]>();
            foreach (var kv in buckets)
                PhonBuckets[kv.Key] = kv.Value.ToArray();

            var ids = new List<int>(v);
            for (int i = FirstWordId; i < v; i++)
                ids.Add(i);
            ids.Sort((a, b) => string.CompareOrdinal(Folded[a], Folded[b]));
            SortedIds = ids.ToArray();
            SortedKeys = new string[SortedIds.Length];
            for (int i = 0; i < SortedIds.Length; i++)
                SortedKeys[i] = Folded[SortedIds[i]];
        }

        static long Key(int a, int b)
        {
            return ((long)a << 32) | (uint)b;
        }

        /// <summary>Identifiant d'un jeton (mot en minuscules ou jeton spécial), -1 si inconnu.</summary>
        public int IdOf(string token)
        {
            if (token == null)
                return -1;
            if (token == Tokenizer.SentenceStart)
                return IdSentence;
            if (token == Tokenizer.Comma)
                return IdComma;
            if (token == Tokenizer.Number)
                return IdNumber;
            int id;
            return idByLower.TryGetValue(token.ToLowerInvariant(), out id) ? id : -1;
        }

        /// <summary>Plage [start, end) des mots dont la clé sans accents commence par <paramref name="folded"/>.</summary>
        public void PrefixRange(string folded, out int start, out int end)
        {
            int lo = 0, hi = SortedKeys.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (string.CompareOrdinal(SortedKeys[mid], folded) < 0)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            start = lo;
            end = lo;
            while (end < SortedKeys.Length && SortedKeys[end].StartsWith(folded, StringComparison.Ordinal))
                end++;
        }

        /// <summary>Plage des suites du contexte (a, b) dans TriWord, ou false si absent.</summary>
        public bool TrigramRange(int a, int b, out int start, out int end, out int total)
        {
            int k = a < 0 || b < 0 ? -1 : Array.BinarySearch(triKeys, Key(a, b));
            if (k < 0)
            {
                start = end = total = 0;
                return false;
            }
            int c = triContext[k];
            start = triStart[c];
            end = triStart[c + 1];
            total = triTotal[c];
            return true;
        }
    }
}
