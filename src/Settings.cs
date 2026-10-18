using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Plume
{
    internal enum SelectionKeys
    {
        Digits,
        FunctionKeys,
        MouseOnly
    }

    internal enum Placement
    {
        FollowCaret,
        Fixed
    }

    /// <summary>Réglages de l'utilisateur, enregistrés dans %APPDATA%\Plume\reglages.ini.</summary>
    internal sealed class Settings
    {
        // Prédiction
        public string Language = "fr";          // langue d'écriture (dictionnaire, voix)
        public bool PredictionEnabled = true;
        public int PredictionCount = 5;
        public SelectionKeys Keys = SelectionKeys.Digits;
        public bool PredictNextWord = true;
        public bool AddSpace = true;
        public bool SmartPunctuation = true;
        public bool SpellingHelp = true;
        public bool Learn = true;
        public bool PredictInTerminals = false; // leurs invites de mot de passe ne sont pas protégées
        public int MinLearnCount = 3;
        public Placement Placement = Placement.FollowCaret;
        public int FixedX = -1;
        public int FixedY = -1;

        // Parole
        public bool SpeechEnabled = true;
        public string VoiceFr = "";
        public string VoiceEn = "";
        public int Rate = 0;
        public int Volume = 100;
        public bool SpeakOnHover = true;
        public bool SpeakChosen = true;
        public bool EchoLetters = false;
        public bool EchoWords = false;
        public bool EchoSentences = true;

        // Apparence
        public string FontName = "Segoe UI";
        public float FontSize = 15f;
        public string Theme = "Clair";
        public string InterfaceLanguage = "";   // langue des menus et fenêtres : fr ou en
        public bool ToolbarVisible = true;
        public int ToolbarX = -1;
        public int ToolbarY = -1;

        // Raccourcis et exclusions
        public string HotkeyRead = "Ctrl+Shift+F12";
        public string HotkeyToggle = "Ctrl+Shift+F11";
        public string ExcludedApps = "keepass;keepassxc;1password;bitwarden;mstsc";

        public static string Folder;

        static string FilePath
        {
            get { return Path.Combine(Folder, "reglages.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    // Premier lancement : on suit la langue d'affichage de Windows.
                    s.Language = s.InterfaceLanguage = L.WindowsLanguage();
                    return s;
                }
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;
                    FieldInfo f = typeof(Settings).GetField(line.Substring(0, eq).Trim());
                    if (f == null || f.IsStatic)
                        continue;
                    string v = line.Substring(eq + 1).Trim();
                    try
                    {
                        if (f.FieldType == typeof(string))
                            f.SetValue(s, v);
                        else if (f.FieldType == typeof(int))
                            f.SetValue(s, int.Parse(v, CultureInfo.InvariantCulture));
                        else if (f.FieldType == typeof(float))
                            f.SetValue(s, float.Parse(v, CultureInfo.InvariantCulture));
                        else if (f.FieldType == typeof(bool))
                            f.SetValue(s, v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                        else if (f.FieldType.IsEnum)
                            f.SetValue(s, Enum.Parse(f.FieldType, v, true));
                    }
                    catch (Exception)
                    {
                        Log.Write("Réglage ignoré : " + line);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Lecture des réglages impossible : " + ex.Message);
            }
            s.PredictionCount = Math.Max(1, Math.Min(9, s.PredictionCount));
            s.FontSize = Math.Max(8f, Math.Min(48f, s.FontSize));
            if (s.Language != "en")
                s.Language = "fr";
            // Profil antérieur à l'interface bilingue : l'interface était en français.
            if (s.InterfaceLanguage != "en")
                s.InterfaceLanguage = "fr";
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder("; Réglages de Plume / Plume settings\r\n");
                foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    object v = f.GetValue(this);
                    string text = v is bool ? ((bool)v ? "1" : "0") : Convert.ToString(v, CultureInfo.InvariantCulture);
                    sb.Append(f.Name).Append('=').Append(text).Append("\r\n");
                }
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Write("Enregistrement des réglages impossible : " + ex.Message);
            }
        }

        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
        }

        public string VoiceFor(string lang)
        {
            return lang == "en" ? VoiceEn : VoiceFr;
        }
    }

    internal static class Log
    {
        static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    string path = Path.Combine(Settings.Folder ?? Path.GetTempPath(), "plume.log");
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 512 * 1024)
                        fi.Delete();
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture)
                        + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
