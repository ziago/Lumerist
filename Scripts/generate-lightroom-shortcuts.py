#!/usr/bin/env python3
"""Checks Shared/lightroom-shortcuts.json (Lightroom Classic's own default
keys, from Adobe's public help) and writes the Mac app's table from it,
Sources/LumeristCore/LightroomShortcutData.swift. Run from anywhere:
python3 Scripts/generate-lightroom-shortcuts.py [--check]
--check exits 1 when the file is invalid or the Swift table is out of date
(Scripts/check-parity.py runs it). The Windows app reads the JSON itself."""
import json, pathlib, re, sys

root = pathlib.Path(__file__).resolve().parent.parent
source = root / "Shared/lightroom-shortcuts.json"
target = root / "Sources/LumeristCore/LightroomShortcutData.swift"
data = json.loads(source.read_text(encoding="utf-8"))

KEYS = ({chr(c) for c in range(ord("A"), ord("Z") + 1)} | {str(d) for d in range(10)}
        | {f"F{n}" for n in range(1, 13)}
        | {"LeftOf1", "Tab", "Esc", "Space", "Return", "Backspace", "ForwardDelete", "Home", "End", "PageUp", "PageDown",
           "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "[", "]", "=", "-", ",", ".", "/", ";", "'", "\\"})
MODIFIERS = ["Control", "Alt", "Shift", "Primary"]
CONTEXTS = {"library": ".library", "develop": ".develop", "import": ".importWindow"}
ROLES = {"include", "exclude", "toggle"}
# The Lumerist commands that press one of these entries (FallbackShortcuts, AppCommand).
FALLBACKS = {"autoTone", "undo", "redo", "flagPick", "flagReject", "flagRemove", "showClipping", "pasteSettingsFromPrevious"}
LANGUAGE_CODE = re.compile(r"[a-z]{2,3}(_[a-z0-9]{2,8})?")


def chord_problems(name, chord, platform):
    found = []
    *modifiers, key = chord.split("+")
    if key not in KEYS:
        found.append(f"{name}: unknown key {key!r} in {chord}")
    if any(m not in MODIFIERS for m in modifiers) or len(set(modifiers)) != len(modifiers):
        found.append(f"{name}: bad modifiers in {chord}")
    if platform == "windows" and "Control" in modifiers:
        found.append(f"{name}: Windows chords write Ctrl as Primary ({chord})")
    return found


def entries():
    """(section id, shortcut, its contexts) for every English entry, in file order."""
    for section in data.get("sections", []):
        for shortcut in section.get("shortcuts", []):
            yield section.get("id"), shortcut, shortcut.get("contexts", section.get("contexts", []))


def normal(chord):
    *modifiers, key = chord.split("+")
    return (frozenset(modifiers), key)


def language_problems():
    found = []
    english = {(section, shortcut["action"]): (shortcut, contexts) for section, shortcut, contexts in entries()}
    seen_codes, ids = {}, set()
    languages = data.get("languages", [])
    if not languages or languages[0].get("id") != "en" or languages[0].get("overrides"):
        found.append("languages: the first entry must be English (en), with no overrides")
    for language in languages:
        where = f"languages/{language.get('id', '<no id>')}"
        for field in ("id", "name", "codes", "importToggle", "source", "readFrom", "retrieved", "overrides"):
            if field not in language:
                found.append(f"{where}: missing {field}")
        if language.get("id") in ids:
            found.append(f"{where}: duplicate id")
        ids.add(language.get("id"))
        if language.get("importToggle") not in ("`", "<"):
            found.append(f"{where}: importToggle must be ` or <")
        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", language.get("retrieved", "")):
            found.append(f"{where}: retrieved must be a date (YYYY-MM-DD)")
        if language.get("source") is None and not language.get("sourceNote"):
            found.append(f"{where}: a language without a page needs a sourceNote")
        for code in language.get("codes", []):
            if not LANGUAGE_CODE.fullmatch(code):
                found.append(f"{where}: {code!r} is not a language code as the apps write it")
            if code in seen_codes:
                found.append(f"{where}: code {code} is also {seen_codes[code]}'s")
            seen_codes[code] = language.get("id")
        # This language's chords: English, with its overrides.
        merged = {key: {"mac": entry["mac"], "windows": entry["windows"]} for key, (entry, _) in english.items()}
        overridden = set()
        for override in language.get("overrides", []):
            key = (override.get("section"), override.get("action"))
            name = f"{where}: {key[0]}/{key[1]}"
            if key not in english:
                found.append(f"{name}: no such English entry")
                continue
            if key in overridden:
                found.append(f"{name}: overridden twice")
            overridden.add(key)
            if not override.get("evidence"):
                found.append(f"{name}: an override needs its evidence")
            if not isinstance(override.get("press"), bool):
                found.append(f"{name}: press must be true or false")
            if override.get("press") and english[key][0].get("fallbackFor") not in FALLBACKS:
                found.append(f"{name}: press needs an entry with fallbackFor")
            for platform in ("mac", "windows"):
                if not isinstance(override.get(platform), list):
                    found.append(f"{name}: {platform} must be a list")
                    continue
                for chord in override[platform]:
                    found += chord_problems(name, chord, platform)
                merged[key][platform] = override[platform]
        # An override must not land on another action's key in the same place
        # (the same action listed in two tables, with the same English keys, is one).
        for key in overridden:
            entry, contexts = english[key]
            for platform in ("mac", "windows"):
                mine = {normal(c) for c in merged[key][platform]}
                for other, (other_entry, other_contexts) in english.items():
                    if other == key or not set(contexts) & set(other_contexts):
                        continue
                    if other_entry[platform] == entry[platform]:
                        continue
                    shared = mine & {normal(c) for c in merged[other][platform]}
                    if shared:
                        found.append(f"{where}: {key[0]}/{key[1]} clashes with {other[0]}/{other[1]} on {platform}")
    return found


def problems():
    found = []
    for section in data.get("sections", []):
        where = section.get("id", "<no id>")
        for field in ("id", "title", "source", "retrieved", "contexts", "shortcuts"):
            if field not in section:
                found.append(f"{where}: missing {field}")
        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", section.get("retrieved", "")):
            found.append(f"{where}: retrieved must be a date (YYYY-MM-DD)")
        if section.get("source") == "community" and not (section.get("communityReported") and section.get("sourceNote")):
            found.append(f"{where}: community entries need communityReported and a sourceNote")
        for shortcut in section.get("shortcuts", []):
            name = f"{where}: {shortcut.get('action', '<no action>')}"
            contexts = shortcut.get("contexts", section.get("contexts", []))
            if not contexts or any(context not in CONTEXTS for context in contexts):
                found.append(f"{name}: contexts must be library, develop or import")
            fallback = shortcut.get("fallbackFor")
            if fallback is not None and fallback not in FALLBACKS:
                found.append(f"{name}: fallbackFor must be one of {', '.join(sorted(FALLBACKS))}")
            role = shortcut.get("importRole")
            if role is not None and (role not in ROLES or contexts != ["import"]):
                found.append(f"{name}: importRole must be include, exclude or toggle, in the Import window only")
            for platform in ("mac", "windows"):
                if not isinstance(shortcut.get(platform), list):
                    found.append(f"{name}: {platform} must be a list")
                    continue
                for chord in shortcut[platform]:
                    found += chord_problems(name, chord, platform)
    fallbacks = [s.get("fallbackFor") for _, s, _ in entries() if s.get("fallbackFor")]
    if sorted(fallbacks) != sorted(FALLBACKS):
        found.append(f"fallbackFor must name each of {', '.join(sorted(FALLBACKS))} once")
    return found + language_problems()


def swift_string(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def generate():
    rows, index = [], {}
    for section in data["sections"]:
        community = bool(section.get("communityReported"))
        for shortcut in section["shortcuts"]:
            if not shortcut["mac"]:
                continue
            index[(section["id"], shortcut["action"])] = len(rows)
            contexts = ", ".join(CONTEXTS[c] for c in shortcut.get("contexts", section["contexts"]))
            extras = ""
            if community:
                extras += ", isCommunityReported: true"
            if shortcut.get("importRole"):
                extras += f", importRole: .{shortcut['importRole']}"
            if shortcut.get("fallbackFor"):
                extras += f", fallbackFor: {swift_string(shortcut['fallbackFor'])}"
            chords = ", ".join(swift_string(c) for c in shortcut["mac"])
            name = shortcut.get("name", shortcut["action"])
            rows.append(f"        Entry(name: {swift_string(name)}, mac: [{chords}], contexts: [{contexts}]{extras}),")
    languages = []
    for language in data["languages"]:
        overrides = []
        for override in language["overrides"]:
            entry = index.get((override["section"], override["action"]))
            if entry is None:
                continue  # A Windows-only entry: nothing to change on the Mac.
            chords = ", ".join(swift_string(c) for c in override["mac"])
            overrides.append(f"Override(entry: {entry}, mac: [{chords}], press: {'true' if override['press'] else 'false'})")
        codes = ", ".join(swift_string(c) for c in language["codes"])
        listed = ("[\n" + "".join(f"                     {o},\n" for o in overrides) + "                 ]") if overrides else "[]"
        languages.append(f"        Language(id: {swift_string(language['id'])}, name: {swift_string(language['name'])}, "
                         f"codes: [{codes}], importToggle: {swift_string(language['importToggle'])},\n"
                         f"                 overrides: {listed}),")
    return (
        "// Generated by Scripts/generate-lightroom-shortcuts.py from Shared/lightroom-shortcuts.json.\n"
        "// Do not edit: change the JSON and run the script again.\n\n"
        "extension LightroomShortcuts {\n"
        "    /// Lightroom Classic's default keys on the Mac, in the order of Adobe's help.\n"
        "    static let table: [Entry] = [\n" + "\n".join(rows) + "\n    ]\n\n"
        "    /// The languages Lightroom Classic ships, English first, with the keys\n"
        "    /// their help lists differently (overrides name an entry of `table`).\n"
        "    static let languages: [Language] = [\n" + "\n".join(languages) + "\n    ]\n}\n"
    )


found = problems()
if found:
    print("Invalid Shared/lightroom-shortcuts.json:\n  " + "\n  ".join(found))
    sys.exit(1)
text = generate()
if "--check" in sys.argv:
    if not target.exists() or target.read_text(encoding="utf-8") != text:
        print(f"{target.relative_to(root)} is out of date: run python3 Scripts/generate-lightroom-shortcuts.py")
        sys.exit(1)
    print("Lightroom shortcuts: the Swift table is up to date")
else:
    target.write_text(text, encoding="utf-8")
    print(f"Wrote {target.relative_to(root)}")
