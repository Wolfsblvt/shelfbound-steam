using Shouldly;
using Shelfbound.Storage.Config;

namespace Shelfbound.Steam.Tests;

public class ShelfboundConfigTests
{
    [Fact]
    public void Saves_credentials_with_owner_only_permissions_on_unix()
    {
        if (OperatingSystem.IsWindows())
            return;

        string directory = Path.Combine(Path.GetTempPath(), "shelfbound-config-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "config.json");
        try
        {
            new ShelfboundConfig { SteamApiKey = "synthetic-test-key" }.Save(path);

            File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
