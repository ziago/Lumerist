namespace Lumerist.Core;

// The Keyboard tab without the drawing (Views/KeyboardTab.swift): what each
// key does in a module, the short names on the key faces, and where every
// key sits on a drawn ANSI or ISO keyboard (by scan code).

/// <summary>Where a click on the drawn keyboard leads.</summary>
public abstract record KeyboardReveal
{
    private KeyboardReveal() { }

    /// <summary>The Sliders (Quick Develop) tab, searching for this slider.</summary>
    public sealed record Slider(string Label) : KeyboardReveal;

    /// <summary>The Commands tab, searching for this Command Key.</summary>
    public sealed record Command(string Title) : KeyboardReveal;

    /// <summary>
    /// Where a System Key is changed: Floating panels for the Palette and Key
    /// Reference keys, General for the others.
    /// </summary>
    public sealed record SystemKey(SystemKeyRole Role) : KeyboardReveal;

    public bool OpensFloatingPanels => this is SystemKey { Role: SystemKeyRole.PaletteKey or SystemKeyRole.KeyReferenceToggle };

    /// <summary>What a click does, for screen readers.</summary>
    public string Hint => this switch
    {
        Slider => "Shows this slider in the Sliders tab",
        Command => "Shows this Command Key in the Commands tab",
        _ => OpensFloatingPanels
            ? "Opens Floating panels, where you can change this key"
            : "Opens General, where you can change this key",
    };
}

/// <summary>One key binding of a module, ready to draw.</summary>
/// <param name="Name">Full name: the slider, the Command Key's title or the System Key.</param>
/// <param name="ShortName">A few characters for the key face.</param>
/// <param name="Detail">What the key does, in a few words.</param>
public sealed record KeyAssignment(
    string Id,
    KeyChord Chord,
    KeyAssignment.Kind Type,
    KeyUsage Usage,
    string Name,
    string ShortName,
    string Detail,
    KeyboardReveal? Reveal)
{
    /// <summary>In drawing priority order.</summary>
    public enum Kind
    {
        Dial,
        NudgeDown,
        NudgeUp,
        Command,
        System,
    }

    public static string Title(Kind kind) => kind switch
    {
        Kind.Dial => "Dial Key",
        Kind.NudgeDown => "Nudge −",
        Kind.NudgeUp => "Nudge +",
        Kind.Command => "Command Key",
        _ => "System key",
    };

    /// <summary>Every binding of a module: System Keys, slider keys and Command Keys.</summary>
    public static IReadOnlyList<KeyAssignment> All(Keymap keymap, ParameterScope scope)
    {
        var result = new List<KeyAssignment>();
        foreach (var role in SystemKeyRoles.All)
        {
            if (keymap.SystemKeys.ChordFor(role) is not { } chord) continue;
            if (role == SystemKeyRole.PaletteKey && scope != ParameterScope.Develop) continue; // the Palette is a Develop tool
            var (shortName, detail) = role switch
            {
                SystemKeyRole.ComboKey => ("Combo", "hold it, then press a key for a combo"),
                SystemKeyRole.PaletteKey => ("Palette", "hold it for the Palette: new masks and presets"),
                SystemKeyRole.KeyReferenceToggle => ("Key list", "shows or hides the Key Reference"),
                _ => ("Pause", "switches Lumerist off and back on"),
            };
            result.Add(new KeyAssignment($"system.{role.WireName()}", chord, Kind.System, new KeyUsage.SystemKey(role),
                role.Title(), shortName, detail, new KeyboardReveal.SystemKey(role)));
        }
        foreach (var (_, parameters) in ParameterCatalog.Sections(scope))
        {
            foreach (var descriptor in parameters)
            {
                if (keymap.Slot(descriptor.Key) is not { } slot) continue;
                var key = descriptor.Key;
                var label = DisplayName(descriptor.Label);
                var shortName = Abbreviated(label);
                var entries = new (KeyChord? Chord, Kind Kind, KeyUsage Usage, string Short, string Detail)[]
                {
                    (slot.Dial, Kind.Dial, new KeyUsage.Dial(key), shortName, "hold it, then scroll, drag or press ← →"),
                    (slot.NudgeDown, Kind.NudgeDown, new KeyUsage.NudgeDown(key), "− " + shortName, "one step down per press"),
                    (slot.NudgeUp, Kind.NudgeUp, new KeyUsage.NudgeUp(key), "+ " + shortName, "one step up per press"),
                };
                foreach (var entry in entries)
                {
                    if (entry.Chord is not { } chord) continue;
                    result.Add(new KeyAssignment($"{(int)entry.Kind}.{key}", chord, entry.Kind, entry.Usage, label, entry.Short,
                        entry.Detail, new KeyboardReveal.Slider(descriptor.Label)));
                }
            }
        }
        foreach (var commandKey in keymap.CommandKeysIn(scope))
        {
            result.Add(new KeyAssignment($"command.{commandKey.Id}", commandKey.Chord, Kind.Command,
                new KeyUsage.Action(commandKey.Id, commandKey.Title), commandKey.Title, Abbreviated(commandKey.Title),
                commandKey.Action.Summary(), new KeyboardReveal.Command(commandKey.Title)));
        }
        return result;
    }

    /// <summary>"Q · Exposure, Dial Key: hold it, then scroll, drag or press ← →".</summary>
    public string Description(Keymap keymap) => $"{keymap.DisplayText(Chord)} · {Name}, {Title(Type)}: {Detail}";

    /// <summary>A tooltip line with the Combo Key named: "Tab + A · Auto Tone, Command Key: Auto Tone".</summary>
    public string Line(KeyChord? comboKey) => $"{Chord.DisplayTextWith(comboKey)} · {Name}, {Title(Type)}: {Detail}";

    /// <summary>The key face: modifiers held with the key come first ("Alt Auto Tone", "Ctrl+Shift − Exposure").</summary>
    public string FaceText
    {
        get
        {
            var prefix = KeyNames.ModifierPrefix(Chord.WithoutCombo.Modifiers).TrimEnd('+');
            return prefix.Length == 0 ? ShortName : $"{prefix} {ShortName}";
        }
    }

    /// <summary>Library labels drop their "Quick" prefix on the key face.</summary>
    public static string DisplayName(string label)
    {
        if (!label.StartsWith("Quick ", StringComparison.Ordinal) || label.Length <= 6) return label;
        var rest = label[6..];
        return char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    /// <summary>Shortens long words so a name fits on two lines of a key.</summary>
    public static string Abbreviated(string name)
    {
        if (name.Length <= 12) return name;
        var text = name.Replace("White Balance", "WB", StringComparison.Ordinal);
        // In order: longer words before the words they contain.
        (string Long, string Short)[] words =
        [
            ("saturation", "sat."), ("luminance", "lum."), ("highlights", "highl."), ("highlight", "highl."),
            ("temperature", "temp."), ("sharpening", "sharp."), ("amount", "amt."), ("settings", "sett."),
            ("previous", "prev."), ("distortion", "dist."), ("vignetting", "vign."), ("vignette", "vign."),
            ("roundness", "round."), ("smoothness", "smooth."), ("noise reduction", "NR"),
        ];
        foreach (var (longWord, shortWord) in words)
            text = text.Replace(longWord, shortWord, StringComparison.OrdinalIgnoreCase);
        // Keep the first letter's case ("Sat." at the start, "sat." after).
        if (text.Length == 0 || char.IsLower(text[0]) == char.IsLower(name[0])) return text;
        return name[0] + text[1..];
    }

    /// <summary>The key's own binding first, then those with modifiers; then by kind.</summary>
    public static IReadOnlyList<KeyAssignment> Sorted(IEnumerable<KeyAssignment> list) =>
        list.OrderBy(assignment => assignment.Chord.WithoutCombo.Modifiers == KeyModifiers.None ? 0 : 1)
            .ThenBy(assignment => (int)assignment.Type)
            .ToList();
}

/// <summary>The physical keyboard drawn: ANSI (one-row Enter) or ISO (tall Enter, extra key left of Z).</summary>
public enum KeyboardShape
{
    Ansi,
    Iso,
}

/// <summary>One key position on the drawing, in key units (a letter key is 1 × 1).</summary>
/// <param name="ScanCode">Null: drawn for orientation only (modifiers, Caps Lock).</param>
/// <param name="Caption">Printed name for keys without a character ("Tab", "Enter"…).</param>
/// <param name="IsIsoEnter">The ISO Enter key, two rows tall and narrower at the bottom.</param>
public sealed record KeySpot(string Id, int? ScanCode, string? Caption, double X, double Y, double Width, double Height,
    bool IsIsoEnter = false)
{
    /// <summary>The drawing is 15 × 5 key units.</summary>
    public const double TotalWidth = 15, TotalHeight = 5;

    /// <summary>Layouts whose keyboards are usually ANSI; every other layout is drawn ISO.</summary>
    private static readonly HashSet<int> AnsiLanguages =
    [
        0x0409, // English (United States)
        0x1009, // English (Canada)
        0x0C09, // English (Australia)
        0x1409, // English (New Zealand)
        0x4009, // English (India)
        0x0411, // Japanese
        0x0412, // Korean
        0x0404, // Chinese (Taiwan)
        0x0804, // Chinese (PRC)
        0x0C04, // Chinese (Hong Kong)
    ];

    /// <summary>
    /// Windows cannot tell the physical keyboard, so the shape follows the
    /// layout's language (the low word of an HKL): US-style layouts are drawn
    /// ANSI, European ones ISO.
    /// </summary>
    public static KeyboardShape ShapeFor(int languageId) => AnsiLanguages.Contains(languageId & 0xFFFF) ? KeyboardShape.Ansi : KeyboardShape.Iso;

    public static IReadOnlyList<KeySpot> Spots(KeyboardShape shape)
    {
        var spots = new List<KeySpot>();
        void Row(double y, IEnumerable<(int? Code, string? Caption, double Width)> keys)
        {
            double x = 0;
            foreach (var (code, caption, width) in keys)
            {
                spots.Add(new KeySpot($"{x}-{y}", code, caption, x, y, width, 1));
                x += width;
            }
        }
        static IEnumerable<(int?, string?, double)> Keys(int first, int last) =>
            Enumerable.Range(first, last - first + 1).Select(code => ((int?)code, (string?)null, 1.0));

        // 1 … =, Q … ], A … ', Z … / are consecutive scan codes on every layout.
        switch (shape)
        {
            case KeyboardShape.Ansi:
                Row(0, [(ScanCodes.LeftOf1, null, 1), .. Keys(0x02, 0x0D), (ScanCodes.Backspace, "Backspace", 2)]);
                Row(1, [(ScanCodes.Tab, "Tab", 1.5), .. Keys(0x10, 0x1B), (0x2B, null, 1.5)]);
                Row(2, [(null, "Caps Lock", 1.75), .. Keys(0x1E, 0x28), (ScanCodes.Enter, "Enter", 2.25)]);
                Row(3, [(null, "Shift", 2.25), .. Keys(0x2C, 0x35), (null, "Shift", 2.75)]);
                break;
            default:
                Row(0, [(ScanCodes.LeftOf1, null, 1), .. Keys(0x02, 0x0D), (ScanCodes.Backspace, "Backspace", 2)]);
                Row(1, [(ScanCodes.Tab, "Tab", 1.5), .. Keys(0x10, 0x1B)]);
                spots.Add(new KeySpot("iso-enter", ScanCodes.Enter, "Enter", 13.5, 1, 1.5, 2, IsIsoEnter: true));
                Row(2, [(null, "Caps Lock", 1.75), .. Keys(0x1E, 0x28), (0x2B, null, 1)]);
                Row(3, [(null, "Shift", 1.25), (0x56, null, 1), .. Keys(0x2C, 0x35), (null, "Shift", 2.75)]);
                break;
        }
        Row(4, [(null, "Ctrl", 1.5), (null, "Win", 1.25), (null, "Alt", 1.25), (ScanCodes.Space, "Space", 5.5),
            (null, "Alt", 1.25), (null, "Ctrl", 2)]);
        // Arrow keys: ← and → half height, ↑ over ↓.
        spots.Add(new KeySpot("left", ScanCodes.LeftArrow, "←", 12.75, 4.5, 0.75, 0.5));
        spots.Add(new KeySpot("up", ScanCodes.UpArrow, "↑", 13.5, 4, 0.75, 0.5));
        spots.Add(new KeySpot("down", ScanCodes.DownArrow, "↓", 13.5, 4.5, 0.75, 0.5));
        spots.Add(new KeySpot("right", ScanCodes.RightArrow, "→", 14.25, 4.5, 0.75, 0.5));
        return spots;
    }

    /// <summary>Half-height keys (the arrows) show only their glyph.</summary>
    public bool IsCompact => Height < 1;
}

/// <summary>The Keyboard tab's Clear… choices.</summary>
public enum ClearTarget
{
    Dial,
    Nudge,
}

public static class ClearTargets
{
    /// <summary>The choices worth offering: only kinds of key the module has.</summary>
    public static IReadOnlyList<ClearTarget> Available(Keymap keymap, ParameterScope scope)
    {
        var slots = keymap.Slots(scope).Values;
        var targets = new List<ClearTarget>();
        if (slots.Any(slot => slot.Dial is not null)) targets.Add(ClearTarget.Dial);
        if (slots.Any(slot => slot.NudgeDown is not null || slot.NudgeUp is not null)) targets.Add(ClearTarget.Nudge);
        return targets;
    }

    public static string Title(this ClearTarget target) => target == ClearTarget.Dial ? "Clear all Dial Keys" : "Clear all Nudge Keys";

    /// <summary>"Clear every Dial Key in Develop?"</summary>
    public static string Question(this ClearTarget target, ParameterScope scope) =>
        $"Clear every {(target == ClearTarget.Nudge ? "Nudge Key" : "Dial Key")} in {(scope == ParameterScope.Develop ? "Develop" : "Library")}?";
}
