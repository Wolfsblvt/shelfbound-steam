namespace Shelfbound.Core.Model;

/// <summary>
/// Explicit coverage and completeness semantics for <see cref="LibraryScope"/>. The enum's published
/// numeric values cannot express the logical ordering because <see cref="LibraryScope.FullLibrary"/>
/// must retain its historical ordinal.
/// </summary>
public static class LibraryScopeSemantics
{
    /// <summary>Returns the logical coverage rank: installed-only, observed subset, then complete.</summary>
    public static int GetCoverageRank(LibraryScope scope) => scope switch
    {
        LibraryScope.InstalledOnly => 0,
        LibraryScope.ObservedSubset => 1,
        LibraryScope.FullLibrary => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown library scope."),
    };

    /// <summary>Whether <paramref name="candidate"/> covers more than <paramref name="baseline"/>.</summary>
    public static bool IsBroaderThan(LibraryScope candidate, LibraryScope baseline) =>
        GetCoverageRank(candidate) > GetCoverageRank(baseline);

    /// <summary>Returns the broader of two scopes using the explicit coverage ordering.</summary>
    public static LibraryScope BroaderOf(LibraryScope left, LibraryScope right) =>
        IsBroaderThan(left, right) ? left : right;

    /// <summary>Whether absence from the scope may be interpreted using a completeness contract.</summary>
    public static bool IsComplete(LibraryScope scope) => scope == LibraryScope.FullLibrary;

    /// <summary>
    /// Returns the scope that consumers may use operationally. Published schema 0.4.x and 0.5.x
    /// producers labeled the visibility-gated Steam Web API result <see cref="LibraryScope.FullLibrary"/>,
    /// although that source had no completeness contract. Preserve the reported value in raw documents,
    /// but treat it no stronger than <see cref="LibraryScope.ObservedSubset"/> for behavior.
    /// </summary>
    public static LibraryScope GetOperationalScope(string schemaVersion, LibraryScope reportedScope)
    {
        if (reportedScope != LibraryScope.FullLibrary)
            return reportedScope;

        return IsLegacyVisibilityGatedSchema(schemaVersion)
            ? LibraryScope.ObservedSubset
            : LibraryScope.FullLibrary;
    }

    private static bool IsLegacyVisibilityGatedSchema(string schemaVersion)
    {
        string versionCore = schemaVersion.Split(['-', '+'], 2)[0];
        string[] components = versionCore.Split('.');
        return components.Length >= 2 &&
            int.TryParse(components[0], out int major) &&
            int.TryParse(components[1], out int minor) &&
            major == 0 && minor is 4 or 5;
    }
}
