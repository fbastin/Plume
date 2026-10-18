using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Plume
{
    /// <summary>Tests du moteur (sans interface) : build.cmd test</summary>
    internal static class EngineTests
    {
        static int failures, total;

        static void Check(bool ok, string what)
        {
            total++;
            if (!ok)
            {
                failures++;
                Console.WriteLine("ÉCHEC : " + what);
            }
        }

        static List<string> Predict(Predictor p, string text, bool startKnown, int max)
        {
            TextContext ctx = Tokenizer.Analyze(text, startKnown, p.Model.Lang);
            return p.Predict(ctx, max).Select(x => x.Text).ToList();
        }

        static void Expect(Predictor p, string text, string word, int within)
        {
            List<string> got = Predict(p, text, true, within);
            Check(got.Contains(word), "« " + text + " » devrait proposer « " + word + " » (top " + within + ") : " + string.Join(", ", got));
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string data = args.Length > 0 ? args[0] : "data";
            string profile = Path.Combine(Path.GetTempPath(), "PlumeTests");
            if (Directory.Exists(profile))
                Directory.Delete(profile, true);
            Directory.CreateDirectory(profile);
            Settings.Folder = profile;

            // ---- Découpage ----
            TextContext c = Tokenizer.Analyze("Bonjour, je m'appelle Léa. L'éc", true, "fr");
            Check(c.Partial == "éc", "mot partiel après élision : " + c.Partial);
            Check(c.Prev1 == "L'", "élision en contexte : " + c.Prev1);
            Check(c.Prev2 == "<s>", "début de phrase avant l'élision : " + c.Prev2);
            c = Tokenizer.Analyze("Il y ", true, "fr");
            Check(c.Partial == "" && c.Prev1 == "y" && c.Prev2 == "Il" && c.AfterSeparator, "contexte « il y »");
            c = Tokenizer.Analyze("aujourd'", true, "fr");
            Check(c.Partial == "aujourd'", "apostrophe interne : " + c.Partial);
            c = Tokenizer.Analyze("j'", true, "fr");
            Check(c.Partial == "" && c.Prev1 == "j'" && c.AfterSeparator, "« j' » est un mot complet");
            c = Tokenizer.Analyze("peut-", true, "fr");
            Check(c.Partial == "peut-", "trait d'union : " + c.Partial);
            c = Tokenizer.Analyze("chat noir", false, "fr");
            Check(c.Partial == "noir" && c.Prev1 == null, "début inconnu : le premier mot peut être tronqué");
            c = Tokenizer.Analyze(" chat noir", false, "fr");
            Check(c.Prev1 == "chat", "début inconnu mais séparé par une espace");
            c = Tokenizer.Analyze("J'ai 12", true, "fr");
            Check(c.InNumber, "nombre en cours");
            Check(Tokenizer.LastSentence("Il pleut. Je suis content !") == "Je suis content !", "dernière phrase");

            // ---- Comparaisons ----
            Check(TextUtil.MatchPrefix("ecol", "école"), "sans accent -> accent");
            Check(!TextUtil.MatchPrefix("é", "est"), "accent tapé exigé");
            Check(TextUtil.MatchPrefix("coe", "cœur"), "ligature œ");
            Check(TextUtil.MatchPrefix("elev", "élève"), "plusieurs accents");
            Check(TextUtil.Phonetic("otomobil", "fr") == TextUtil.Phonetic("automobil", "fr"), "phonétique au/o");
            Check(TextUtil.Phonetic("fesait", "fr") == TextUtil.Phonetic("faisait", "fr"), "phonétique ai/e");
            Check(TextUtil.ApplyCase("bonjour", "", true) == "Bonjour", "majuscule en début de phrase");
            Check(TextUtil.ApplyCase("paris", "PA", false) == "PARIS", "tout en majuscules");
            var rows = new int[64];
            Check(TextUtil.PrefixDistance("becuase", "because", 2, new int[64], new int[64], rows) == 1, "transposition");

            // ---- Prédictions françaises ----
            var sw = Stopwatch.StartNew();
            LanguageModel fr = LanguageModel.Load(Path.Combine(data, "fr.plm"));
            Console.WriteLine("Modèle français : " + fr.Count + " mots, chargé en " + sw.ElapsedMilliseconds + " ms");
            var pf = new Predictor(fr, new UserModel("fr", profile));
            Expect(pf, "Il y ", "a", 1);
            Expect(pf, "Je ", "suis", 3);
            Expect(pf, "Je suis ", "très", 5);
            Expect(pf, "Je vais à l'éc", "école", 2);
            Expect(pf, "Je vais à l'ecol", "école", 1);
            Expect(pf, "Il fait b", "beau", 3);
            Expect(pf, "parce q", "que", 1);
            Expect(pf, "", "Je", 5);
            Expect(pf, "C'est une bonne id", "idée", 1);
            Expect(pf, "Les enfants jouent dans la c", "cour", 5);
            Expect(pf, "J'aime beaucoup ma voiture et mon otomob", "automobile", 5);
            Expect(pf, "Il fesai", "faisait", 3);
            Expect(pf, "Elle a mang", "manger", 5);
            Check(!Predict(pf, "Elle a mang", true, 5).Contains("mentionné"), "les fautes ne passent pas devant les bons débuts");
            Expect(pf, "Il fait beau aujourd", "aujourd'hui", 1);
            Expect(pf, "aujourd", "Aujourd'hui", 1);
            Expect(pf, "Je pense qu'", "il", 3);
            Expect(pf, "s'il vous pl", "plaît", 1);
            Expect(pf, "Nous somm", "sommes", 1);
            Expect(pf, "J'ai 12 ", "ans", 3);
            List<string> start = Predict(pf, "Bonjour. ", true, 5);
            Check(start.All(w => char.IsUpper(w[0])), "majuscules après un point : " + string.Join(", ", start));
            Check(!Predict(pf, "Il va a", true, 9).Contains("a"), "ne propose pas ce qui est déjà tapé");
            Check(Predict(pf, "Il va a", true, 9).Contains("à"), "propose « à » pour « a »");
            Check(!Predict(pf, "put", true, 9).Contains("putain"), "pas de grossièretés");

            // Apprentissage
            for (int i = 0; i < 3; i++)
                pf.User.Observe("mon", "Kerbouc", false);
            Expect(pf, "mon K", "Kerbouc", 3);
            pf.User.Block("école");
            Check(!Predict(pf, "Je vais à l'éc", true, 9).Contains("école"), "mot refusé");
            pf.User.Unblock("école");
            pf.User.SetAbbreviations("bjr = bonjour\n");
            Check(Predict(pf, "Il dit bjr", true, 3).FirstOrDefault() == "bonjour", "abréviation");
            Check(Predict(pf, "bjr", true, 3).FirstOrDefault() == "Bonjour", "abréviation en début de phrase");
            pf.User.Save();
            var reloaded = new UserModel("fr", profile);
            Check(reloaded.Count("kerbouc") == 3, "profil enregistré et relu");

            // Vitesse
            string[] samples = { "Je ", "Il y ", "l'éc", "Nous somm", "otomob", "Je vais à la m", "d", "pr" };
            sw.Restart();
            int n = 0;
            for (int r = 0; r < 50; r++)
                foreach (string s in samples)
                {
                    Predict(pf, s, true, 5);
                    n++;
                }
            double ms = sw.Elapsed.TotalMilliseconds / n;
            Console.WriteLine("Prédiction : " + ms.ToString("0.00") + " ms en moyenne");
            Check(ms < 15, "prédiction assez rapide (" + ms.ToString("0.0") + " ms)");

            // ---- Prédictions anglaises ----
            LanguageModel en = LanguageModel.Load(Path.Combine(data, "en.plm"));
            var pe = new Predictor(en, new UserModel("en", profile));
            Expect(pe, "I don't ", "know", 3);
            Expect(pe, "Thank ", "you", 1);
            Expect(pe, "I would like to ", "be", 10);
            Expect(pe, "I stayed home becuas", "because", 2);
            Expect(pe, "i", "I", 3);
            Expect(pe, "Wh", "What", 5);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "Tous les tests réussis (" + total + ")" : failures + " échec(s) sur " + total);
            foreach (string probe in new[] { "Je ", "Je suis ", "Il fait ", "l'éc", "Nous allons au c", "Elle a mang", "otomob" })
                Console.WriteLine("  « " + probe + " » -> " + string.Join(", ", Predict(pf, probe, true, 5)));
            foreach (string probe in new[] { "I ", "I want to ", "The weather is ", "becau", "Can you hel" })
                Console.WriteLine("  « " + probe + " » -> " + string.Join(", ", Predict(pe, probe, true, 5)));
            return failures == 0 ? 0 : 1;
        }
    }
}
