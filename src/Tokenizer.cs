using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Plume
{
    /// <summary>
    /// Découpe le texte en jetons comme tools/build_lm.py : mots (avec traits d'union et
    /// apostrophes internes), élisions françaises séparées (« l' »), nombres (« &lt;num&gt; »),
    /// fins de phrase (« &lt;s&gt; ») et ponctuation faible (« , »).
    /// </summary>
    internal static class Tokenizer
    {
        public const string SentenceStart = "<s>";
        public const string Comma = ",";
        public const string Number = "<num>";

        static readonly HashSet<string> ElisionFr = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "l", "d", "j", "m", "n", "s", "t", "c", "qu", "jusqu", "lorsqu", "puisqu", "quoiqu"
        };

        // Un mot peut se terminer par une apostrophe ou un trait d'union quand on est en train de le taper.
        static readonly Regex TokenRe = new Regex(
            @"(\p{L}+(?:['\-]\p{L}+)*['\-]?)|(\d+(?:[.,]\d+)*)|([.!?…]+|\n)|([,;:])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Normalize(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '’':
                    case '‘':
                    case 'ʼ':
                    case '`':
                    case '´':
                        sb.Append('\'');
                        break;
                    case ' ':
                    case ' ':
                    case ' ':
                        sb.Append(' ');
                        break;
                    case '\r':
                        sb.Append('\n');
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        public static bool IsElision(string token, string lang)
        {
            return lang == "fr" && token.Length > 1 && token[token.Length - 1] == '\''
                && ElisionFr.Contains(token.Substring(0, token.Length - 1));
        }

        /// <summary>Sépare « qu'aujourd'hui » en « qu' » + « aujourd'hui ».</summary>
        public static List<string> SplitWord(string word, string lang, bool keepTrailing)
        {
            var parts = new List<string>(2);
            if (lang == "fr")
            {
                while (true)
                {
                    int i = word.IndexOf('\'');
                    if (i <= 0 || !ElisionFr.Contains(word.Substring(0, i)))
                        break;
                    parts.Add(word.Substring(0, i + 1));
                    word = word.Substring(i + 1);
                }
            }
            if (word.Length > 0)
            {
                if (!keepTrailing)
                {
                    char last = word[word.Length - 1];
                    if (last == '-' || (last == '\'' && !IsElision(word, lang)))
                        word = word.Substring(0, word.Length - 1);
                }
                if (word.Length > 0)
                    parts.Add(word);
            }
            return parts;
        }

        /// <summary>
        /// Analyse le texte qui précède le curseur : renvoie le mot en cours de frappe et
        /// les deux jetons précédents.
        /// </summary>
        /// <param name="text">texte connu avant le curseur</param>
        /// <param name="startKnown">vrai si le texte commence au début du champ (sinon contexte inconnu)</param>
        public static TextContext Analyze(string text, bool startKnown, string lang)
        {
            var ctx = new TextContext();
            text = Normalize(text);
            var tokens = new List<string>();
            bool endsInWord = false, endsInNumber = false;
            Match last = null;
            foreach (Match m in TokenRe.Matches(text))
            {
                last = m;
                if (m.Groups[1].Success)
                {
                    tokens.AddRange(SplitWord(m.Value, lang, true));
                }
                else if (m.Groups[2].Success)
                {
                    tokens.Add(Number);
                }
                else if (m.Groups[3].Success)
                {
                    if (tokens.Count == 0 || tokens[tokens.Count - 1] != SentenceStart)
                        tokens.Add(SentenceStart);
                }
                else if (tokens.Count > 0 && tokens[tokens.Count - 1] != SentenceStart && tokens[tokens.Count - 1] != Comma)
                {
                    tokens.Add(Comma);
                }
            }
            if (last != null && last.Index + last.Length == text.Length)
            {
                endsInWord = last.Groups[1].Success;
                endsInNumber = last.Groups[2].Success;
            }

            if (endsInWord)
            {
                string lastTok = tokens[tokens.Count - 1];
                if (!IsElision(lastTok, lang))
                {
                    ctx.Partial = lastTok;
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }
            ctx.InNumber = endsInNumber;

            // Le premier mot connu peut être tronqué si on ignore le début du texte.
            int firstUsable = startKnown ? 0 : 1;
            if (!startKnown && tokens.Count > 0 && text.Length > 0 && !char.IsLetterOrDigit(text[0]))
                firstUsable = 0;

            ctx.Prev1 = TokenAt(tokens, tokens.Count - 1, firstUsable, startKnown);
            ctx.Prev2 = ctx.Prev1 == null ? null : TokenAt(tokens, tokens.Count - 2, firstUsable, startKnown);
            if (ctx.Prev1 == SentenceStart)
                ctx.Prev2 = null;

            char lastChar = text.Length > 0 ? text[text.Length - 1] : '\0';
            ctx.AfterSeparator = text.Length == 0 ? startKnown : (char.IsWhiteSpace(lastChar)
                || (lastChar == '\'' && ctx.Partial.Length == 0));
            return ctx;
        }

        static string TokenAt(List<string> tokens, int index, int firstUsable, bool startKnown)
        {
            if (index >= firstUsable && index >= 0)
                return tokens[index];
            if (index == -1 && startKnown)
                return SentenceStart;
            return null;
        }

        /// <summary>Renvoie la dernière phrase du texte (pour la relecture).</summary>
        public static string LastSentence(string text)
        {
            text = text.TrimEnd();
            if (text.Length == 0)
                return "";
            int end = text.Length - 1;
            while (end >= 0 && ".!?…\n".IndexOf(text[end]) >= 0)
                end--;
            int start = end;
            while (start >= 0 && ".!?…\n".IndexOf(text[start]) < 0)
                start--;
            return text.Substring(start + 1).Trim();
        }
    }

    /// <summary>Contexte de frappe : mot partiel + jetons précédents (null = inconnu).</summary>
    internal sealed class TextContext
    {
        public string Partial = "";
        public string Prev1;
        public string Prev2;
        public bool InNumber;
        /// <summary>Vrai si le curseur suit une espace (on peut prédire le mot suivant).</summary>
        public bool AfterSeparator;

        public bool SentenceStart
        {
            get { return Prev1 == Tokenizer.SentenceStart; }
        }
    }
}
