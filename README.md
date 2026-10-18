# Plume — assistant d'écriture pour Windows

**Français** · [English](README.en.md)

Plume aide à écrire dans **n'importe quelle application** (Word, navigateur, courriel, Bloc-notes…),
à la manière de WordQ :

- **Prédiction de mots** : pendant la frappe, une liste de mots apparaît sous le curseur.
  On en choisit un avec son chiffre (ou à la souris) ; Plume complète le mot et ajoute l'espace.
- **Mot suivant** : après une espace, Plume propose la suite probable (« il y » → *a*, *avait*…).
- **Aide orthographique** : les mots mal orthographiés sont reconnus à leur son
  (« otomob » → *automobile*, « fesai » → *faisait*), et les accents sont facultatifs (« ecol » → *école*).
- **Lecture à voix haute** : lecture du mot survolé, du mot choisi, de chaque phrase terminée,
  et du texte sélectionné (bouton **Lire** ou **Ctrl+Maj+F12**).
- **Apprentissage** : Plume retient les mots et enchaînements que vous utilisez ; les mots nouveaux
  sont proposés après quelques utilisations (sauf s'ils ressemblent à une faute d'un mot connu).
- **Abréviations** : « bjr » → *bonjour*, « svp » → *s'il vous plaît* (modifiables).
- **Bilingue** : on écrit en français ou en anglais (bascule en un clic), et l'interface (menus,
  options, installation) est en français ou en anglais (*Options → Apparence → Langue de l'interface* ;
  par défaut, celle de Windows).
- Thèmes (clair, sombre, crème, contraste élevé), police et taille au choix (ex. police pour dyslexie).

## Installer

```
build.cmd          construit bin\Plume.exe et bin\PlumeSetup.exe (aucun outil à installer :
                   utilise le compilateur C# fourni avec Windows)
```

**`bin\PlumeSetup.exe`** est un programme d'installation en un seul fichier (Plume et ses
dictionnaires sont dedans). Double-cliquez dessus, puis **Installer** :

- Plume est copié dans `%LOCALAPPDATA%\Programs\Plume`, pour votre compte uniquement :
  **aucun droit d'administrateur** n'est demandé ;
- il apparaît dans le **menu Démarrer** (tapez « Plume »), avec en option un raccourci sur le Bureau
  et le lancement à l'ouverture de session ;
- il est inscrit dans **Paramètres → Applications → Applications installées**, d'où on le
  **désinstalle** comme n'importe quel programme. La désinstallation demande si l'on veut aussi
  effacer les mots appris et les réglages (par défaut, ils sont conservés pour une réinstallation).

Relancer `PlumeSetup.exe` met Plume à jour (le programme ouvert est fermé puis remplacé).
Pour un déploiement sans question : `PlumeSetup.exe /quiet` (ajouter `/nolaunch` pour ne pas
démarrer Plume). Désinstallation sans question : `"%LOCALAPPDATA%\Programs\Plume\Plume.exe" --uninstall --quiet`.

`PlumeSetup.exe` n'est pas signé numériquement : sur un autre ordinateur, Windows SmartScreen peut
afficher « Windows a protégé votre ordinateur » → *Informations complémentaires* → *Exécuter quand même*.

Sans installation, `bin\Plume.exe` se lance aussi directement (le dossier `bin` est autonome).

## Utilisation

| Action | Comment |
|---|---|
| Choisir un mot proposé | chiffre **1** à **9** (ou **F1**–**F9**, ou clic, selon les options) |
| Taper un chiffre alors que la liste est affichée | **Échap** masque la liste, puis tapez le chiffre |
| Clavier AZERTY | les chiffres s'obtiennent avec **Maj** ou le **pavé numérique** ; on peut aussi choisir F1–F9 |
| Entendre un mot de la liste | passer la souris dessus |
| Ne plus voir un mot | clic droit sur le mot → *Ne plus proposer* |
| Lire la sélection (ou la dernière phrase) | bouton **Lire** ou **Ctrl+Maj+F12** (rappuyer pour arrêter) |
| Activer / désactiver la prédiction | bouton **Prédire** ou **Ctrl+Maj+F11** |
| Ouvrir le menu (langue d'écriture, démarrage avec Windows, dossier des mots…) | clic droit sur la barre d'outils ou sur l'icône de Plume |
| Quitter Plume | bouton **×** de la barre d'outils, ou menu → *Quitter Plume* |

La barre d'outils se déplace en la faisant glisser par sa poignée. Le bouton **—** la masque sans
arrêter Plume (double-clic sur l'icône de la zone de notification pour la retrouver ; sous Windows 11,
l'icône peut être rangée derrière la flèche **^** de la barre des tâches).

## Vie privée

- Tout reste sur l'ordinateur : réglages, mots appris et abréviations sont dans `%APPDATA%\Plume`
  (menu de l'icône → *Ouvrir le dossier de mes mots*).
- Plume ne prédit rien et n'apprend rien dans les **champs de mot de passe**, ni dans les
  applications exclues (gestionnaires de mots de passe par défaut ; liste modifiable dans les options).
- Le journal `plume.log` ne contient jamais le texte tapé ou lu.

## Limites connues

- Plume ne peut pas écrire dans les applications lancées **en administrateur** (protection de Windows),
  sauf s'il est lui-même lancé en administrateur.
- Si une application ne révèle pas la position de son curseur, la liste s'affiche près du dernier clic
  ou sous la barre d'outils (option *position fixe* pour la placer où l'on veut).
- La lecture de la sélection passe par l'accessibilité de Windows ; à défaut, Plume fait une copie
  (Ctrl+C) et remet ensuite le presse-papiers comme il était. Dans un terminal, il ne lit que ce qui a été tapé.
- Les voix sont celles de Windows (SAPI) : par défaut *Hortense* (français) et *David* / *Zira* (anglais).
  D'autres voix peuvent s'ajouter dans les paramètres de Windows (Heure et langue → Voix).
- Par rapport à WordQ, il manque la dictée vocale (SpeakQ), les exemples d'emploi des mots
  et le surlignage pendant la lecture.

## Tests

```
build.cmd test     tests du moteur : découpage, accents, phonétique, prédictions FR/EN, apprentissage, vitesse
build.cmd e2e      test de bout en bout : ouvre une fenêtre « Test Plume » et y tape du texte (~30 s)
```

Le test de bout en bout prend le clavier pendant une trentaine de secondes : ne pas utiliser
l'ordinateur pendant ce temps (il s'arrête de lui-même si sa fenêtre perd le focus, sans rien taper ailleurs).

Pour essayer l'installation sans toucher au vrai menu Démarrer, définir la variable d'environnement
`PLUME_INSTALL_TEST` (un dossier) : fichiers, raccourcis et registre (`HKCU\Software\PlumeInstallTest`)
y sont alors redirigés.

## Organisation du code

| Fichier | Rôle |
|---|---|
| `src/App.cs` | démarrage, chef d'orchestre (frappe → prédiction → affichage, lecture, menus) |
| `src/InputHooks.cs` | crochets clavier/souris globaux (fil dédié), touches mortes, insertion des mots |
| `src/Tokenizer.cs` | découpage du texte (élisions « l' », traits d'union, nombres, phrases) |
| `src/Predictor.cs`, `src/LanguageModel.cs` | moteur de prédiction (trigrammes, bigrammes, fréquences, phonétique) |
| `src/TextUtil.cs` | comparaison sans accents, clé phonétique, distance d'édition |
| `src/UserModel.cs` | mots appris, mots refusés, abréviations |
| `src/Accessibility.cs` | position du curseur, champ mot de passe, sélection (UI Automation / MSAA) |
| `src/Speaker.cs` | synthèse vocale |
| `src/Windows.cs`, `src/OptionsForm.cs` | liste de mots, barre d'outils, fenêtre d'options |
| `src/Strings.cs` | langue de l'interface : chaque texte est écrit en français et en anglais, `L.T("Lire", "Read")` |
| `src/Installer.cs`, `setup/Setup.cs` | installation, menu Démarrer, désinstallation |
| `tools/build_lm.py` | construction des dictionnaires `data/*.plm` |

Les commentaires du code source sont en français.

## Licences

- **Code** : licence [MIT](LICENSE).
- **Dictionnaires** (`data/fr.plm`, `data/en.plm`) : licence CC BY-SA 4.0, voir [data/LICENSE.md](data/LICENSE.md).
  Ils contiennent uniquement des **comptes agrégés** (fréquences de mots et de suites de 2 ou 3 mots),
  calculés à partir de [FrequencyWords](https://github.com/hermitdave/FrequencyWords) (OpenSubtitles, CC BY-SA 4.0),
  de [Tatoeba](https://tatoeba.org) (CC BY 2.0 FR) et de la [Leipzig Corpora Collection](https://wortschatz.uni-leipzig.de)
  (CC BY ; usage commercial soumis à autorisation selon leurs conditions générales).

Pour les reconstruire (Python 3, sans dépendance) :

```
python3 tools/build_lm.py --lang fr --subs fr_50k.txt \
    --leipzig fra_news_2023_1M-sentences.txt --tatoeba fra_sentences.tsv.bz2 --out data/fr.plm
python3 tools/build_lm.py --lang en --vocab 70000 --tatoeba-weight 1 --subs en_50k.txt \
    --leipzig eng_news_2023_1M-sentences.txt --tatoeba eng_sentences.tsv.bz2 --out data/en.plm
```

`tools/make_icon.ps1` régénère l'icône `src/plume.ico`.
