using System;
using System.Globalization;
using System.Text;

namespace Plume
{
    /// <summary>Outils de comparaison tolérante (accents, majuscules, phonétique).</summary>
    internal static class TextUtil
    {
        static readonly char[] BaseTable = BuildBaseTable();

        static char[] BuildBaseTable()
        {
            var t = new char[0x250];
            for (int i = 0; i < t.Length; i++)
            {
                char c = char.ToLowerInvariant((char)i);
                string d = c.ToString().Normalize(NormalizationForm.FormD);
                t[i] = d.Length > 0 && d[0] < 0x80 ? d[0] : c;
            }
            return t;
        }

        /// <summary>Minuscule sans accent (« É » -> « e »). Les ligatures restent telles quelles.</summary>
        public static char Base(char c)
        {
            return c < BaseTable.Length ? BaseTable[c] : char.ToLowerInvariant(c);
        }

        /// <summary>Clé de recherche : minuscules, sans accents, ligatures développées.</summary>
        public static string Fold(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            foreach (char c in s)
            {
                char b = Base(c);
                switch (b)
                {
                    case 'œ': sb.Append("oe"); break;
                    case 'æ': sb.Append("ae"); break;
                    case 'ß': sb.Append("ss"); break;
                    case '’': sb.Append('\''); break;
                    default: sb.Append(b); break;
                }
            }
            return sb.ToString();
        }

        public static string Lower(string s)
        {
            return s.ToLowerInvariant();
        }

        /// <summary>
        /// Le mot commence-t-il par ce qui a été tapé ? Une lettre tapée sans accent accepte
        /// toutes ses variantes accentuées (« ecol » -> « école »), mais une lettre tapée avec
        /// accent doit correspondre exactement. Les deux chaînes sont en minuscules.
        /// </summary>
        public static bool MatchPrefix(string typed, string word)
        {
            int i = 0, j = 0;
            while (i < typed.Length)
            {
                if (j >= word.Length)
                    return false;
                char t = typed[i], w = word[j];
                if (t == w)
                {
                    i++;
                    j++;
                    continue;
                }
                if ((w == 'œ' || w == 'æ') && t == (w == 'œ' ? 'o' : 'a'))
                {
                    char second = w == 'œ' ? 'e' : 'e';
                    if (i + 1 == typed.Length)
                        return true;
                    if (typed[i + 1] != second)
                        return false;
                    i += 2;
                    j++;
                    continue;
                }
                if (Base(t) == t && Base(w) == t)
                {
                    i++;
                    j++;
                    continue;
                }
                return false;
            }
            return true;
        }

        /// <summary>Met la casse de la proposition en accord avec ce qui a été tapé.</summary>
        public static string ApplyCase(string word, string typed, bool sentenceStart)
        {
            if (word.Length == 0)
                return word;
            int letters = 0, upper = 0;
            foreach (char c in typed)
            {
                if (char.IsLetter(c))
                {
                    letters++;
                    if (char.IsUpper(c))
                        upper++;
                }
            }
            if (letters >= 2 && upper == letters)
                return word.ToUpperInvariant();
            bool capFirst = sentenceStart || (letters > 0 && char.IsUpper(FirstLetter(typed)));
            if (capFirst && char.IsLower(word[0]))
                return char.ToUpperInvariant(word[0]) + word.Substring(1);
            return word;
        }

        static char FirstLetter(string s)
        {
            foreach (char c in s)
                if (char.IsLetter(c))
                    return c;
            return ' ';
        }

        public static bool IsWordChar(char c)
        {
            return char.IsLetter(c) || c == '\'' || c == '-' || c == '’';
        }

        /// <summary>
        /// Clé phonétique simplifiée pour retrouver un mot mal orthographié
        /// (« otomobil » ≈ « automobile », « fesait » ≈ « faisait »).
        /// </summary>
        public static string Phonetic(string word, string lang)
        {
            string s = word.ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            int n = s.Length;
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                char next = i + 1 < n ? s[i + 1] : '\0';
                char next2 = i + 2 < n ? s[i + 2] : '\0';
                if (lang == "fr")
                {
                    if (c == 'e' && next == 'a' && next2 == 'u') { sb.Append('o'); i += 2; continue; }
                    if (c == 'a' && next == 'u') { sb.Append('o'); i++; continue; }
                    if ((c == 'a' || c == 'e') && next == 'i') { sb.Append('e'); i++; continue; }
                    if (c == 'o' && (next == 'i' || next == 'y')) { sb.Append("wa"); i++; continue; }
                    if (c == 'o' && next == 'u') { sb.Append('U'); i++; continue; }
                    if (c == 'g' && next == 'u' && IsFrontVowel(next2)) { sb.Append('g'); i++; continue; }
                    if (c == 'g' && IsFrontVowel(next)) { sb.Append('j'); continue; }
                    if (c == 'ç') { sb.Append('s'); continue; }
                    if (c == 'h') continue;
                    if (IsNasalVowel(c) && (next == 'n' || next == 'm') && !IsVowel(next2) && next2 != 'n' && next2 != 'm')
                    {
                        char v = Base(c);
                        sb.Append(v == 'a' || v == 'e' ? 'A' : v == 'o' ? 'O' : 'I');
                        i++;
                        continue;
                    }
                }
                else
                {
                    if (c == 'c' && next == 'k') { sb.Append('k'); i++; continue; }
                    if (c == 'w' && next == 'h') { sb.Append('w'); i++; continue; }
                    if (c == 'g' && next == 'h') { i++; continue; }
                    if (c == 'x') { sb.Append("ks"); continue; }
                    if (c == 'e' && next == 'e') { sb.Append('i'); i++; continue; }
                }
                if (c == 'p' && next == 'h') { sb.Append('f'); i++; continue; }
                if (c == 't' && next == 'h') { sb.Append('t'); i++; continue; }
                if (c == 'q' && next == 'u') { sb.Append('k'); i++; continue; }
                if (c == 's' && next == 'c' && next2 == 'h') { sb.Append('S'); i += 2; continue; }
                if (c == 'c' && next == 'h') { sb.Append('S'); i++; continue; }
                if (c == 'c') { sb.Append(IsFrontVowel(next) ? 's' : 'k'); continue; }
                if (c == 'q') { sb.Append('k'); continue; }
                if (c == 'y') { sb.Append('i'); continue; }
                if (c == 's' && sb.Length > 0 && IsVowel(PrevChar(s, i)) && IsVowel(next) && lang == "fr")
                {
                    sb.Append('z');
                    continue;
                }
                char b = Base(c);
                if (b == 'œ') b = 'e';
                if (b == '\'' || b == '-')
                    continue;
                if (sb.Length > 0 && sb[sb.Length - 1] == b && !IsVowel(b))
                    continue; // consonnes doubles
                sb.Append(b);
            }
            return sb.ToString();
        }

        static char PrevChar(string s, int i)
        {
            return i > 0 ? s[i - 1] : '\0';
        }

        static bool IsVowel(char c)
        {
            return "aeiouyàâäéèêëîïôöùûüœæ".IndexOf(c) >= 0 && c != '\0';
        }

        static bool IsFrontVowel(char c)
        {
            return c != '\0' && "eiyéèêëîï".IndexOf(c) >= 0;
        }

        static bool IsNasalVowel(char c)
        {
            return c != '\0' && "aeiouéè".IndexOf(c) >= 0;
        }

        /// <summary>
        /// Distance d'édition (avec transpositions) entre <paramref name="typed"/> et le meilleur
        /// préfixe de <paramref name="word"/>. Renvoie max+1 si elle dépasse <paramref name="max"/>.
        /// </summary>
        public static int PrefixDistance(string typed, string word, int max, int[] prev2, int[] prev, int[] cur)
        {
            int n = typed.Length;
            int m = Math.Min(word.Length, n + max);
            // Lignes = caractères du mot, colonnes = caractères tapés.
            for (int i = 0; i <= n; i++)
                prev[i] = i;
            int best = prev[n];
            for (int j = 1; j <= m; j++)
            {
                cur[0] = j;
                int rowMin = cur[0];
                char wc = word[j - 1];
                for (int i = 1; i <= n; i++)
                {
                    char tc = typed[i - 1];
                    int cost = tc == wc ? 0 : 1;
                    int v = Math.Min(Math.Min(prev[i] + 1, cur[i - 1] + 1), prev[i - 1] + cost);
                    if (i > 1 && j > 1 && tc == word[j - 2] && typed[i - 2] == wc)
                        v = Math.Min(v, prev2[i - 2] + 1);
                    cur[i] = v;
                    if (v < rowMin)
                        rowMin = v;
                }
                if (cur[n] < best)
                    best = cur[n];
                if (rowMin > max)
                    break;
                int[] tmp = prev2;
                prev2 = prev;
                prev = cur;
                cur = tmp;
            }
            return best > max ? max + 1 : best;
        }

        public static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            return char.ToUpper(s[0], CultureInfo.InvariantCulture) + s.Substring(1);
        }
    }
}
