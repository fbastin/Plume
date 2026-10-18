# Plume — writing assistant for Windows

[Français](README.md) · **English**

Plume helps you write in **any application** (Word, web browser, email, Notepad…),
in the spirit of WordQ:

- **Word prediction**: as you type, a list of words appears below the cursor.
  Pick one with its number (or the mouse); Plume completes the word and adds the space.
- **Next word**: after a space, Plume suggests what is likely to come next ("I don't" → *know*, *think*…).
- **Spelling help**: misspelled words are recognized by their sound
  ("becuas" → *because*, French "otomob" → *automobile*), and accents are optional ("ecol" → *école*).
- **Reading aloud**: the word under the mouse, the chosen word, each finished sentence,
  and the selected text (**Read** button or **Ctrl+Shift+F12**).
- **Learning**: Plume remembers the words and word sequences you use; new words are suggested
  after a few uses (unless they look like a misspelling of a known word).
- **Abbreviations**: "btw" → *by the way*, "bjr" → *bonjour* (editable).
- **Bilingual**: write in English or French (one-click switch), with the interface (menus, options,
  setup) in English or French (*Options → Appearance → Interface language*; Windows' language by default).
- Themes (light, dark, cream, high contrast), any font and size (e.g. a dyslexia-friendly font).

## Install

```
build.cmd          builds bin\Plume.exe and bin\PlumeSetup.exe (nothing to install:
                   uses the C# compiler that ships with Windows)
```

**`bin\PlumeSetup.exe`** is a single-file installer (Plume and its dictionaries are inside).
Double-click it, then **Install**:

- Plume is copied to `%LOCALAPPDATA%\Programs\Plume`, for your account only:
  **no administrator rights** are required;
- it appears in the **Start menu** (type "Plume"), with an optional desktop shortcut
  and start at sign-in;
- it is registered in **Settings → Apps → Installed apps**, where you **uninstall** it like any
  other program. Uninstalling asks whether to also delete your learned words and settings
  (by default they are kept for a future reinstallation).

Running `PlumeSetup.exe` again updates Plume (the running program is closed, then replaced).
Unattended install: `PlumeSetup.exe /quiet` (add `/nolaunch` to not start Plume afterwards).
Unattended uninstall: `"%LOCALAPPDATA%\Programs\Plume\Plume.exe" --uninstall --quiet`.

`PlumeSetup.exe` is not digitally signed: on another computer, Windows SmartScreen may show
"Windows protected your PC" → *More info* → *Run anyway*.

Without installing, `bin\Plume.exe` also runs directly (the `bin` folder is self-contained).

## Usage

| Action | How |
|---|---|
| Choose a suggested word | number key **1** to **9** (or **F1**–**F9**, or a click, depending on the options) |
| Type a digit while the list is shown | **Esc** hides the list, then type the digit |
| AZERTY keyboard | digits come with **Shift** or the **numeric keypad**; F1–F9 can be chosen instead |
| Hear a word from the list | hover over it with the mouse |
| Stop seeing a word | right-click the word → *Never suggest* |
| Read the selection (or the last sentence) | **Read** button or **Ctrl+Shift+F12** (press again to stop) |
| Turn word prediction on / off | **Predict** button or **Ctrl+Shift+F11** |
| Open the menu (writing language, start with Windows, words folder…) | right-click the toolbar or Plume's icon |
| Quit Plume | **×** button on the toolbar, or menu → *Quit Plume* |

Drag the toolbar by its handle to move it. The **—** button hides it without stopping Plume
(double-click the notification-area icon to bring it back; on Windows 11 the icon may be tucked
behind the **^** arrow of the taskbar).

## Privacy

- Everything stays on the computer: settings, learned words and abbreviations are in `%APPDATA%\Plume`
  (icon menu → *Open my words folder*).
- Plume predicts nothing and learns nothing in **password fields**, nor in excluded applications
  (password managers by default; the list can be edited in the options).
- The `plume.log` file never contains the text you type or have read aloud.

## Known limitations

- Plume cannot type into applications running **as administrator** (a Windows protection),
  unless Plume itself runs as administrator.
- If an application does not expose its cursor position, the list appears near the last click
  or below the toolbar (the *fixed position* option lets you place it anywhere).
- Reading the selection uses Windows accessibility; when that is unavailable, Plume copies the
  selection (Ctrl+C) and then restores the clipboard as it was. In a terminal, it only reads what was typed.
- Voices are the Windows (SAPI) voices: *Hortense* (French) and *David* / *Zira* (English) by default.
  More voices can be added in Windows settings (Time & language → Speech).
- Compared with WordQ, voice dictation (SpeakQ), word usage examples and highlighting while
  reading are missing.

## Tests

```
build.cmd test     engine tests: tokenizing, accents, phonetics, FR/EN predictions, learning, speed
build.cmd e2e      end-to-end test: opens a "Test Plume" window and types text into it (~30 s)
```

The end-to-end test takes over the keyboard for about thirty seconds: do not use the computer
meanwhile (it stops by itself if its window loses focus, without typing anywhere else).

To try the installer without touching the real Start menu, set the `PLUME_INSTALL_TEST`
environment variable (a folder): files, shortcuts and registry entries (`HKCU\Software\PlumeInstallTest`)
are then redirected there.

## Code layout

| File | Role |
|---|---|
| `src/App.cs` | startup, orchestration (typing → prediction → display, reading, menus) |
| `src/InputHooks.cs` | global keyboard/mouse hooks (dedicated thread), dead keys, word insertion |
| `src/Tokenizer.cs` | text splitting (French elisions "l'", hyphens, numbers, sentences) |
| `src/Predictor.cs`, `src/LanguageModel.cs` | prediction engine (trigrams, bigrams, frequencies, phonetics) |
| `src/TextUtil.cs` | accent-insensitive matching, phonetic key, edit distance |
| `src/UserModel.cs` | learned words, blocked words, abbreviations |
| `src/Accessibility.cs` | cursor position, password fields, selection (UI Automation / MSAA) |
| `src/Speaker.cs` | speech synthesis |
| `src/Windows.cs`, `src/OptionsForm.cs` | word list, toolbar, options window |
| `src/Strings.cs` | interface language: every text is written in French and English, `L.T("Lire", "Read")` |
| `src/Installer.cs`, `setup/Setup.cs` | installation, Start menu, uninstallation |
| `tools/build_lm.py` | builds the `data/*.plm` dictionaries |

Source code comments are written in French.

## Dictionaries and data licenses

The `data/fr.plm` and `data/en.plm` files only contain **aggregated counts**
(frequencies of words and of 2- or 3-word sequences), computed from:

- [FrequencyWords](https://github.com/hermitdave/FrequencyWords) (Hermit Dave, from OpenSubtitles 2018) — CC BY-SA 4.0;
- [Tatoeba](https://tatoeba.org) (example sentences) — CC BY 2.0 FR;
- [Leipzig Corpora Collection](https://wortschatz.uni-leipzig.de) (2023 news, 1 million sentences) —
  check their terms of use before any redistribution.

Because FrequencyWords is licensed CC BY-SA, the derived dictionaries must be shared under the
same terms if they are redistributed.

To rebuild them (Python 3, no dependencies):

```
python3 tools/build_lm.py --lang fr --subs fr_50k.txt \
    --leipzig fra_news_2023_1M-sentences.txt --tatoeba fra_sentences.tsv.bz2 --out data/fr.plm
python3 tools/build_lm.py --lang en --vocab 70000 --tatoeba-weight 1 --subs en_50k.txt \
    --leipzig eng_news_2023_1M-sentences.txt --tatoeba eng_sentences.tsv.bz2 --out data/en.plm
```

`tools/make_icon.ps1` regenerates the `src/plume.ico` icon.
