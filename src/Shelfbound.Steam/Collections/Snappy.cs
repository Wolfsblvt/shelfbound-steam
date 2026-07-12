namespace Shelfbound.Steam.Collections;

/// <summary>
/// Minimal Snappy <b>decompression</b> (raw block format, no stream framing) — just enough to read the
/// snappy-compressed data blocks inside Steam's Chromium LevelDB. Decompression only; Shelfbound never
/// writes these files. Format:
/// https://github.com/google/snappy/blob/main/format_description.txt
/// </summary>
internal static class Snappy
{
    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        int pos = 0;
        int length = ReadUncompressedLength(input, ref pos);
        if (length > SteamInputLimits.MaxLevelDbBlockBytes)
            throw new InvalidDataException($"Snappy output exceeds the {SteamInputLimits.MaxLevelDbBlockBytes}-byte limit.");

        var output = new byte[length];
        int outPos = 0;

        while (pos < input.Length)
        {
            byte tag = input[pos++];
            int type = tag & 0x03;

            if (type == 0) // literal
            {
                int litLen = tag >> 2;
                if (litLen < 60)
                {
                    litLen += 1;
                }
                else
                {
                    int extra = litLen - 59; // 1..4 trailing length bytes
                    EnsureAvailable(input, pos, extra);
                    litLen = (int)ReadLittleEndian(input, pos, extra) + 1;
                    pos += extra;
                }
                EnsureAvailable(input, pos, litLen);
                EnsureOutputCapacity(output, outPos, litLen);
                input.Slice(pos, litLen).CopyTo(output.AsSpan(outPos));
                pos += litLen;
                outPos += litLen;
            }
            else // copy
            {
                int copyLen, offset;
                if (type == 1) // 1-byte offset
                {
                    EnsureAvailable(input, pos, 1);
                    copyLen = ((tag >> 2) & 0x07) + 4;
                    offset = ((tag >> 5) << 8) | input[pos++];
                }
                else if (type == 2) // 2-byte offset
                {
                    EnsureAvailable(input, pos, 2);
                    copyLen = (tag >> 2) + 1;
                    offset = input[pos] | (input[pos + 1] << 8);
                    pos += 2;
                }
                else // type == 3, 4-byte offset
                {
                    EnsureAvailable(input, pos, 4);
                    copyLen = (tag >> 2) + 1;
                    offset = input[pos] | (input[pos + 1] << 8) | (input[pos + 2] << 16) | (input[pos + 3] << 24);
                    pos += 4;
                }

                // Copies may overlap the output written so far (offset < copyLen), so copy byte-by-byte.
                if (offset <= 0 || offset > outPos)
                    throw new InvalidDataException("Snappy copy offset points outside the decoded output.");
                EnsureOutputCapacity(output, outPos, copyLen);
                int start = outPos - offset;
                for (int i = 0; i < copyLen; i++)
                    output[outPos + i] = output[start + i];
                outPos += copyLen;
            }
        }

        if (outPos != length)
            throw new InvalidDataException("Snappy stream did not produce its declared output length.");

        return output;
    }

    private static int ReadUncompressedLength(ReadOnlySpan<byte> input, ref int pos)
    {
        int result = 0, shift = 0;
        while (true)
        {
            if (pos >= input.Length || shift > 28)
                throw new InvalidDataException("Invalid Snappy length prefix.");
            byte b = input[pos++];
            result |= (b & 0x7f) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
    }

    private static long ReadLittleEndian(ReadOnlySpan<byte> data, int pos, int count)
    {
        long value = 0;
        for (int i = 0; i < count; i++)
            value |= (long)data[pos + i] << (8 * i);
        return value;
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> input, int position, int count)
    {
        if (count < 0 || position < 0 || position > input.Length - count)
            throw new InvalidDataException("Snappy stream is truncated.");
    }

    private static void EnsureOutputCapacity(byte[] output, int position, int count)
    {
        if (count < 0 || position < 0 || position > output.Length - count)
            throw new InvalidDataException("Snappy stream exceeds its declared output length.");
    }
}
