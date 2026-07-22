namespace Shelfbound.Steam;

/// <summary>Resource ceilings for untrusted local Steam/cache inputs.</summary>
internal static class SteamInputLimits
{
    public const int MaxVdfFileBytes = 16 * 1024 * 1024;
    public const int MaxVdfTextChars = 16 * 1024 * 1024;
    public const int MaxVdfDepth = 64;

    public const int MaxLevelDbFileBytes = 64 * 1024 * 1024;
    public const long MaxLevelDbTotalBytes = 256L * 1024 * 1024;
    public const int MaxLevelDbFiles = 256;
    public const int MaxLevelDbEntriesPerFile = 250_000;
    public const int MaxLevelDbBlockBytes = 16 * 1024 * 1024;
    public const int MaxLevelDbValueBytes = 16 * 1024 * 1024;
    public const int MaxLevelDbKeyBytes = 1024 * 1024;
    public const int MaxNamespaceJsonChars = 16 * 1024 * 1024;
    public const int MaxCollectionEntries = 10_000;
    public const int MaxCollectionMemberships = 250_000;
    public const int MaxCategoryNameChars = 256;
    public const int MaxPrivateAppEntries = 250_000;

    public const int MaxInstallDirectoryNameChars = 255;
}
