"""Byte-level Snappy decompression vectors — mirrors the C# oracle
tests/Shelfbound.Steam.Tests/SnappyTests.cs. Streams are hand-built from the format spec:
a varint uncompressed length, then literals/copies.
"""

import pytest

from shelfbound_decky import limits, snappy


def test_decompresses_literal_only():
    # len=5, literal tag ((5-1)<<2)=0x10, then "hello".
    data = bytes([0x05, 0x10]) + b"hello"
    assert snappy.decompress(data).decode("ascii") == "hello"


def test_decompresses_two_byte_offset_copy():
    # "abc" literal then a 2-byte-offset copy (len 6, offset 3) -> overlapping "abcabc".
    data = bytes([0x09, 0x08]) + b"abc" + bytes([0x16, 0x03, 0x00])
    assert snappy.decompress(data).decode("ascii") == "abcabcabc"


def test_decompresses_one_byte_offset_copy():
    # "ab" literal then a 1-byte-offset copy (len 6, offset 2) -> overlapping "ababab".
    data = bytes([0x08, 0x04]) + b"ab" + bytes([0x09, 0x02])
    assert snappy.decompress(data).decode("ascii") == "abababab"


def test_decompresses_long_literal_with_length_prefix():
    # 100-byte literal: tag low bits = 60 (one extra length byte), extra byte = 100-1 = 99.
    body = b"\x41" * 100  # 'A' x100
    data = bytes([100, (60 << 2) | 0x00, 99]) + body
    output = snappy.decompress(data)
    assert len(output) == 100
    assert all(b == 0x41 for b in output)


def test_rejects_declared_output_above_resource_limit_before_allocating():
    remaining = limits.MAX_LEVELDB_BLOCK_BYTES + 1
    prefix = bytearray()
    while remaining >= 0x80:
        prefix.append((remaining & 0x7F) | 0x80)
        remaining >>= 7
    prefix.append(remaining)

    with pytest.raises(ValueError, match="output exceeds"):
        snappy.decompress(bytes(prefix))


def test_rejects_copy_that_points_before_decoded_output():
    with pytest.raises(ValueError, match="copy offset"):
        snappy.decompress(bytes([0x04, 0x01, 0x01]))
