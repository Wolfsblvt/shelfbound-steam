using System.Buffers.Binary;

namespace Shelfbound.Steam.Collections;

/// <summary>
/// A read-only reader for a Chromium "Local Storage" LevelDB directory — enough to fetch the latest
/// value for one key while the owning process (Steam) holds the LevelDB lock. It parses the immutable
/// <c>*.ldb</c> SSTables and the <c>*.log</c> write-ahead log <i>directly</i> (no LevelDB handle, so no
/// lock contention), then resolves a key to its highest-sequence value.
///
/// <para>Deliberately partial: we don't read the MANIFEST or merge levels. Across all on-disk files we
/// keep, per user-key, the entry with the greatest sequence number whose type is a value — skipping
/// deletions. This yields the latest <i>fully-persisted</i> value, which is what we want: while Steam is
/// running it can leave a trailing tombstone whose replacement is still only in the in-memory memtable
/// (see docs/project/steam-collections.md). Best-effort by nature.</para>
/// </summary>
internal static class ChromiumLevelDb
{
    private const ulong TypeMask = 0xff;
    private const byte TypeValue = 1; // 0 = deletion/tombstone

    /// <summary>
    /// Returns the highest-sequence value stored for the exact user-key across every SSTable and the WAL
    /// in <paramref name="leveldbDir"/>, or null if no value entry exists (or only tombstones do).
    /// </summary>
    public static byte[]? ReadLatestValue(string leveldbDir, byte[] userKey)
    {
        byte[]? best = null;
        ulong bestSeq = 0;
        bool found = false;

        foreach (Entry entry in EnumerateEntries(leveldbDir))
        {
            if (entry.Type != TypeValue)
                continue; // ignore tombstones — keep the latest persisted value
            if (!userKey.AsSpan().SequenceEqual(entry.UserKey))
                continue;
            if (!found || entry.Sequence > bestSeq)
            {
                found = true;
                bestSeq = entry.Sequence;
                best = entry.Value;
            }
        }

        return best;
    }

    private readonly record struct Entry(byte[] UserKey, ulong Sequence, byte Type, byte[] Value);

    private static IEnumerable<Entry> EnumerateEntries(string dir)
    {
        // SSTables hold compacted data; the WAL holds the newest writes not yet compacted.
        foreach (string path in Directory.EnumerateFiles(dir, "*.ldb"))
            foreach (Entry e in SafeRead(path, ReadTable))
                yield return e;
        foreach (string path in Directory.EnumerateFiles(dir, "*.log"))
            foreach (Entry e in SafeRead(path, ReadLog))
                yield return e;
    }

    /// <summary>A malformed/locked file must never break the scan; it just yields nothing.</summary>
    private static IEnumerable<Entry> SafeRead(string path, Func<string, IEnumerable<Entry>> reader)
    {
        List<Entry> entries;
        try
        {
            entries = reader(path).ToList();
        }
        catch
        {
            return [];
        }
        return entries;
    }

    // --- SSTable (.ldb) ---------------------------------------------------------------------------

    private const int FooterSize = 48; // two block handles (varint pairs, padded) + 8-byte magic

    private static IEnumerable<Entry> ReadTable(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < FooterSize)
            yield break;

        // Footer: metaindex handle, index handle (each = varint offset + varint size), then the magic.
        int fp = data.Length - FooterSize;
        ReadVarint(data, ref fp); // metaindex offset (unused)
        ReadVarint(data, ref fp); // metaindex size  (unused)
        long indexOffset = ReadVarint(data, ref fp);
        long indexSize = ReadVarint(data, ref fp);

        foreach ((byte[] _, byte[] handle) in ParseBlock(ReadBlock(data, (int)indexOffset, (int)indexSize)))
        {
            int hp = 0;
            long blockOffset = ReadVarint(handle, ref hp);
            long blockSize = ReadVarint(handle, ref hp);

            byte[] block;
            try
            {
                block = ReadBlock(data, (int)blockOffset, (int)blockSize);
            }
            catch
            {
                continue;
            }

            foreach ((byte[] internalKey, byte[] value) in ParseBlock(block))
            {
                if (internalKey.Length < 8)
                    continue;
                // Internal key = user-key + 8-byte trailer ((sequence << 8) | type), little-endian.
                ulong trailer = BinaryPrimitives.ReadUInt64LittleEndian(internalKey.AsSpan(internalKey.Length - 8));
                yield return new Entry(internalKey[..^8], trailer >> 8, (byte)(trailer & TypeMask), value);
            }
        }
    }

    /// <summary>Reads a block by handle, undoing the 1-byte compression-type trailer (snappy or none).</summary>
    private static byte[] ReadBlock(byte[] data, int offset, int size)
    {
        ReadOnlySpan<byte> content = data.AsSpan(offset, size);
        byte compression = data[offset + size]; // trailer: 1 type byte + 4 CRC bytes (CRC unchecked)
        return compression == 1 ? Snappy.Decompress(content) : content.ToArray();
    }

    /// <summary>Parses a LevelDB data/index block: prefix-compressed entries then a restart array.</summary>
    private static IEnumerable<(byte[] Key, byte[] Value)> ParseBlock(byte[] block)
    {
        int numRestarts = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(block.Length - 4));
        int restartArray = block.Length - 4 - numRestarts * 4;

        int pos = 0;
        byte[] lastKey = [];
        while (pos < restartArray)
        {
            int shared = (int)ReadVarint(block, ref pos);
            int nonShared = (int)ReadVarint(block, ref pos);
            int valueLength = (int)ReadVarint(block, ref pos);

            var key = new byte[shared + nonShared];
            Array.Copy(lastKey, 0, key, 0, shared);
            Array.Copy(block, pos, key, shared, nonShared);
            pos += nonShared;

            var value = new byte[valueLength];
            Array.Copy(block, pos, value, 0, valueLength);
            pos += valueLength;

            lastKey = key;
            yield return (key, value);
        }
    }

    // --- Write-ahead log (.log) -------------------------------------------------------------------

    private const int LogBlockSize = 32768;

    private static IEnumerable<Entry> ReadLog(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        byte[] fragment = [];

        int pos = 0;
        while (pos < data.Length)
        {
            int blockEnd = Math.Min(pos + LogBlockSize, data.Length);
            int bp = pos;
            while (bp + 7 <= blockEnd)
            {
                // Record header: 4-byte CRC (unchecked) + 2-byte length + 1-byte type.
                int length = data[bp + 4] | (data[bp + 5] << 8);
                byte recordType = data[bp + 6];
                bp += 7;
                if (recordType == 0 || bp + length > blockEnd)
                    break; // zero padding to the block boundary, or truncated tail

                byte[] chunk = data.AsSpan(bp, length).ToArray();
                bp += length;

                // FULL=1, FIRST=2, MIDDLE=3, LAST=4 — reassemble batches that span records.
                switch (recordType)
                {
                    case 1:
                        foreach (Entry e in ParseBatch(chunk)) yield return e;
                        break;
                    case 2:
                        fragment = chunk;
                        break;
                    case 3:
                        fragment = Concat(fragment, chunk);
                        break;
                    case 4:
                        foreach (Entry e in ParseBatch(Concat(fragment, chunk))) yield return e;
                        fragment = [];
                        break;
                }
            }
            pos += LogBlockSize;
        }
    }

    /// <summary>Parses a WriteBatch: 8-byte sequence + 4-byte count, then put/delete records.</summary>
    private static IEnumerable<Entry> ParseBatch(byte[] batch)
    {
        if (batch.Length < 12)
            yield break;

        ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(batch.AsSpan(0, 8));
        int count = BinaryPrimitives.ReadInt32LittleEndian(batch.AsSpan(8, 4));

        int pos = 12;
        for (int i = 0; i < count && pos < batch.Length; i++)
        {
            byte recordType = batch[pos++];
            if (recordType == TypeValue)
            {
                int keyLength = (int)ReadVarint(batch, ref pos);
                byte[] key = batch[pos..(pos + keyLength)];
                pos += keyLength;
                int valueLength = (int)ReadVarint(batch, ref pos);
                byte[] value = batch[pos..(pos + valueLength)];
                pos += valueLength;
                // In the WAL the stored key is the user-key directly; the sequence is per-record.
                yield return new Entry(key, sequence + (ulong)i, TypeValue, value);
            }
            else if (recordType == 0) // deletion
            {
                int keyLength = (int)ReadVarint(batch, ref pos);
                byte[] key = batch[pos..(pos + keyLength)];
                pos += keyLength;
                yield return new Entry(key, sequence + (ulong)i, 0, []);
            }
            else
            {
                yield break; // unknown record type — stop parsing this batch
            }
        }
    }

    // --- helpers ----------------------------------------------------------------------------------

    private static long ReadVarint(byte[] data, ref int pos)
    {
        long result = 0;
        int shift = 0;
        while (true)
        {
            byte b = data[pos++];
            result |= (long)(b & 0x7f) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        if (a.Length == 0) return b;
        var result = new byte[a.Length + b.Length];
        Array.Copy(a, result, a.Length);
        Array.Copy(b, 0, result, a.Length, b.Length);
        return result;
    }
}
