#!/usr/bin/env python3
"""Construit un modèle de langue Plume (data/<langue>.plm) à partir de corpus.

Le modèle contient :
  * un lexique (forme canonique + fréquence pondérée de chaque mot) ;
  * des bigrammes et trigrammes élagués (pour prédire selon le contexte).

Sources attendues (voir README) :
  --subs      liste de fréquences OpenSubtitles (« mot compte » par ligne)
  --leipzig   phrases Leipzig (« id<TAB>phrase »)
  --tatoeba   phrases Tatoeba (« id<TAB>langue<TAB>phrase », .bz2 accepté)

La tokenisation doit rester identique à celle de src/Tokenizer.cs.
"""
import argparse
import bz2
import gzip
import re
import struct
import sys
import time
import unicodedata
from array import array
from collections import Counter, defaultdict

ELISION_FR = {"l", "d", "j", "m", "n", "s", "t", "c", "qu", "jusqu", "lorsqu", "puisqu", "quoiqu"}

TOKEN_RE = re.compile(r"([^\W\d_]+(?:['\-][^\W\d_]+)*'?)|(\d+(?:[.,]\d+)*)|([.!?…]+)|([,;:])")
APOS_RE = re.compile(r"[’‘ʼ`´]")
SPACE_RE = re.compile(r"[   ]")
VALID_RE = re.compile(r"^[a-zà-öø-ÿœæĀ-ſ]+(?:['\-][a-zà-öø-ÿœæĀ-ſ]+)*'?$")

S, COMMA, NUM = "<s>", ",", "<num>"
SPECIALS = [S, COMMA, NUM]

# Mots grossiers jamais proposés (l'utilisateur peut les ajouter lui-même).
BLOCKED = {
    "fr": {"putain", "merde", "connard", "connards", "connasse", "salope", "salopes", "enculé", "enculés",
           "enculer", "bordel", "foutre", "baiser", "baise", "bite", "couille", "couilles", "con", "conne",
           "cons", "pute", "putes", "nique", "niquer", "chier", "emmerde", "emmerder", "pétasse", "pd",
           "pédé", "pédés", "batard", "bâtard", "bâtards", "enfoiré", "enfoirés", "salaud", "salauds",
           "branler", "branleur", "chiant", "chiante", "merdique", "fils de pute"},
    "en": {"fuck", "fucking", "fucked", "fucker", "fuckin", "shit", "shitty", "bitch", "bitches", "bastard",
           "asshole", "ass", "dick", "cock", "pussy", "cunt", "whore", "slut", "motherfucker", "damn",
           "goddamn", "crap", "piss", "pissed", "bullshit", "dickhead", "fag", "faggot", "nigger", "nigga",
           "retard", "twat", "wanker", "bollocks"},
}


# Prénoms récurrents des phrases d'exemple Tatoeba : on les ignore pour ne pas les proposer partout.
TATOEBA_NAMES = {"tom", "mary", "sami", "ziri", "yanni", "rima", "layla", "skura", "mennad", "baya", "taninna",
                 "tom's", "mary's", "sami's", "ziri's", "yanni's", "rima's", "layla's", "skura's", "mennad's"}


def normalize(text):
    text = unicodedata.normalize("NFC", text)
    text = APOS_RE.sub("'", text)
    return SPACE_RE.sub(" ", text)


def split_word(word, lang):
    """Sépare les élisions françaises : « l'école » -> [« l' », « école »]."""
    parts = []
    if lang == "fr":
        while True:
            i = word.find("'")
            if i <= 0 or word[:i].lower() not in ELISION_FR:
                break
            parts.append(word[: i + 1])
            word = word[i + 1:]
    if word:
        if word.endswith("'") and not (lang == "fr" and word[:-1].lower() in ELISION_FR):
            word = word[:-1]
        if word:
            parts.append(word)
    return parts


def tokenize(text, lang):
    """Renvoie la liste des jetons d'une ligne (« <s> » marque les débuts de phrase)."""
    out = [S]
    for m in TOKEN_RE.finditer(normalize(text)):
        if m.group(1):
            out.extend(split_word(m.group(1), lang))
        elif m.group(2):
            out.append(NUM)
        elif m.group(3):
            if out[-1] != S:
                out.append(S)
        elif out[-1] not in (S, COMMA):
            out.append(COMMA)
    return out


def read_lines(path, kind):
    opener = bz2.open if path.endswith(".bz2") else (gzip.open if path.endswith(".gz") else open)
    with opener(path, "rt", encoding="utf-8", errors="replace") as f:
        for line in f:
            cols = line.rstrip("\n").split("\t")
            if kind == "tatoeba" and len(cols) >= 3:
                yield cols[2]
            elif kind == "leipzig" and len(cols) >= 2:
                yield cols[1]


def is_valid(key):
    return len(key) <= 25 and VALID_RE.match(key) is not None


def log(msg):
    print("[%s] %s" % (time.strftime("%H:%M:%S"), msg), file=sys.stderr, flush=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lang", required=True, choices=["fr", "en"])
    ap.add_argument("--subs")
    ap.add_argument("--leipzig", action="append", default=[])
    ap.add_argument("--tatoeba", action="append", default=[])
    ap.add_argument("--tatoeba-weight", type=int, default=2, help="poids des phrases Tatoeba dans les n-grammes")
    ap.add_argument("--vocab", type=int, default=80000)
    ap.add_argument("--bigram-min", type=int, default=3)
    ap.add_argument("--bigram-max", type=int, default=400, help="suites gardées par mot")
    ap.add_argument("--trigram-ctx-min", type=int, default=12)
    ap.add_argument("--trigram-min", type=int, default=3)
    ap.add_argument("--trigram-max", type=int, default=60)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    lang = args.lang

    sources = [(p, "leipzig", 1) for p in args.leipzig] + [(p, "tatoeba", args.tatoeba_weight) for p in args.tatoeba]

    # Passe 1 : fréquences des mots et formes (majuscules) observées hors début de phrase.
    counts = {"leipzig": Counter(), "tatoeba": Counter()}
    forms = defaultdict(Counter)
    for path, kind, _ in sources:
        log("passe 1 : " + path)
        c = counts[kind]
        for line in read_lines(path, kind):
            prev = S
            for tok in tokenize(line, lang):
                if tok not in SPECIALS:
                    key = tok.lower()
                    if kind == "tatoeba" and key in TATOEBA_NAMES:
                        prev = tok
                        continue
                    c[key] += 1
                    if prev != S:
                        forms[key][tok] += 1
                prev = tok

    subs = Counter()
    if args.subs:
        with open(args.subs, encoding="utf-8") as f:
            for line in f:
                parts = line.split()
                if len(parts) == 2 and parts[1].isdigit():
                    w = normalize(parts[0]).lower()
                    if ("'" in w or "-" in w) and not (lang == "fr" and w.endswith("'") and w[:-1] in ELISION_FR):
                        continue
                    subs[w] += int(parts[1])

    totals = {k: max(1, sum(v.values())) for k, v in counts.items()}
    subs_total = max(1, sum(subs.values()))
    weights = {"subs": 0.4, "tatoeba": 0.3, "leipzig": 0.3}
    blocked = BLOCKED[lang]

    scored = []
    for key in set(counts["leipzig"]) | set(counts["tatoeba"]) | set(subs):
        if not is_valid(key) or key in blocked:
            continue
        corpus_n = counts["leipzig"][key] + counts["tatoeba"][key]
        if corpus_n < 3 and subs[key] == 0:
            continue
        p_corp = {k: counts[k][key] / totals[k] for k in counts}
        num = weights["tatoeba"] * p_corp["tatoeba"] + weights["leipzig"] * p_corp["leipzig"]
        den = weights["tatoeba"] + weights["leipzig"]
        if "'" not in key.rstrip("'") and "-" not in key:
            # Les sous-titres découpent « don't » en « don » + « 't » : on borne leur poids
            # par rapport aux corpus écrits pour éviter ces fragments.
            avg = num / den
            p_subs = min(subs[key] / subs_total, 5 * avg + 2e-7)
            num += weights["subs"] * p_subs
            den += weights["subs"]
        scored.append((num / den, key))
    scored.sort(reverse=True)
    scored = scored[: args.vocab]
    log("vocabulaire : %d mots" % len(scored))

    words = list(SPECIALS)
    probs = [0.0, 0.0, 0.0]
    for p, key in scored:
        f = forms.get(key)
        form = key
        if f:
            # « Cour » (de cassation) ne doit pas masquer « cour » : la minuscule l'emporte
            # dès qu'elle représente une part notable des emplois en milieu de phrase.
            best = f.most_common(1)[0][0]
            if f[key] < 0.3 * sum(f.values()):
                form = best
        words.append(form)
        probs.append(p)
    ids = {w.lower(): i for i, w in enumerate(words)}
    V = len(words)
    UNK = -1

    # Passe 2 : séquences d'identifiants (UNK coupe le contexte).
    seqs = []
    for path, kind, weight in sources:
        log("passe 2 : " + path)
        seq = array("i")
        for line in read_lines(path, kind):
            for tok in tokenize(line, lang):
                key = tok if tok in SPECIALS else tok.lower()
                seq.append(UNK if kind == "tatoeba" and key in TATOEBA_NAMES else ids.get(key, UNK))
            seq.append(-2)  # fin de ligne
        seqs.append((seq, weight))

    log("bigrammes")
    ctx_total = [0] * V
    bigrams = Counter()
    for seq, weight in seqs:
        prev = -2
        for t in seq:
            if prev >= 0 and t != -2:
                ctx_total[prev] += weight
                if t >= 0:
                    bigrams[prev * V + t] += weight
            prev = t

    log("trigrammes (%d bigrammes distincts)" % len(bigrams))
    tri_ctx = {k for k, n in bigrams.items() if n >= args.trigram_ctx_min}
    trigrams = Counter()
    for seq, weight in seqs:
        a = b = -2
        for t in seq:
            if a >= 0 and b >= 0 and t >= 0:
                k = a * V + b
                if k in tri_ctx:
                    trigrams[k * V + t] += weight
            a, b = b, t

    log("élagage")
    by_ctx = defaultdict(list)
    for k, n in bigrams.items():
        if n >= args.bigram_min:
            by_ctx[k // V].append((n, k % V))
    tri_by_ctx = defaultdict(list)
    for k, n in trigrams.items():
        if n >= args.trigram_min:
            tri_by_ctx[k // V].append((n, k % V))
    del trigrams

    scale = 1e9
    with gzip.open(args.out, "wb", compresslevel=9) as out:
        out.write(b"PLM1")
        lang_b = lang.encode()
        out.write(struct.pack("<H", len(lang_b)) + lang_b)
        out.write(struct.pack("<i", V))
        for i, w in enumerate(words):
            b = w.encode("utf-8")
            out.write(struct.pack("<H", len(b)) + b)
            out.write(struct.pack("<ii", max(1, int(probs[i] * scale)) if i >= len(SPECIALS) else 0, ctx_total[i]))
        n_bi = 0
        for i in range(V):
            lst = sorted(by_ctx.get(i, ()), reverse=True)[: args.bigram_max]
            a = array("i", [len(lst)])
            for n, w in lst:
                a.append(w)
                a.append(n)
            out.write(a.tobytes())
            n_bi += len(lst)
        ctxs = sorted(tri_by_ctx)
        out.write(struct.pack("<i", len(ctxs)))
        n_tri = 0
        for k in ctxs:
            lst = sorted(tri_by_ctx[k], reverse=True)[: args.trigram_max]
            a = array("i", [k // V, k % V, bigrams[k], len(lst)])
            for n, w in lst:
                a.append(w)
                a.append(n)
            out.write(a.tobytes())
            n_tri += len(lst)
    log("écrit %s : %d mots, %d bigrammes, %d contextes / %d trigrammes" % (args.out, V, n_bi, len(ctxs), n_tri))


if __name__ == "__main__":
    main()
