#!/usr/bin/env python3
"""Static instances of the bundled variable fonts, for the Windows app.

WPF draws only the default instance of a variable font (and fakes bold), so
Windows gets one file per weight. The fonts are SIL OFL without reserved
names, so the instances keep their family names.

Usage: python3 Scripts/make-windows-fonts.py   (needs fontTools)
"""
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "Resources" / "Fonts"
TARGET = ROOT / "Windows" / "Lumerist.App" / "Assets" / "Fonts"
FAMILIES = {"Inter": "Inter-Variable.ttf", "Outfit": "Outfit-Variable.ttf",
            "JetBrains Mono": "JetBrainsMono-Variable.ttf"}
WEIGHTS = {400: "Regular", 500: "Medium", 600: "SemiBold", 700: "Bold"}


def set_names(font: TTFont, family: str, style: str, weight: int) -> None:
    """Family + weight as Windows groups them: legacy names for R/B, typographic for the rest."""
    full = f"{family} {style}"
    legacy_family = family if style in ("Regular", "Bold") else full
    legacy_style = style if style in ("Regular", "Bold") else "Regular"
    postscript = f"{family.replace(' ', '')}-{style}"
    table = font["name"]
    for record in list(table.names):
        if record.nameID in (1, 2, 3, 4, 6, 16, 17, 21, 22, 25):
            table.removeNames(nameID=record.nameID)
    for name_id, value in ((1, legacy_family), (2, legacy_style), (3, f"Lumerist;{postscript}"),
                           (4, full), (6, postscript), (16, family), (17, style)):
        table.setName(value, name_id, 3, 1, 0x409)
        table.setName(value, name_id, 1, 0, 0)
    os2 = font["OS/2"]
    os2.usWeightClass = weight
    # fsSelection: bit 0 italic, 5 bold, 6 regular.
    os2.fsSelection = (os2.fsSelection & ~0b1100001) | (1 << 5 if style == "Bold" else 0) | (1 << 6 if style == "Regular" else 0)
    font["head"].macStyle = 1 if style == "Bold" else 0


def main() -> None:
    TARGET.mkdir(parents=True, exist_ok=True)
    for family, file in FAMILIES.items():
        for weight, style in WEIGHTS.items():
            font = TTFont(SOURCE / file)
            instance = instantiateVariableFont(font, {"wght": weight})
            if "STAT" in instance:
                del instance["STAT"]
            set_names(instance, family, style, weight)
            out = TARGET / f"{family.replace(' ', '')}-{style}.ttf"
            instance.save(out)
            print(f"{out.relative_to(ROOT)}  {out.stat().st_size // 1024} KB")


if __name__ == "__main__":
    main()
