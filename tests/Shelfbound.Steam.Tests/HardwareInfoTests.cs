using Shelfbound.Steam.Steam;
using Shouldly;

namespace Shelfbound.Steam.Tests;

public class HardwareInfoTests
{
    [Fact]
    public void Collect_always_returns_the_safe_cross_platform_fields()
    {
        var specs = HardwareInfo.Collect();

        // Cores, OS, and architecture are available on every platform; the rest is best-effort.
        specs.LogicalCores.ShouldNotBeNull();
        specs.LogicalCores!.Value.ShouldBeGreaterThan(0);
        specs.OsDescription.ShouldNotBeNullOrWhiteSpace();
        specs.Architecture.ShouldNotBeNullOrWhiteSpace();
    }
}
