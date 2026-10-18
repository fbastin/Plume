# Code signing policy / Politique de signature du code

**English** · [Français](#français)

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

**Status:** application to SignPath Foundation in progress. Releases up to and including 1.0.3 are not signed.

## What is signed

- `Plume.exe` (the program) and `PlumeSetup.exe` (the per-user installer) attached to the
  [releases of this repository](https://github.com/fbastin/Plume/releases).
- They are built by GitHub Actions from the tagged source code only
  ([`.github/workflows/build.yml`](.github/workflows/build.yml)), and every release also carries a GitHub
  provenance attestation: `gh attestation verify PlumeSetup-X.Y.Z.exe -R fbastin/Plume`.
- Files obtained anywhere else, or built by someone else, are not covered by this policy.

## Team roles

Plume is maintained by a single person.

- Committers and reviewers: [Fabian Bastin (@fbastin)](https://github.com/fbastin). Contributions from
  anyone else go through pull requests reviewed by the maintainer.
- Approvers: [Fabian Bastin (@fbastin)](https://github.com/fbastin), who approves each signing request.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by
the user or the person installing or operating it.

In practice:

- The words Plume learns, the abbreviations and the settings stay on the computer, in `%APPDATA%\Plume`
  (see [Privacy](README.en.md#privacy)).
- Text-to-speech uses the voices installed in Windows, on the computer.
- The only network access happens when the user clicks a link in the *About* window, which opens the web
  browser.

---

## Français

Signature du code gratuite fournie par [SignPath.io](https://about.signpath.io), certificat de la
[SignPath Foundation](https://signpath.org).

**État :** candidature auprès de la SignPath Foundation en cours. Les versions jusqu'à la 1.0.3 comprise ne
sont pas signées.

### Ce qui est signé

- `Plume.exe` (le programme) et `PlumeSetup.exe` (le programme d'installation) joints aux
  [versions publiées de ce dépôt](https://github.com/fbastin/Plume/releases).
- Ils sont compilés par GitHub Actions, uniquement à partir du code source étiqueté
  ([`.github/workflows/build.yml`](.github/workflows/build.yml)), et chaque version porte aussi une
  attestation de provenance GitHub : `gh attestation verify PlumeSetup-X.Y.Z.exe -R fbastin/Plume`.
- Les fichiers obtenus ailleurs, ou compilés par quelqu'un d'autre, ne sont pas couverts par cette politique.

### Rôles

Plume est maintenu par une seule personne.

- Auteur et relecteur : [Fabian Bastin (@fbastin)](https://github.com/fbastin). Les contributions de toute
  autre personne passent par des demandes de fusion (« pull requests ») relues par le mainteneur.
- Approbateur : [Fabian Bastin (@fbastin)](https://github.com/fbastin), qui approuve chaque demande de
  signature.

### Vie privée

Ce programme ne transmet aucune information à d'autres systèmes en réseau, sauf à la demande expresse de
la personne qui l'utilise, l'installe ou l'administre.

Concrètement :

- les mots appris par Plume, les abréviations et les réglages restent sur l'ordinateur, dans
  `%APPDATA%\Plume` (voir [Vie privée](README.md#vie-privée)) ;
- la synthèse vocale utilise les voix installées dans Windows, sur l'ordinateur ;
- le seul accès au réseau a lieu quand on clique sur un lien de la fenêtre *À propos*, qui ouvre le
  navigateur.
