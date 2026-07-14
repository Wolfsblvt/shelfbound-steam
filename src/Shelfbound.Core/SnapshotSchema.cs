namespace Shelfbound.Core;

/// <summary>Constants describing the current snapshot schema/contract.</summary>
public static class SnapshotSchema
{
    /// <summary>
    /// Current snapshot schema version (semver). Bump on contract changes and keep the package-to-schema
    /// mapping in Directory.Build.props synchronized; see docs/project/snapshot-schema.md and
    /// schema/snapshot.v0.schema.json.
    /// </summary>
    public const string Version = "0.6.0";

    /// <summary>Canonical tool name recorded in <c>source.tool</c> for the bundled CLI scanner.</summary>
    public const string CliToolName = "shelfbound-cli";
}
