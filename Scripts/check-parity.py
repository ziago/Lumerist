#!/usr/bin/env python3
"""Checks that the Mac app, the Windows app and the plug-in still agree.

  python3 Scripts/check-parity.py                 # both apps (needs both test runs)
  python3 Scripts/check-parity.py --side swift    # after `swift test` (macOS CI)
  python3 Scripts/check-parity.py --side csharp   # after `dotnet test Windows` (Windows CI)

It fails when
- a case in Shared/fixtures was not run by a side's tests (each test suite
  writes the ids it ran to build/parity/<side>/<group>.txt), or the list is
  older than the cases;
- a value of Shared/contract.json (ports, protocol version, plug-in id,
  consent version and hash) is missing from a side or from the plug-in;
- a row of docs/feature-spec.md has an empty macOS or Windows column.
- a theme colour of the Windows app differs from the Mac's.
"""
import argparse, json, pathlib, re, sys

root = pathlib.Path(__file__).resolve().parent.parent
fixtures = root / "Shared/fixtures"
contract = json.loads((root / "Shared/contract.json").read_text())
problems: list[str] = []
passes: list[str] = []


def ok(message): passes.append(message)
def bad(message): problems.append(message)


# Shared cases ------------------------------------------------------------------

def case_ids(group: str) -> tuple[set[str], float]:
    """Case ids of a fixture group and the newest file's modification time."""
    if group == "standard-layout":
        path = fixtures / "standard-layout.json"
        return {"standard-layout"}, path.stat().st_mtime
    files = sorted((fixtures / group).glob("*.json"))
    ids: set[str] = set()
    for path in files:
        for case in json.loads(path.read_text())["cases"]:
            if case["id"] in ids:
                bad(f"{path.relative_to(root)}: duplicate case id {case['id']}")
            ids.add(case["id"])
    return ids, max(path.stat().st_mtime for path in files)


GROUPS = ["gestures", "keymap-rules", "protocol", "standard-layout"]
RUNNERS = {
    "swift": "swift test",
    "csharp": "dotnet test Windows",
}


def check_runs(side: str):
    for group in GROUPS:
        expected, newest = case_ids(group)
        listing = root / "build/parity" / side / f"{group}.txt"
        if not listing.exists():
            bad(f"{side}: no run of the {group} cases (run `{RUNNERS[side]}` first)")
            continue
        ran = set(listing.read_text().split())
        missing, unknown = sorted(expected - ran), sorted(ran - expected)
        if listing.stat().st_mtime < newest:
            bad(f"{side}: the {group} run is older than the cases (run `{RUNNERS[side]}` again)")
        elif missing:
            bad(f"{side}: {len(missing)} {group} case(s) did not run: {', '.join(missing[:5])}"
                + (" …" if len(missing) > 5 else ""))
        elif unknown:
            bad(f"{side}: ran {group} case(s) that no longer exist: {', '.join(unknown[:5])}")
        else:
            ok(f"{side}: all {len(expected)} {group} case(s) ran")


# Shared values -----------------------------------------------------------------

def sources(patterns: list[str]) -> str:
    text = []
    for pattern in patterns:
        for path in sorted(root.glob(pattern)):
            if not any(part in ("bin", "obj") for part in path.parts):
                text.append(path.read_text(errors="replace"))
    return "\n".join(text)


def port(value: int) -> str:
    """50550 also matches 50_550 (Swift and C# digit separators)."""
    digits = str(value)
    return digits[:2] + "_?" + digits[2:]


def expect(side: str, text: str, what: str, pattern: str):
    if re.search(pattern, text):
        ok(f"{side}: {what}")
    else:
        bad(f"{side}: {what} not found (expected /{pattern}/)")


def check_values(side: str):
    consent = contract["consent"]
    version, digest = re.escape(consent["version"]), consent["sha256"]
    plugin_id = re.escape(contract["pluginId"])
    if side == "plug-in":
        lua = sources(["Lightroom/Lumerist.lrplugin/*.lua"])
        expect(side, lua, "request port", rf"REQUEST_PORT\s*=\s*{contract['requestPort']}\b")
        expect(side, lua, "response port", rf"RESPONSE_PORT\s*=\s*{contract['responsePort']}\b")
        expect(side, lua, "protocol version", rf"protocolVersion\s*=\s*{contract['protocolVersion']}\b")
        expect(side, lua, "plug-in id", rf'LrToolkitIdentifier\s*=\s*"{plugin_id}"')
        expect(side, lua, "Windows link file", r'"plugin-link\.json"')
    elif side == "swift":
        code = sources(["Sources/**/*.swift"])
        tests = sources(["Tests/LumeristCoreTests/*.swift"])
        expect(side, code, "request port", rf"port:\s*{port(contract['requestPort'])}\b")
        expect(side, code, "response port", rf"responsePort:\s*UInt16\s*=\s*{port(contract['responsePort'])}\b")
        expect(side, code, "protocol version", rf"protocolVersion\s*=\s*{contract['protocolVersion']}\b")
        expect(side, code, "consent version", rf'consentVersion\s*=\s*"{version}"')
        expect(side, tests, "consent hash test", digest)
    else:
        code = sources(["Windows/Lumerist.Core/**/*.cs", "Windows/Lumerist.App/**/*.cs"])
        tests = sources(["Windows/Lumerist.Core.Tests/**/*.cs"])
        if not code.strip():
            bad("csharp: no C# sources in Windows/Lumerist.Core yet")
            return
        expect(side, code, "request port", rf"RequestPort\s*=\s*{port(contract['requestPort'])}\b")
        expect(side, code, "response port", rf"ResponsePort\s*=\s*{port(contract['responsePort'])}\b")
        expect(side, code, "protocol version", rf"ProtocolVersion\s*=\s*{contract['protocolVersion']}\b")
        expect(side, code, "consent version", rf'ConsentVersion\s*=\s*"{version}"')
        expect(side, code, "plug-in id", rf'"{plugin_id}"')
        expect(side, code, "Windows link file", r'"plugin-link\.json"')
        expect(side, tests, "consent hash test", digest)


# Feature list ------------------------------------------------------------------

def check_feature_spec():
    path = root / "docs/feature-spec.md"
    lines = path.read_text().splitlines()
    tables = rows = 0
    header = None
    for number, line in enumerate(lines, 1):
        if not line.startswith("|"):
            header = None
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if header is None:
            header = cells
            tables += 1
            if "macOS" not in header or "Windows" not in header:
                bad(f"docs/feature-spec.md:{number}: table without macOS and Windows columns")
                header = []
            continue
        if not header or set(line) <= set("|-: "):
            continue
        rows += 1
        for column in ("macOS", "Windows"):
            index = header.index(column)
            if index >= len(cells) or not cells[index]:
                bad(f"docs/feature-spec.md:{number}: empty {column} column")
    ok(f"feature spec: {rows} rows in {tables} tables have macOS and Windows status")


# Main --------------------------------------------------------------------------

def palettes(text: str, start: str) -> dict[str, dict[str, int]]:
    """Every `name = …(token: 0xRRGGBB, …)` palette in a source file, by name."""
    found = {}
    for match in re.finditer(start, text):
        name = match.group(1)
        body = text[match.end():text.index(")", match.end())]
        tokens = {key.lower(): int(value, 16) for key, value in re.findall(r"(\w+):\s*0x([0-9A-Fa-f]{6})", body)}
        found[name.lower()] = tokens
    return found


def check_theme_palettes():
    """The Windows themes use exactly the Mac's colours."""
    swift = (root / "Sources/LumeristApp/Design/Theme.swift").read_text()
    csharp = (root / "Windows/Lumerist.App/Design/ThemePalette.cs").read_text()
    mac = palettes(swift, r"static let (\w+) = SynthPalette\(")
    windows = palettes(csharp, r"public static readonly ThemePalette (\w+) = new\(")
    if not mac:
        bad("themes: no palette found in Theme.swift")
        return
    for name, tokens in mac.items():
        other = windows.get(name)
        if other is None:
            bad(f"themes: {name} is missing on Windows")
        elif other != tokens:
            diff = sorted(key for key in tokens.keys() | other.keys() if tokens.get(key) != other.get(key))
            bad(f"themes: {name} differs in {', '.join(diff)}")
        else:
            ok(f"themes: {name} has the same {len(tokens)} colours on Mac and Windows")
    for name in windows.keys() - mac.keys():
        bad(f"themes: {name} exists only on Windows")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--side", choices=["swift", "csharp", "both"], default="both")
    side = parser.parse_args().side
    sides = ["swift", "csharp"] if side == "both" else [side]

    check_values("plug-in")
    for name in sides:
        check_values(name)
        check_runs(name)
    check_feature_spec()
    check_theme_palettes()

    for message in passes:
        print("✔ " + message)
    for message in problems:
        print("✘ " + message)
    print(f"\n{len(passes)} checks passed, {len(problems)} failed")
    sys.exit(1 if problems else 0)


main()
