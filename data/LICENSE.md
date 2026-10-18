# Licence des dictionnaires / Dictionary license

**Français** · [English](#english)

Les fichiers `fr.plm` et `en.plm` de ce dossier ne sont **pas** couverts par la licence MIT du code.
Ils contiennent uniquement des comptes agrégés (fréquences de mots et de suites de 2 ou 3 mots)
calculés à partir des sources ci-dessous, et sont distribués sous licence
**[Creative Commons Attribution – Partage dans les mêmes conditions 4.0 (CC BY-SA 4.0)](https://creativecommons.org/licenses/by-sa/4.0/deed.fr)**,
imposée par la première source.

| Source | Licence | Attribution |
|---|---|---|
| [FrequencyWords](https://github.com/hermitdave/FrequencyWords), listes de fréquences tirées d'OpenSubtitles 2018 | CC BY-SA 4.0 | Hermit Dave ; [OpenSubtitles](https://www.opensubtitles.org) |
| [Tatoeba](https://tatoeba.org), phrases d'exemple | CC BY 2.0 FR | contributeurs de Tatoeba |
| [Leipzig Corpora Collection](https://wortschatz.uni-leipzig.de), corpus d'actualités 2023 (1 million de phrases, français et anglais) | CC BY pour les corpus téléchargeables | D. Goldhahn, T. Eckart, U. Quasthoff, *Building Large Monolingual Dictionaries at the Leipzig Corpora Collection: From 100 to 200 Languages*, LREC 2012 |

Les conditions générales du site de Leipzig interdisent par ailleurs l'**usage commercial** de leurs données
sans autorisation écrite : avant tout usage commercial de ces dictionnaires, vérifiez leurs conditions ou
reconstruisez les dictionnaires sans ce corpus (`tools/build_lm.py`).

---

## English

The `fr.plm` and `en.plm` files in this folder are **not** covered by the MIT license of the code.
They only contain aggregated counts (frequencies of words and of 2- or 3-word sequences) computed from
the sources below, and are distributed under the
**[Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0)](https://creativecommons.org/licenses/by-sa/4.0/)**
license, required by the first source.

| Source | License | Attribution |
|---|---|---|
| [FrequencyWords](https://github.com/hermitdave/FrequencyWords), frequency lists from OpenSubtitles 2018 | CC BY-SA 4.0 | Hermit Dave; [OpenSubtitles](https://www.opensubtitles.org) |
| [Tatoeba](https://tatoeba.org), example sentences | CC BY 2.0 FR | Tatoeba contributors |
| [Leipzig Corpora Collection](https://wortschatz.uni-leipzig.de), 2023 news corpora (1 million sentences, French and English) | CC BY for downloadable corpora | D. Goldhahn, T. Eckart, U. Quasthoff, *Building Large Monolingual Dictionaries at the Leipzig Corpora Collection: From 100 to 200 Languages*, LREC 2012 |

The general terms of the Leipzig website also forbid **commercial use** of their data without written
permission: before any commercial use of these dictionaries, check their terms or rebuild the dictionaries
without that corpus (`tools/build_lm.py`).
