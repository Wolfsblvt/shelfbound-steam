using System.Text;
using Shouldly;
using Shelfbound.Steam.Collections;

namespace Shelfbound.Steam.Tests;

public class SnappyTests
{
    // Snappy streams hand-built from the format spec: a varint uncompressed length, then literals/copies.

    [Fact]
    public void Decompresses_literal_only()
    {
        // len=5, literal tag ((5-1)<<2)=0x10, then "hello".
        byte[] input = [0x05, 0x10, .. "hello"u8];
        Encoding.ASCII.GetString(Snappy.Decompress(input)).ShouldBe("hello");
    }

    [Fact]
    public void Decompresses_two_byte_offset_copy()
    {
        // "abc" literal then a 2-byte-offset copy (len 6, offset 3) -> overlapping "abcabc".
        byte[] input = [0x09, 0x08, .. "abc"u8, 0x16, 0x03, 0x00];
        Encoding.ASCII.GetString(Snappy.Decompress(input)).ShouldBe("abcabcabc");
    }

    [Fact]
    public void Decompresses_one_byte_offset_copy()
    {
        // "ab" literal then a 1-byte-offset copy (len 6, offset 2) -> overlapping "ababab".
        byte[] input = [0x08, 0x04, .. "ab"u8, 0x09, 0x02];
        Encoding.ASCII.GetString(Snappy.Decompress(input)).ShouldBe("abababab");
    }

    [Fact]
    public void Decompresses_long_literal_with_length_prefix()
    {
        // 100-byte literal: tag low bits = 60 (one extra length byte), extra byte = 100-1 = 99.
        byte[] body = Enumerable.Repeat((byte)0x41, 100).ToArray(); // 'A' x100
        byte[] input = [100, (60 << 2) | 0x00, 99, .. body];
        byte[] output = Snappy.Decompress(input);
        output.Length.ShouldBe(100);
        output.ShouldAllBe(b => b == 0x41);
    }
}
