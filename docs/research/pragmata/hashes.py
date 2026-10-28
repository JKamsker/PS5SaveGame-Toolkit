"""MurmurHash3 x86-32 and CityHash64. CityHash algorithm: Google, MIT license.

See THIRD_PARTY_NOTICES.txt for the CityHash copyright notice.
"""
import struct

MASK64 = (1 << 64) - 1
K0, K1, K2 = 0xc3a5c85c97cb3127, 0xb492b66fbe98f273, 0x9ae16a3b2f90404f

def murmur3(data, seed=0xffffffff):
    if isinstance(data, str):
        data = data.encode('utf-8')
    h = seed
    def rol(x, n): return ((x << n) | (x >> (32 - n))) & 0xffffffff
    n = len(data) & ~3
    for (k,) in struct.iter_unpack('<I', data[:n]):
        k = (k * 0xcc9e2d51) & 0xffffffff
        k = (rol(k, 15) * 0x1b873593) & 0xffffffff
        h ^= k
        h = (rol(h, 13) * 5 + 0xe6546b64) & 0xffffffff
    k = int.from_bytes(data[n:], 'little')
    if len(data) != n:
        k = (k * 0xcc9e2d51) & 0xffffffff
        k = (rol(k, 15) * 0x1b873593) & 0xffffffff
        h ^= k
    h ^= len(data)
    h ^= h >> 16
    h = (h * 0x85ebca6b) & 0xffffffff
    h ^= h >> 13
    h = (h * 0xc2b2ae35) & 0xffffffff
    return h ^ (h >> 16)

def _rot(x, n):
    x &= MASK64
    return ((x >> n) | (x << (64 - n))) & MASK64

def _shift(x):
    x &= MASK64
    return x ^ (x >> 47)

def _h16(u, v, mul=0x9ddfea08eb382d69):
    a = ((u ^ v) * mul) & MASK64
    a ^= a >> 47
    b = ((v ^ a) * mul) & MASK64
    b ^= b >> 47
    return (b * mul) & MASK64

def city64(s):
    """CityHash64, with unsigned 64-bit arithmetic."""
    n = len(s)
    def f(i): return struct.unpack_from('<Q', s, i)[0]
    def bswap(x): return int.from_bytes((x & MASK64).to_bytes(8, 'little'), 'big')
    def weak(i, a, b):
        w, x, y, z = struct.unpack_from('<QQQQ', s, i)
        a = (a + w) & MASK64
        b = _rot(b + a + z, 21)
        c = a
        a = (a + x + y) & MASK64
        b = (b + _rot(a, 44)) & MASK64
        return (a + z) & MASK64, (b + c) & MASK64
    if n <= 16:
        if n >= 8:
            mul = (K2 + n * 2) & MASK64
            a, b = (f(0) + K2) & MASK64, f(n - 8)
            c = (_rot(b, 37) * mul + a) & MASK64
            d = ((_rot(a, 25) + b) * mul) & MASK64
            return _h16(c, d, mul)
        if n >= 4:
            a = struct.unpack_from('<I', s, 0)[0]
            b = struct.unpack_from('<I', s, n - 4)[0]
            return _h16(n + (a << 3), b, K2 + n * 2)
        if n:
            y, z = s[0] + (s[n >> 1] << 8), n + (s[-1] << 2)
            return (_shift(((y * K2) & MASK64) ^ ((z * K0) & MASK64)) * K2) & MASK64
        return K2
    if n <= 32:
        mul = K2 + n * 2
        a, b = (f(0) * K1) & MASK64, f(8)
        c, d = (f(n - 8) * mul) & MASK64, (f(n - 16) * K2) & MASK64
        return _h16((_rot(a + b, 43) + _rot(c, 30) + d) & MASK64,
                    (a + _rot(b + K2, 18) + c) & MASK64, mul)
    if n <= 64:
        mul = K2 + n * 2
        a, b, c, d = (f(0) * K2) & MASK64, f(8), f(n - 24), f(n - 32)
        e, f0, g, h = (f(16) * K2) & MASK64, (f(24) * 9) & MASK64, f(n - 8), (f(n - 16) * mul) & MASK64
        u = (_rot(a + g, 43) + (_rot(b, 30) + c) * 9) & MASK64
        v = (((a + g) & MASK64) ^ d) + f0 + 1
        v &= MASK64
        w = (bswap((u + v) * mul) + h) & MASK64
        x = (_rot(e + f0, 42) + c) & MASK64
        y = ((bswap((v + w) * mul) + g) * mul) & MASK64
        z = (e + f0 + c) & MASK64
        a = (bswap((x + z) * mul + y) + b) & MASK64
        b = (_shift((z + a) * mul + d + h) * mul) & MASK64
        return (b + x) & MASK64
    x = f(n - 40)
    y = (f(n - 16) + f(n - 56)) & MASK64
    z = _h16((f(n - 48) + n) & MASK64, f(n - 24))
    v = weak(n - 64, n, z)
    w = weak(n - 32, (y + K1) & MASK64, x)
    x = (x * K1 + f(0)) & MASK64
    for i in range(0, (n - 1) & ~63, 64):
        x = (_rot(x + y + v[0] + f(i + 8), 37) * K1) & MASK64
        y = (_rot(y + v[1] + f(i + 48), 42) * K1) & MASK64
        x ^= w[1]
        y = (y + v[0] + f(i + 40)) & MASK64
        z = (_rot(z + w[0], 33) * K1) & MASK64
        v = weak(i, (v[1] * K1) & MASK64, (x + w[0]) & MASK64)
        w = weak(i + 32, (z + w[1]) & MASK64, (y + f(i + 16)) & MASK64)
        z, x = x, z
    return _h16((_h16(v[0], w[0]) + _shift(y) * K1 + z) & MASK64,
                (_h16(v[1], w[1]) + x) & MASK64)
