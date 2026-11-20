namespace Lumerist.Core;

/// <summary>
/// Where a parameter is adjusted: the Develop module (LrDevelopController,
/// active photo) or Library Quick Develop (relative, every selected photo).
/// </summary>
public enum ParameterScope
{
    Develop,
    Library,
}

public static class ParameterScopes
{
    public static IReadOnlyList<ParameterScope> All { get; } = [ParameterScope.Develop, ParameterScope.Library];

    /// <summary>"develop" / "library", as in the protocol and the fixtures.</summary>
    public static string WireName(this ParameterScope scope) =>
        scope == ParameterScope.Develop ? "develop" : "library";

    public static ParameterScope? FromWireName(string? name) => name switch
    {
        "develop" => ParameterScope.Develop,
        "library" => ParameterScope.Library,
        _ => null,
    };
}

/// <summary>One adjustable Lightroom parameter, generated from Shared/parameters.json.</summary>
/// <param name="Id">The LrDevelopController parameter name ("lumerist." ids are plug-in specials).</param>
/// <param name="Unit">"EV", "temp" (points; 50 K each on a Kelvin scale), "deg", "px", "%", "choice" or "pt".</param>
/// <param name="MaximumDelta">Largest change one request may carry.</param>
/// <param name="StepMinimum">Lower end of the range offered for user-defined steps.</param>
/// <param name="StepMaximum">Upper end of that range.</param>
/// <param name="Wraps">Hue-like parameters wrap around instead of stopping at the end.</param>
/// <param name="IsEnumerated">Choice parameters (e.g. vignette style) step through options.</param>
/// <param name="NeedsVerification">Name still to be confirmed in Lightroom.</param>
public sealed record ParameterDescriptor(
    string Id,
    string Label,
    string Section,
    ParameterScope Scope,
    string Unit,
    double NormalStep,
    double CoarseStep,
    double MaximumDelta,
    double StepMinimum,
    double StepMaximum,
    bool Wraps,
    bool IsEnumerated,
    bool NeedsVerification)
{
    /// <summary>Unique across scopes ("develop:Exposure", "library:Exposure").</summary>
    public ParameterKey Key => new(Scope, Id);

    public bool StepRangeContains(double step) => step >= StepMinimum && step <= StepMaximum;
}

/// <summary>Identifies a parameter in one scope.</summary>
public readonly record struct ParameterKey(ParameterScope Scope, string Id)
{
    public override string ToString() => $"{Scope.WireName()}:{Id}";
}

public static partial class ParameterCatalog
{
    // GeneratedEntries lives in ParameterCatalogData.g.cs; the index is built
    // on first use (once, thread-safe) so the order of static initializers
    // across files never matters.
    private static readonly Lazy<Dictionary<ParameterKey, ParameterDescriptor>> Index = new(() =>
        GeneratedEntries.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.First()));

    private static Dictionary<ParameterKey, ParameterDescriptor> ByKey => Index.Value;

    public static IReadOnlyList<ParameterDescriptor> All => GeneratedEntries;

    public static ParameterDescriptor? Descriptor(ParameterKey key) => ByKey.GetValueOrDefault(key);

    public static ParameterDescriptor? Descriptor(ParameterScope scope, string id) => Descriptor(new ParameterKey(scope, id));

    /// <summary>Parameters of one scope grouped by section, in catalog order.</summary>
    public static IReadOnlyList<(string Section, IReadOnlyList<ParameterDescriptor> Parameters)> Sections(ParameterScope scope) =>
        GeneratedEntries
            .Where(entry => entry.Scope == scope)
            .GroupBy(entry => entry.Section)
            .Select(group => (group.Key, (IReadOnlyList<ParameterDescriptor>)group.ToList()))
            .ToList();
}
