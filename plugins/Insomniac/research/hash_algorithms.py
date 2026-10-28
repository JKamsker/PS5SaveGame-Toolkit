"""Independent, research-only implementations; see README sources for parameters."""

import struct
import zlib

MASK32 = (1 << 32) - 1
MASK64 = (1 << 64) - 1


def crc_table(polynomial, width, reflected):
    mask = (1 << width) - 1
    table = []
    for byte in range(256):
        value = byte if reflected else byte << (width - 8)
        for _ in range(8):
            if reflected:
                value = (value >> 1) ^ (polynomial if value & 1 else 0)
            else:
                value = ((value << 1) ^ (polynomial if value >> (width - 1) else 0)) & mask
        table.append(value)
    return table


CRC64_ECMA = crc_table(0x42F0E1EBA9EA3693, 64, False)
CRC64_XZ = crc_table(0xC96C5795D7870F42, 64, True)
CRC64_ISO = crc_table(0xD800000000000000, 64, True)
CRC32C = crc_table(0x82F63B78, 32, True)


def crc_reflected(data, table, initial, final):
    value = initial
    for byte in data:
        value = (value >> 8) ^ table[(value ^ byte) & 255]
    return value ^ final


def crc64_ecma(data):
    value = 0
    for byte in data:
        value = ((value << 8) & MASK64) ^ CRC64_ECMA[(value >> 56) ^ byte]
    return value


def fnv(data, width, alternate):
    value, prime = (0x811C9DC5, 0x01000193) if width == 32 else (0xCBF29CE484222325, 0x100000001B3)
    mask = (1 << width) - 1
    for byte in data:
        if alternate:
            value = ((value ^ byte) * prime) & mask
        else:
            value = ((value * prime) & mask) ^ byte
    return value


def rotate32(value, count):
    return ((value << count) | (value >> (32 - count))) & MASK32


def murmur3(data, seed=0):
    """MurmurHash3 x86_32, including its little-endian tail and avalanche."""
    value = seed
    block_end = len(data) // 4 * 4
    for (block,) in struct.iter_unpack("<I", data[:block_end]):
        block = rotate32((block * 0xCC9E2D51) & MASK32, 15)
        value ^= (block * 0x1B873593) & MASK32
        value = (rotate32(value, 13) * 5 + 0xE6546B64) & MASK32
    if block_end != len(data):
        block = int.from_bytes(data[block_end:], "little")
        block = rotate32((block * 0xCC9E2D51) & MASK32, 15)
        value ^= (block * 0x1B873593) & MASK32
    value ^= len(data)
    value ^= value >> 16
    value = (value * 0x85EBCA6B) & MASK32
    value ^= value >> 13
    value = (value * 0xC2B2AE35) & MASK32
    return value ^ (value >> 16)


def hashes(data):
    return {
        "CRC32/ISO-HDLC": (zlib.crc32(data), 4),
        "CRC32/JAMCRC": (zlib.crc32(data) ^ MASK32, 4),
        "CRC32/engine-name-seed": (zlib.crc32(data, 0xEDB88320 ^ MASK32) ^ MASK32, 4),
        "CRC32C": (crc_reflected(data, CRC32C, MASK32, MASK32), 4),
        "CRC64/ECMA-182": (crc64_ecma(data), 8),
        "CRC64/XZ": (crc_reflected(data, CRC64_XZ, MASK64, MASK64), 8),
        "CRC64/GO-ISO": (crc_reflected(data, CRC64_ISO, MASK64, MASK64), 8),
        "FNV1/32": (fnv(data, 32, False), 4),
        "FNV1a/32": (fnv(data, 32, True), 4),
        "FNV1/64": (fnv(data, 64, False), 8),
        "FNV1a/64": (fnv(data, 64, True), 8),
        "Murmur3/x86_32/seed0": (murmur3(data), 4),
        "Murmur3/x86_32/seedFFFFFFFF": (murmur3(data, MASK32), 4),
    }


def verify_vectors():
    values = hashes(b"123456789")
    assert values["CRC32/ISO-HDLC"][0] == 0xCBF43926
    assert values["CRC32C"][0] == 0xE3069283
    assert values["CRC64/ECMA-182"][0] == 0x6C40DF5F0B497347
    assert values["CRC64/XZ"][0] == 0x995DC9BBDF1939FA
    assert values["CRC64/GO-ISO"][0] == 0xB90956C775A41001
    assert fnv(b"hello", 32, True) == 0x4F9F2CAB
    assert fnv(b"hello", 64, True) == 0xA430D84680AABD0B
    assert murmur3(b"foo") == 0xF6A5C420
    assert murmur3(b"") == 0


if __name__ == "__main__":
    verify_vectors()
    print("Hash vectors passed.")
