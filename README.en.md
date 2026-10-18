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
| Pause Plume (no suggestions, no learning) | **Predict** button or **Ctrl+Shift+F11** |
| Open the menu (writing language, start with Windows, words folder…) | right-click the toolbar or Plume's icon |
| See the version, licenses and useful links | menu → *About Plume…* |
| Quit Plume | **×** button on the toolbar, or menu → *Quit Plume* |

Drag the toolbar by its handle to move it. The **—** button hides it without stopping Plume
(double-click the notification-area icon to bring it back; on Windows 11 the icon may be tucked
behind the **^** arrow of the taskbar).

## Privacy

To learn, Plume records the words you type. Here is exactly what it keeps, and how to control it.

- **What is recorded**: `%APPDATA%\Plume\mots_fr.txt` (and `mots_en.txt`) contains, **in plain text**, the words
  typed in every application and the pairs of consecutive words, with how often they occur. It is not the text
  itself (no sentence order, no dates), but names, topics or pieces of addresses can be read in it.
  Icon menu → *Open my words folder* to look at it.
- **Where**: this file never leaves the computer, but any program running under your account can read it;
  with a roaming profile (school, company), the `%APPDATA%` folder is copied to the server.
- **What is never recorded**: what you type in **password fields**; in **terminals** (Windows Terminal, command
  prompt, PowerShell, WSL, PuTTY…), whose password prompts, such as those of `sudo` or `ssh`, do not declare
  themselves to Windows (an option turns terminals back on); in excluded applications (password managers by
  default, the list can be edited in the options); and anything you type while prediction is **turned off**
  (**Predict** button or **Ctrl+Shift+F11**). Numbers are not recorded either.
- **Limits**: a password typed in an ordinary field (some websites or programs do not use a real password field)
  is treated as text. Terminals built into another program (VS Code's, for instance) are not recognized: add that
  program to the excluded applications if needed.
- **Erase everything**: *Options → Vocabulary → Forget everything Plume has learned* (abbreviations and blocked
  words are kept). To stop learning altogether: uncheck *Learn the words I use*.
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
| `src/Windows.cs`, `src/OptionsForm.cs`, `src/AboutForm.cs` | word list, toolbar, options and About windows |
| `src/Strings.cs` | interface language: every text is written in French and English, `L.T("Lire", "Read")` |
| `src/Installer.cs`, `setup/Setup.cs` | installation, Start menu, uninstallation |
| `tools/build_lm.py` | builds the `data/*.plm` dictionaries |

Source code comments are written in French.

## Licenses

- **Code**: [MIT](LICENSE) license.
- **Dictionaries** (`data/fr.plm`, `data/en.plm`): CC BY-SA 4.0 license, see [data/LICENSE.md](data/LICENSE.md).
  They only contain **aggregated counts** (frequencies of words and of 2- or 3-word sequences),
  computed from [FrequencyWords](https://github.com/hermitdave/FrequencyWords) (OpenSubtitles, CC BY-SA 4.0),
  [Tatoeba](https://tatoeba.org) (CC BY 2.0 FR) and the [Leipzig Corpora Collection](https://wortschatz.uni-leipzig.de)
  (CC BY for downloadable corpora).

The *About Plume* window lists these licenses and the dictionary sources; the installer copies
`LICENSE` and `data/LICENSE.md` next to the program.

To rebuild them (Python 3, no dependencies):

```
python3 tools/build_lm.py --lang fr --subs fr_50k.txt \
    --leipzig fra_news_2023_1M-sentences.txt --tatoeba fra_sentences.tsv.bz2 --out data/fr.plm
python3 tools/build_lm.py --lang en --vocab 70000 --tatoeba-weight 1 --subs en_50k.txt \
    --leipzig eng_news_2023_1M-sentences.txt --tatoeba eng_sentences.tsv.bz2 --out data/en.plm
```

`tools/make_icon.ps1` regenerates the `src/plume.ico` icon.
