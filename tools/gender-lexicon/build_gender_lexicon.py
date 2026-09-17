"""
Builds the masculine <-> feminine form lexicon used by the deterministic gender
review from a pinned SGJP dictionary dump (Morfeusz 2 project, BSD 2-clause).

Output: src/NapisyPL.Core/Resources/gender_forms.sgjp.tsv.gz
Each line is   <category>\t<masculine form>\t<feminine form>
  self        first person singular, past and conditional   (chciałem / chciałam)
  addressee   second person singular, past and conditional  (chciałeś / chciałaś)
  predicate   nominative singular adjective or passive participle (gotowy / gotowa)

A form is dropped when it would be unsafe to rewrite on its own:
  - a verb form that also has a reading other than the matching past/conditional
    slot (a noun, a present-tense form, ...), because the rewriter sees words, not tags;
  - a form that would map to two different targets.
Predicates are only ever rewritten directly after "jestem"/"jesteś", so they keep
homographs but still drop conflicting targets.

Usage:
    python build_gender_lexicon.py [path-to-sgjp-YYYYMMDD.tab.gz]
Without an argument the pinned dump is downloaded.
"""
from __future__ import annotations

import collections
import gzip
import io
import pathlib
import sys
import urllib.request

SGJP_VERSION = "20260823"
SGJP_URL = f"https://download.sgjp.pl/morfeusz/{SGJP_VERSION}/sgjp-{SGJP_VERSION}.tab.gz"

REPO = pathlib.Path(__file__).resolve().parents[2]
OUTPUT = REPO / "src" / "NapisyPL.Core" / "Resources" / "gender_forms.sgjp.tsv.gz"
NOTICE = REPO / "src" / "NapisyPL.Core" / "Resources" / "gender_forms.sgjp.NOTICE.txt"

MASCULINE = "m1.m2.m3"
FEMININE = "f"


def open_dump(argument: str | None) -> io.TextIOBase:
    if argument:
        return gzip.open(argument, "rt", encoding="utf-8")
    print(f"downloading {SGJP_URL}", file=sys.stderr)
    data = urllib.request.urlopen(SGJP_URL, timeout=600).read()
    return io.TextIOWrapper(gzip.GzipFile(fileobj=io.BytesIO(data)), encoding="utf-8")


def read_copyright(stream) -> tuple[list[str], list[str]]:
    header, first_data = [], []
    inside = False
    for line in stream:
        if line.startswith("#<COPYRIGHT>"):
            inside = True
            continue
        if line.startswith("#</COPYRIGHT>"):
            break
        if inside:
            header.append(line.rstrip("\n"))
    return header, first_data


def classify(tag: str):
    """Returns (category, gender, key) for a slot we care about, else None."""
    parts = tag.split(":")
    kind = parts[0]
    if kind in ("praet", "cond") and len(parts) >= 4 and parts[1] == "sg":
        gender, person = parts[2], parts[3]
        if gender not in (MASCULINE, FEMININE):
            return None
        if person == "pri":
            return "self", gender, kind
        if person == "sec":
            return "addressee", gender, kind
        return None
    if kind in ("adj", "ppas") and len(parts) >= 4 and parts[1] == "sg" and "nom" in parts[2].split("."):
        gender = parts[3]
        if gender not in (MASCULINE, FEMININE):
            return None
        if kind == "adj" and (len(parts) < 5 or parts[4] != "pos"):
            return None
        if kind == "ppas" and "neg" in parts:
            return None
        return "predicate", gender, kind
    return None


def main() -> int:
    stream = open_dump(sys.argv[1] if len(sys.argv) > 1 else None)
    copyright_lines, _ = read_copyright(stream)

    # slots[(category, lemma, kind, aspect)][gender] -> list of (form, qualified)
    slots: dict[tuple, dict[str, list[tuple[str, bool]]]] = collections.defaultdict(
        lambda: collections.defaultdict(list))
    # every reading of every form, used to reject ambiguous verb forms
    readings: dict[str, set[str]] = collections.defaultdict(set)

    for line in stream:
        if not line or line.startswith("#"):
            continue
        columns = line.rstrip("\n").split("\t")
        if len(columns) < 3:
            continue
        form, lemma, tag = columns[0], columns[1], columns[2]
        qualifiers = columns[4] if len(columns) > 4 else ""
        lower = form.lower()
        if lower != form and form[:1].isupper():
            # proper names are not rewritten
            readings[lower].add("proper")
            continue

        classified = classify(tag)
        if classified is None:
            readings[lower].add(tag.split(":")[0])
            continue

        category, gender, kind = classified
        readings[lower].add(f"{category}:{gender}")
        aspect = next((p for p in tag.split(":") if p in ("perf", "imperf", "imperf.perf")), "")
        slots[(category, lemma, kind, aspect)][gender].append((lower, bool(qualifiers.strip())))

    def preferred(candidates: list[tuple[str, bool]]) -> str:
        unqualified = sorted({form for form, qualified in candidates if not qualified})
        pool = unqualified or sorted({form for form, _ in candidates})
        return pool[0]

    targets: dict[tuple[str, str, str], set[str]] = collections.defaultdict(set)
    for (category, _lemma, _kind, _aspect), genders in slots.items():
        if MASCULINE not in genders or FEMININE not in genders:
            continue
        masculine_forms = {form for form, _ in genders[MASCULINE]}
        feminine_forms = {form for form, _ in genders[FEMININE]}
        feminine_target = preferred(genders[FEMININE])
        masculine_target = preferred(genders[MASCULINE])
        for form in masculine_forms:
            targets[(category, "m", form)].add(feminine_target)
        for form in feminine_forms:
            targets[(category, "f", form)].add(masculine_target)

    def safe_verb_form(category: str, gender: str, form: str) -> bool:
        allowed = {f"{category}:{MASCULINE if gender == 'm' else FEMININE}"}
        return readings[form] <= allowed

    pairs: set[tuple[str, str, str]] = set()
    dropped = collections.Counter()
    for (category, gender, form), options in targets.items():
        if len(options) != 1:
            dropped[f"{category}:conflicting_target"] += 1
            continue
        if category != "predicate" and not safe_verb_form(category, gender, form):
            dropped[f"{category}:homograph"] += 1
            continue
        target = next(iter(options))
        if form == target:
            continue
        pairs.add((category, form, target) if gender == "m" else (category, target, form))

    # A pair is only emitted when both directions survived; otherwise a rewrite
    # could not be reversed by the same data and the two maps would disagree.
    forward = {(c, m) for c, m, _ in pairs}
    backward = {(c, f) for c, _, f in pairs}
    usable = sorted(
        (c, m, f) for c, m, f in pairs
        if (c, m) in forward and (c, f) in backward
        and targets.get((c, "m", m)) == {f}
        and targets.get((c, "f", f)) == {m}
        and (c == "predicate" or (safe_verb_form(c, "m", m) and safe_verb_form(c, "f", f))))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with gzip.GzipFile(filename="", mode="wb", fileobj=open(OUTPUT, "wb"), mtime=0) as raw:
        with io.TextIOWrapper(raw, encoding="utf-8", newline="\n") as writer:
            writer.write(f"#sgjp {SGJP_VERSION}\n")
            for category, masculine, feminine in usable:
                writer.write(f"{category}\t{masculine}\t{feminine}\n")

    NOTICE.write_text(
        "gender_forms.sgjp.tsv.gz is derived from the SGJP dictionary "
        f"(version {SGJP_VERSION}), distributed with Morfeusz 2.\n"
        "https://morfeusz.sgjp.pl/\n\n" + "\n".join(copyright_lines).strip() + "\n",
        encoding="utf-8")

    counts = collections.Counter(category for category, _, _ in usable)
    print(f"pairs: {dict(counts)}", file=sys.stderr)
    print(f"dropped: {dict(dropped)}", file=sys.stderr)
    print(f"written: {OUTPUT} ({OUTPUT.stat().st_size / 1024:.0f} KB)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
