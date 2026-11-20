namespace Lumerist.Core;

/// <summary>One row of the Key Reference.</summary>
/// <param name="Id">Stable within a list ("dial.Exposure", "system.comboKey").</param>
/// <param name="Keys">The key caps; combos show only their own key, the group title names the Combo Key.</param>
/// <param name="Kind">"System", "Dial", "Nudge" or "Command".</param>
/// <param name="SearchText">Also matched by the search: a combo's full name ("Tab + K").</param>
/// <param name="IsHeader">The title row of a group rather than a key.</param>
public sealed record KeyReferenceEntry(
    string Id,
    IReadOnlyList<string> Keys,
    string Title,
    string Kind,
    string SearchText = "",
    bool IsHeader = false)
{
    public bool Equals(KeyReferenceEntry? other) =>
        other is not null && Id == other.Id && Keys.SequenceEqual(other.Keys) && Title == other.Title && Kind == other.Kind
        && SearchText == other.SearchText && IsHeader == other.IsHeader;

    public override int GetHashCode() => HashCode.Combine(Id, Title, Kind, IsHeader);
}

/// <summary>One page of the Key Reference.</summary>
/// <param name="Rows">At most <see cref="KeyReferenceList.PageSize"/> rows.</param>
/// <param name="Index">The page shown, 0-based (the asked page, kept inside the list).</param>
public sealed record KeyReferencePage(IReadOnlyList<KeyReferenceEntry> Rows, int Index, int Count);

/// <summary>
/// The Key Reference's list (the Mac's KeyReferenceView): the module's System
/// Keys, Dial Keys, Nudge Keys and Command Keys, then the combos in their own
/// group ("Combos (hold Tab)"). Searchable and paged.
/// </summary>
public static class KeyReferenceList
{
    public const int PageSize = 14;

    public const string Title = "Key Reference";
    public const string HideLabel = "Hide the Key Reference";
    public const string SearchPrompt = "Search";
    public const string NoKeys = "No keys assigned in this module.";
    public const string NothingMatches = "Nothing matches.";

    /// <summary>Library in Lightroom's Library module, Develop everywhere else.</summary>
    public static ParameterScope Scope(LightroomContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Module == "library" ? ParameterScope.Library : ParameterScope.Develop;
    }

    /// <summary>"Develop", "Develop · mask" or "Library".</summary>
    public static string Subtitle(LightroomContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Scope(context) == ParameterScope.Develop ? (context.IsMasking ? "Develop · mask" : "Develop") : "Library";
    }

    /// <summary>A System Key's name, as the Mac's keyboard settings write it.</summary>
    public static string RoleTitle(SystemKeyRole role) => role switch
    {
        SystemKeyRole.ComboKey => "Combo Key",
        SystemKeyRole.PaletteKey => "Palette Key",
        SystemKeyRole.KeyReferenceToggle => "Key Reference toggle",
        _ => "Pause toggle",
    };

    /// <summary>Keys that work on their own, then combos (each Nudge Key on its side).</summary>
    public static (IReadOnlyList<KeyReferenceEntry> Regular, IReadOnlyList<KeyReferenceEntry> Combos) Entries(
        Keymap keymap, ParameterScope scope)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        var regular = new List<KeyReferenceEntry>();
        var combos = new List<KeyReferenceEntry>();
        foreach (var role in SystemKeyRoles.All)
        {
            if (keymap.SystemKeys.ChordFor(role) is not { } chord) continue;
            if (role == SystemKeyRole.PaletteKey && scope != ParameterScope.Develop) continue;
            // A System Key may itself be a combo ("Tab + H"): spelled out in full here.
            regular.Add(new($"system.{role.WireName()}", [keymap.DisplayText(chord)], RoleTitle(role), "System"));
        }
        var summary = keymap.Summary(scope, AssignmentSummary.Sort.Section);
        foreach (var entry in summary.DialKeys)
        {
            if (entry.Slot.Dial is not { } chord) continue;
            regular.Add(new($"dial.{entry.Descriptor.Id}", [chord.DisplayText], entry.Descriptor.Label, "Dial"));
        }
        foreach (var entry in summary.NudgeKeys)
        {
            var nudges = new (string Sign, KeyChord? Chord)[] { ("−", entry.Slot.NudgeDown), ("+", entry.Slot.NudgeUp) };
            foreach (var isCombo in new[] { false, true })
            {
                var chords = nudges
                    .Where(nudge => nudge.Chord is { } chord && chord.IsCombo == isCombo)
                    .Select(nudge => (nudge.Sign, Chord: nudge.Chord!.Value))
                    .ToList();
                if (chords.Count == 0) continue;
                var item = new KeyReferenceEntry(
                    $"nudge.{(isCombo ? "combo." : "")}{entry.Descriptor.Id}",
                    chords.Select(nudge => nudge.Sign + nudge.Chord.WithoutCombo.DisplayText).ToList(),
                    entry.Descriptor.Label,
                    "Nudge",
                    string.Join(' ', chords.Select(nudge => keymap.DisplayText(nudge.Chord))));
                (isCombo ? combos : regular).Add(item);
            }
        }
        foreach (var action in keymap.CommandKeysIn(scope))
        {
            var item = new KeyReferenceEntry($"command.{action.Id}", [action.Chord.WithoutCombo.DisplayText], action.Title,
                "Command", keymap.DisplayText(action.Chord));
            (action.Chord.IsCombo ? combos : regular).Add(item);
        }
        return (regular, combos);
    }

    /// <summary>The title row of the combos: "Combos (hold Tab)".</summary>
    public static KeyReferenceEntry ComboHeader(Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        var title = keymap.SystemKeys.ComboKey is { } combo ? $"Combos (hold {combo.DisplayText})" : "Combos (set a Combo Key)";
        return new KeyReferenceEntry("header.combos", [], title, "", IsHeader: true);
    }

    /// <summary>Rows whose title, keys or full combo name contain the search, ignoring case.</summary>
    public static IReadOnlyList<KeyReferenceEntry> Filtered(IReadOnlyList<KeyReferenceEntry> entries, string search)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var query = (search ?? "").Trim();
        if (query.Length == 0) return entries;
        return entries.Where(entry =>
                entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || entry.Keys.Any(key => key.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                || entry.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>Every row in order: the regular keys, then the combo header and the combos (when any match).</summary>
    public static IReadOnlyList<KeyReferenceEntry> Rows(Keymap keymap, ParameterScope scope, string search)
    {
        var (regular, comboEntries) = Entries(keymap, scope);
        var combos = Filtered(comboEntries, search);
        var rows = new List<KeyReferenceEntry>(Filtered(regular, search));
        if (combos.Count > 0)
        {
            rows.Add(ComboHeader(keymap));
            rows.AddRange(combos);
        }
        return rows;
    }

    /// <summary>The rows of page <paramref name="page"/> (kept inside the list), and how many pages there are.</summary>
    public static KeyReferencePage Page(IReadOnlyList<KeyReferenceEntry> rows, int page)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var count = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)PageSize));
        var current = Math.Max(0, Math.Min(page, count - 1));
        return new KeyReferencePage(rows.Skip(current * PageSize).Take(PageSize).ToList(), current, count);
    }

    /// <summary>"1 / 3".</summary>
    public static string PageText(KeyReferencePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return $"{page.Index + 1} / {page.Count}";
    }
}
