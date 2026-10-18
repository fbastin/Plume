using System.Globalization;
using System.IO;

namespace Plume
{
    /// <summary>
    /// Langue de l'interface (français ou anglais). Chaque texte visible est écrit dans les
    /// deux langues à l'endroit où il sert : L.T("Lire", "Read").
    /// </summary>
    internal static class L
    {
        public static string Lang = "fr";

        public static string T(string fr, string en)
        {
            return Lang == "en" ? en : fr;
        }

        /// <summary>Français si Windows est affiché en français, sinon anglais.</summary>
        public static string WindowsLanguage()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";
        }

        /// <summary>
        /// Langue d'interface d'un profil Plume (reglages.ini) ; celle de Windows s'il n'y a pas
        /// encore de profil. Un profil antérieur à l'interface bilingue garde le français.
        /// </summary>
        public static string FromProfile(string folder)
        {
            string path = Path.Combine(folder, "reglages.ini");
            try
            {
                if (!File.Exists(path))
                    return WindowsLanguage();
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("InterfaceLanguage="))
                        return line.Substring("InterfaceLanguage=".Length).Trim() == "en" ? "en" : "fr";
                }
            }
            catch (System.Exception)
            {
            }
            return "fr";
        }
    }
}
