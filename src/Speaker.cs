using System;
using System.Collections.Generic;
using System.Globalization;
using System.Speech.Synthesis;

namespace Plume
{
    /// <summary>Synthèse vocale (voix SAPI installées dans Windows).</summary>
    internal sealed class Speaker : IDisposable
    {
        readonly SpeechSynthesizer synth;
        public bool Enabled = true;
        public event EventHandler StateChanged;

        public Speaker()
        {
            try
            {
                synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();
                synth.SpeakStarted += delegate { RaiseState(); };
                synth.SpeakCompleted += delegate { RaiseState(); };
            }
            catch (Exception ex)
            {
                Log.Write("Synthèse vocale indisponible : " + ex.Message);
                synth = null;
            }
        }

        void RaiseState()
        {
            var h = StateChanged;
            if (h != null)
                h(this, EventArgs.Empty);
        }

        public bool IsSpeaking
        {
            get { return synth != null && synth.State == SynthesizerState.Speaking; }
        }

        /// <summary>Noms des voix installées, celles de la langue demandée en premier.</summary>
        public static List<string> Voices(string forLang)
        {
            var list = new List<string>();
            try
            {
                using (var s = new SpeechSynthesizer())
                {
                    foreach (InstalledVoice v in s.GetInstalledVoices())
                    {
                        if (!v.Enabled)
                            continue;
                        if (v.VoiceInfo.Culture.TwoLetterISOLanguageName == forLang)
                            list.Insert(0, v.VoiceInfo.Name);
                        else
                            list.Add(v.VoiceInfo.Name);
                    }
                }
            }
            catch (Exception)
            {
            }
            return list;
        }

        public void Configure(string language, string voice, int rate, int volume)
        {
            if (synth == null)
                return;
            try
            {
                synth.Rate = Math.Max(-10, Math.Min(10, rate));
                synth.Volume = Math.Max(0, Math.Min(100, volume));
                string chosen = null;
                foreach (InstalledVoice v in synth.GetInstalledVoices())
                {
                    if (!v.Enabled)
                        continue;
                    if (!string.IsNullOrEmpty(voice) && v.VoiceInfo.Name == voice)
                    {
                        chosen = v.VoiceInfo.Name;
                        break;
                    }
                    if (chosen == null && v.VoiceInfo.Culture.TwoLetterISOLanguageName == language)
                        chosen = v.VoiceInfo.Name;
                }
                if (chosen != null)
                    synth.SelectVoice(chosen);
                else
                    synth.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, new CultureInfo(language == "en" ? "en-US" : "fr-FR"));
            }
            catch (Exception ex)
            {
                Log.Write("Choix de la voix : " + ex.Message);
            }
        }

        /// <summary>Lit le texte ; <paramref name="interrupt"/> coupe ce qui est en cours.</summary>
        public void Speak(string text, bool interrupt)
        {
            if (synth == null || !Enabled || string.IsNullOrWhiteSpace(text))
                return;
            try
            {
                if (interrupt)
                    synth.SpeakAsyncCancelAll();
                synth.SpeakAsync(Prepare(text));
            }
            catch (Exception ex)
            {
                Log.Write("Lecture : " + ex.Message);
            }
        }

        /// <summary>Lit même si la parole est désactivée (bouton « Lire », test des voix).</summary>
        public void SpeakAlways(string text)
        {
            bool e = Enabled;
            Enabled = true;
            Speak(text, true);
            Enabled = e;
        }

        public void Stop()
        {
            if (synth != null)
                synth.SpeakAsyncCancelAll();
        }

        string Prepare(string text)
        {
            text = text.Trim();
            // Une lettre seule se lit mieux en minuscule (« A » serait lu comme l'article anglais).
            if (text.Length == 1 && char.IsLetter(text[0]))
                return text.ToLowerInvariant();
            return text;
        }

        public void Dispose()
        {
            if (synth != null)
                synth.Dispose();
        }
    }
}
