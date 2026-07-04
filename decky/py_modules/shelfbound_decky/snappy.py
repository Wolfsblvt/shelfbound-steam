"""Minimal Snappy *decompression* (raw block format, no stream framing).

Mirrors src/Shelfbound.Steam/Collections/Snappy.cs — just enough to read the
snappy-compressed data blocks inside Steam's Chromium LevelDB. Decompression only;
Shelfbound never writes these files. Format:
https://github.com/google/snappy/blob/main/format_description.txt
"""

from __future__ import annotations


def decompress(data: bytes) -> bytes:
    """Decompresses a raw Snappy block into its original bytes."""
    pos = 0
    length, pos = _read_uncompressed_length(data, pos)
    output = bytearray(length)
    out_pos = 0

    while pos < len(data):
        tag = data[pos]
        pos += 1
        tag_type = tag & 0x03

        if tag_type == 0:  # literal
            lit_len = tag >> 2
            if lit_len < 60:
                lit_len += 1
            else:
                extra = lit_len - 59  # 1..4 trailing length bytes
                lit_len = _read_little_endian(data, pos, extra) + 1
                pos += extra
            output[out_pos:out_pos + lit_len] = data[pos:pos + lit_len]
            pos += lit_len
            out_pos += lit_len
        else:  # copy
            if tag_type == 1:  # 1-byte offset
                copy_len = ((tag >> 2) & 0x07) + 4
                offset = ((tag >> 5) << 8) | data[pos]
                pos += 1
            elif tag_type == 2:  # 2-byte offset
                copy_len = (tag >> 2) + 1
                offset = data[pos] | (data[pos + 1] << 8)
                pos += 2
            else:  # tag_type == 3, 4-byte offset
                copy_len = (tag >> 2) + 1
                offset = (data[pos] | (data[pos + 1] << 8)
                          | (data[pos + 2] << 16) | (data[pos + 3] << 24))
                pos += 4

            # Copies may overlap the output written so far (offset < copy_len), so
            # copy byte-by-byte rather than slicing.
            start = out_pos - offset
            for i in range(copy_len):
                output[out_pos + i] = output[start + i]
            out_pos += copy_len

    return bytes(output)


def _read_uncompressed_length(data: bytes, pos: int) -> tuple[int, int]:
    result = 0
    shift = 0
    while True:
        b = data[pos]
        pos += 1
        result |= (b & 0x7F) << shift
        if (b & 0x80) == 0:
            return result, pos
        shift += 7


def _read_little_endian(data: bytes, pos: int, count: int) -> int:
    value = 0
    for i in range(count):
        value |= data[pos + i] << (8 * i)
    return value
