using Shouldly;
using Shelfbound.Steam.Collections;

namespace Shelfbound.Steam.Tests;

public sealed class ChromiumLevelDbTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "shelfbound-leveldb-" + Guid.NewGuid().ToString("N"));

    public ChromiumLevelDbTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Skips_oversized_cache_file_without_reading_it()
    {
        string path = Path.Combine(_directory, "000001.log");
        using (FileStream stream = File.Create(path))
            stream.SetLength(SteamInputLimits.MaxLevelDbFileBytes + 1L);

        byte[]? value = ChromiumLevelDb.ReadLatestValue(_directory, "key"u8.ToArray());

        value.ShouldBeNull();
    }

    [Fact]
    public void Truncated_declared_wal_lengths_fail_closed()
    {
        string path = Path.Combine(_directory, "000001.log");
        byte[] batch = [
            .. BitConverter.GetBytes(1L),
            .. BitConverter.GetBytes(1),
            1,
            0xff, 0xff, 0xff, 0xff, 0x07,
        ];
        byte[] record = [0, 0, 0, 0, (byte)batch.Length, 0, 1, .. batch];
        File.WriteAllBytes(path, record);

        byte[]? value = ChromiumLevelDb.ReadLatestValue(_directory, "key"u8.ToArray());

        value.ShouldBeNull();
    }
}
