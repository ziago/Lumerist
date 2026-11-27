namespace Lumerist.Core;

/// <summary>Where a Command Key works.</summary>
public enum CommandScope
{
    Develop,
    Library,
    Any,
}

public static class CommandScopes
{
    public static bool Includes(this CommandScope scope, ParameterScope parameterScope) => scope switch
    {
        CommandScope.Any => true,
        CommandScope.Develop => parameterScope == ParameterScope.Develop,
        _ => parameterScope == ParameterScope.Library,
    };

    public static string WireName(this CommandScope scope) => scope switch
    {
        CommandScope.Develop => "develop",
        CommandScope.Library => "library",
        _ => "any",
    };

    public static CommandScope? FromWireName(string? name) => name switch
    {
        "develop" => CommandScope.Develop,
        "library" => CommandScope.Library,
        "any" => CommandScope.Any,
        _ => null,
    };
}

/// <summary>What a Command Key does.</summary>
public abstract record CommandAction
{
    private CommandAction() { }

    /// <summary>A protocol command name (docs/protocol-v4.md) with an optional argument.</summary>
    public sealed record Command(string Name, string? Argument = null) : CommandAction;

    /// <summary>A Develop preset, by UUID; the name is for display.</summary>
    public sealed record Preset(string Uuid, string Name) : CommandAction;

    /// <summary>Lightroom's own keyboard shortcut, sent by the app.</summary>
    public sealed record Shortcut(KeyChord Chord) : CommandAction;

    public sealed record Macro(IReadOnlyList<MacroStep> Steps) : CommandAction
    {
        public bool Equals(Macro? other) => other is not null && Steps.SequenceEqual(other.Steps);

        public override int GetHashCode() => Steps.Aggregate(17, HashCode.Combine);
    }
}

/// <param name="Delay">Pause after this step, in seconds (0–10).</param>
public sealed record MacroStep(CommandAction Action, double Delay = 0)
{
    public const double MaximumDelay = 10;
}

/// <summary>A key that runs a Lightroom command, a preset, a shortcut or a macro.</summary>
/// <param name="Chord">May be a combo: then it runs only while the Combo Key is held.</param>
public sealed record CommandKey(Guid Id, CommandScope Scope, KeyChord Chord, string Group, string Title, CommandAction Action)
{
    public const int MaximumMacroSteps = 50;
    public const int MaximumTitleLength = 80;

    public static CommandKey Create(CommandScope scope, KeyChord chord, string title, CommandAction action,
        string group = "Lightroom") => new(Guid.NewGuid(), scope, chord, group, title, action);
}
