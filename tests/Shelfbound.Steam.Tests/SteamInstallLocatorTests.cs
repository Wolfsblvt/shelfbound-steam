using Shelfbound.Steam.Steam;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public sealed class SteamInstallLocatorTests : IDisposable
{
    private readonly string _root;

    public SteamInstallLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "shelfbound-locator-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Explicit_override_beats_every_later_source()
    {
        string explicitPath = CreateSteamRoot("explicit");
        string environmentPath = CreateSteamRoot("environment");
        string registryPath = CreateSteamRoot("registry");
        string defaultPath = CreateSteamRoot("default");

        Locate(explicitPath, environmentPath, registryPath, [defaultPath]).ShouldBe(explicitPath);
    }

    [Fact]
    public void Invalid_explicit_override_does_not_fall_through()
    {
        string environmentPath = CreateSteamRoot("environment");
        string registryPath = CreateSteamRoot("registry");
        string defaultPath = CreateSteamRoot("default");

        Locate(CreateDirectory("invalid-explicit"), environmentPath, registryPath, [defaultPath]).ShouldBeNull();
    }

    [Fact]
    public void Valid_environment_path_beats_registry()
    {
        string environmentPath = CreateSteamRoot("environment");
        string registryPath = CreateSteamRoot("registry");
        string defaultPath = CreateSteamRoot("default");

        Locate(null, environmentPath, registryPath, [defaultPath]).ShouldBe(environmentPath);
    }

    [Fact]
    public void Invalid_environment_path_falls_through_to_registry()
    {
        string registryPath = CreateSteamRoot("registry");
        string defaultPath = CreateSteamRoot("default");

        Locate(null, CreateDirectory("invalid-environment"), registryPath, [defaultPath]).ShouldBe(registryPath);
    }

    [Fact]
    public void Valid_registry_path_beats_default_candidates()
    {
        string registryPath = CreateSteamRoot("registry");
        string defaultPath = CreateSteamRoot("default");

        Locate(null, null, registryPath, [defaultPath]).ShouldBe(registryPath);
    }

    [Theory]
    [MemberData(nameof(InvalidRegistryValues))]
    public void Invalid_or_unavailable_registry_values_fall_through_to_default_candidates(Func<string, object?> registryValue)
    {
        string defaultPath = CreateSteamRoot("default");
        string invalidPath = CreateDirectory("invalid-registry");

        Locate(null, null, () => registryValue(invalidPath), [defaultPath]).ShouldBe(defaultPath);
    }

    [Fact]
    public void Non_windows_lookup_never_reads_the_registry()
    {
        string defaultPath = CreateSteamRoot("default");
        var registryRead = false;
        var sources = new SteamInstallLocatorSources(
            _ => null,
            () => false,
            () =>
            {
                registryRead = true;
                return CreateSteamRoot("registry");
            },
            () => [defaultPath]);

        SteamInstallLocator.Locate(null, sources).ShouldBe(defaultPath);
        registryRead.ShouldBeFalse();
    }

    [Fact]
    public void Is_steam_root_requires_an_existing_steamapps_directory()
    {
        SteamInstallLocator.IsSteamRoot(" ").ShouldBeFalse();
        SteamInstallLocator.IsSteamRoot(Path.Combine(_root, "missing")).ShouldBeFalse();
        SteamInstallLocator.IsSteamRoot(CreateDirectory("not-steam")).ShouldBeFalse();
        SteamInstallLocator.IsSteamRoot(CreateSteamRoot("steam")).ShouldBeTrue();
    }

    public static IEnumerable<object[]> InvalidRegistryValues()
    {
        yield return [new Func<string, object?>(_ => null)];
        yield return [new Func<string, object?>(_ => 42)];
        yield return [new Func<string, object?>(_ => "  ")];
        yield return [new Func<string, object?>(path => path)];
        yield return [new Func<string, object?>(_ => throw new UnauthorizedAccessException())];
        yield return [new Func<string, object?>(_ => throw new ObjectDisposedException("RegistryKey"))];
    }

    private string? Locate(string? overridePath, string? environmentPath, object? registryValue, IEnumerable<string> candidates)
        => Locate(overridePath, environmentPath, () => registryValue, candidates);

    private static string? Locate(
        string? overridePath,
        string? environmentPath,
        Func<object?> getRegistryValue,
        IEnumerable<string> candidates)
    {
        var sources = new SteamInstallLocatorSources(
            variableName => variableName == "SHELFBOUND_STEAM_PATH" ? environmentPath : null,
            () => true,
            getRegistryValue,
            () => candidates);

        return SteamInstallLocator.Locate(overridePath, sources);
    }

    private string CreateSteamRoot(string name)
    {
        string path = CreateDirectory(name);
        Directory.CreateDirectory(Path.Combine(path, "steamapps"));
        return path;
    }

    private string CreateDirectory(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
