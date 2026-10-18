using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Plume
{
    /// <summary>
    /// Ce que Plume apprend de l'utilisateur (par langue) : mots utilisés, enchaînements
    /// fréquents, nouveaux mots, mots refusés et abréviations.
    /// </summary>
    internal sealed class UserModel
    {
        sealed class Entry
        {
            public string Form;
            public int Count;
        }

        readonly Dictionary<string, Entry> words = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, int> bigrams = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly HashSet<string> blocked = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> abbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> recent = new Queue<string>();
        readonly Dictionary<string, int> recentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        const int RecentSize = 300;

        public readonly string Lang;
        readonly string path;
        readonly string abbrevPath;
        public long Total;
        public bool Dirty;

        public UserModel(string lang, string folder)
        {
            Lang = lang;
            path = Path.Combine(folder, "mots_" + lang + ".txt");
            abbrevPath = Path.Combine(folder, "abreviations_" + lang + ".txt");
            Load();
        }

        static string BigramKey(string prev, string word)
        {
            return prev + "\t" + word;
        }

        public int Count(string lower)
        {
            Entry e;
            return lower != null && words.TryGetValue(lower, out e) ? e.Count : 0;
        }

        public int Bigram(string prevLower, string lower)
        {
            int n;
            return prevLower != null && bigrams.TryGetValue(BigramKey(prevLower, lower), out n) ? n : 0;
        }

        public int RecentCount(string lower)
        {
            int n;
            return recentCounts.TryGetValue(lower, out n) ? n : 0;
        }

        public bool IsBlocked(string lower)
        {
            return blocked.Contains(lower);
        }

        /// <summary>Mots appris commençant par <paramref name="typedLower"/> (comparaison tolérante).</summary>
        public IEnumerable<KeyValuePair<string, int>> WordsMatching(string typedLower, int minCount)
        {
            foreach (var kv in words)
            {
                if (kv.Value.Count >= minCount && TextUtil.MatchPrefix(typedLower, kv.Key))
                    yield return new KeyValuePair<string, int>(kv.Value.Form, kv.Value.Count);
            }
        }

        /// <summary>Suites apprises après <paramref name="prevLower"/>.</summary>
        public IEnumerable<string> Continuations(string prevLower)
        {
            if (prevLower == null)
                yield break;
            string prefix = prevLower + "\t";
            foreach (var kv in bigrams)
            {
                if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                    yield return kv.Key.Substring(prefix.Length);
            }
        }

        public string FormOf(string lower)
        {
            Entry e;
            return words.TryGetValue(lower, out e) ? e.Form : lower;
        }

        /// <summary>Enregistre un mot tapé (et le jeton qui le précède).</summary>
        public void Observe(string prev, string word, bool sentenceStart)
        {
            string lower = word.ToLowerInvariant();
            Entry e;
            if (!words.TryGetValue(lower, out e))
            {
                e = new Entry { Form = sentenceStart ? lower : word };
                words[lower] = e;
            }
            else if (!sentenceStart)
            {
                e.Form = word;
            }
            e.Count++;
            Total++;
            if (prev != null)
            {
                string k = BigramKey(prev.ToLowerInvariant(), lower);
                int n;
                bigrams.TryGetValue(k, out n);
                bigrams[k] = n + 1;
            }
            recent.Enqueue(lower);
            int r;
            recentCounts.TryGetValue(lower, out r);
            recentCounts[lower] = r + 1;
            if (recent.Count > RecentSize)
            {
                string old = recent.Dequeue();
                if (--recentCounts[old] == 0)
                    recentCounts.Remove(old);
            }
            Dirty = true;
        }

        public void AddWord(string word, int count)
        {
            string lower = word.ToLowerInvariant();
            Entry e;
            if (!words.TryGetValue(lower, out e))
                words[lower] = e = new Entry { Form = word };
            e.Form = word;
            e.Count = Math.Max(e.Count, count);
            blocked.Remove(lower);
            Dirty = true;
        }

        public void Block(string word)
        {
            string lower = word.ToLowerInvariant();
            blocked.Add(lower);
            words.Remove(lower);
            Dirty = true;
        }

        public void Unblock(string word)
        {
            blocked.Remove(word.ToLowerInvariant());
            Dirty = true;
        }

        public void RemoveWord(string word)
        {
            words.Remove(word.ToLowerInvariant());
            Dirty = true;
        }

        public List<KeyValuePair<string, int>> AllWords()
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var kv in words)
                list.Add(new KeyValuePair<string, int>(kv.Value.Form, kv.Value.Count));
            list.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        public List<string> BlockedWords()
        {
            var list = new List<string>(blocked);
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return list;
        }

        public string Expand(string abbreviation)
        {
            string v;
            return abbreviations.TryGetValue(abbreviation, out v) ? v : null;
        }

        public string AbbreviationsText()
        {
            var sb = new StringBuilder();
            foreach (var kv in abbreviations)
                sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append("\r\n");
            return sb.ToString();
        }

        public void SetAbbreviations(string text)
        {
            abbreviations.Clear();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line.StartsWith("#") || eq <= 0)
                    continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                if (k.Length > 0 && v.Length > 0)
                    abbreviations[k] = v;
            }
            Dirty = true;
        }

        void Load()
        {
            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string[] f = line.Split('\t');
                        int n;
                        if (f.Length == 3 && f[0] == "W" && int.TryParse(f[2], out n))
                        {
                            words[f[1].ToLowerInvariant()] = new Entry { Form = f[1], Count = n };
                            Total += n;
                        }
                        else if (f.Length == 4 && f[0] == "B" && int.TryParse(f[3], out n))
                        {
                            bigrams[BigramKey(f[1], f[2])] = n;
                        }
                        else if (f.Length == 2 && f[0] == "X")
                        {
                            blocked.Add(f[1]);
                        }
                    }
                }
                if (File.Exists(abbrevPath))
                    SetAbbreviations(File.ReadAllText(abbrevPath, Encoding.UTF8));
                else if (Lang == "fr")
                    SetAbbreviations("bjr = bonjour\nstp = s'il te plaît\nsvp = s'il vous plaît\npcq = parce que\nqqch = quelque chose\nqqn = quelqu'un\nmrc = merci\n");
                else
                    SetAbbreviations("btw = by the way\nasap = as soon as possible\nthx = thanks\nidk = I don't know\n");
            }
            catch (Exception ex)
            {
                Log.Write("Lecture du profil utilisateur impossible : " + ex.Message);
            }
            Dirty = false;
        }

        public void Save()
        {
            if (!Dirty)
                return;
            try
            {
                var sb = new StringBuilder();
                sb.Append("# Plume : mots appris / learned words (W mot/word nombre/count), enchaînements / word pairs "
                    + "(B précédent/previous mot/word nombre/count), mots refusés / blocked words (X mot/word)\n");
                foreach (var kv in words)
                    sb.Append("W\t").Append(kv.Value.Form).Append('\t').Append(kv.Value.Count).Append('\n');
                foreach (var kv in bigrams)
                    sb.Append("B\t").Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
                foreach (string b in blocked)
                    sb.Append("X\t").Append(b).Append('\n');
                WriteAtomic(path, sb.ToString());
                WriteAtomic(abbrevPath, "# Une abréviation par ligne : abréviation = texte complet\r\n# One abbreviation per line: abbreviation = full text\r\n" + AbbreviationsText());
                Dirty = false;
            }
            catch (Exception ex)
            {
                Log.Write("Enregistrement du profil impossible : " + ex.Message);
            }
        }

        static void WriteAtomic(string file, string content)
        {
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(file))
                File.Replace(tmp, file, null);
            else
                File.Move(tmp, file);
        }
    }
}
