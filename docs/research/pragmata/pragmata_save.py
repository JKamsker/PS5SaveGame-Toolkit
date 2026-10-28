#!/usr/bin/env python3
"""Research reader and size-preserving editor for the supplied PS5 PRAGMATA saves.

Python 3.10+, pip install cryptography. Never modifies an input file.
"""
import argparse
import datetime as dt
import json
from pathlib import Path
import struct

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
try:
    from cryptography.hazmat.decrepit.ciphers.modes import OFB
except ImportError:
    OFB = modes.OFB
from hashes import city64, murmur3

MASK = (1 << 64) - 1
SEED = 0x7da24a9e1479f3d7
PS5_ID = 526253637
MAX_PLAINTEXT = 64 * 1024 * 1024
ELGAMAL_P = int.from_bytes(bytes.fromhex('f33b6fb972a0b72515e45c391829e182ad8a9bdc0a64d3444d79c810ab863717'), 'little')
FORMATS = {2: '?', 3: 'b', 4: 'B', 5: 'h', 6: 'H', 7: 'i', 8: 'I',
           9: 'q', 10: 'Q', 11: 'f', 12: 'd', 13: 'B', 14: 'H'}

def next_state(state):
    # The mixed OUTPUT becomes the next state; see FORMAT.txt.
    z = (state + 0x9e3779b97f4a7c15) & MASK
    z = ((z ^ (z >> 30)) * 0xbf58476d1ce4e5b9) & MASK
    z = ((z ^ (z >> 27)) * 0x94d049bb133111eb) & MASK
    return z ^ (z >> 31)

class Save:
    def __init__(self, data, key=PS5_ID):
        self.original = bytes(data)
        if len(data) < 172 or data[:4] != b'DSSS':
            raise ValueError('Not a DSSS file')
        if struct.unpack_from('<II', data, 4) != (2, 0x10):
            raise ValueError('Only DSSS v2, flags 0x10 is supported')
        if struct.unpack_from('<I', data, len(data) - 4)[0] != murmur3(data[:-4]):
            raise ValueError('Outer MurmurHash3 mismatch')
        self.length = struct.unpack_from('<Q', data, len(data) - 12)[0]
        if not 0 < self.length <= MAX_PLAINTEXT:
            raise ValueError('Invalid/unsupported plaintext length')
        state = SEED
        sizes = []
        for _ in range((self.length + 0x3fff) // 0x4000):
            sizes.append(((state & 7) + 1) * 0x4000)
            state = next_state(state)
        state = (state + (~key & MASK)) & MASK
        self.blocks = []
        plaintext = bytearray()
        offset = 16
        self.key_iv_checks_passed = 0
        for size in sizes:
            if offset + 0x210 + size > len(data) - 140:
                raise ValueError('Truncated Mandarin block')
            aes_key, iv = bytearray(), bytearray()
            for _ in range(16):
                state = next_state(state)
                aes_key.append(state & 255)
                iv.append((state >> 8) & 255)
            mask = bytearray()
            for _ in range(0x210):
                state = next_state(state)
                mask.append(state & 255)
            metadata = bytes(a ^ b for a, b in zip(data[offset:offset + 0x210], mask))
            # Independently verify preserved authentication material when possible.
            recovered = bytearray()
            for i in range(4):
                at = i * 128
                x0 = int.from_bytes(metadata[at:at + 64], 'little')
                ct = int.from_bytes(metadata[at + 64:at + 128], 'little')
                divisor = pow(x0, (~key & MASK), ELGAMAL_P)
                if not divisor:
                    raise ValueError('Invalid Mandarin authentication material')
                recovered.extend((ct // divisor & MASK).to_bytes(8, 'little'))
            if recovered == aes_key + iv:
                self.key_iv_checks_passed += 1
            decryptor = Cipher(algorithms.AES(bytes(aes_key)), OFB(bytes(iv))).decryptor()
            decoded = decryptor.update(data[offset + 0x210:offset + 0x210 + size]) + decryptor.finalize()
            used = min(size, self.length - len(plaintext))
            chunk = decoded[:used]
            expected = struct.unpack_from('<Q', metadata, 0x200)[0]
            if city64(chunk) != expected:
                raise ValueError('CityHash64 mismatch (wrong key or damaged block)')
            self.blocks.append({'file_offset': offset, 'plain_offset': len(plaintext),
                                'size': size, 'used': used, 'mask': bytes(mask)})
            plaintext.extend(chunk)
            offset += 0x210 + size
            if len(plaintext) == self.length:
                break
        if len(plaintext) != self.length or offset + 140 != len(data):
            raise ValueError('Mandarin layout length mismatch')
        self.plaintext = bytes(plaintext)

    def rebuild(self, plaintext):
        """Re-encrypt edits of the same length, preserving all unknown bytes.

        OFB permits C_new = C_old XOR P_old XOR P_new with the original key/IV.
        Existing ElGamal metadata and RSA trailer remain valid and untouched.
        """
        if len(plaintext) != self.length:
            raise ValueError('Only size-preserving edits are supported')
        output = bytearray(self.original)
        for block in self.blocks:
            p, n, off = block['plain_offset'], block['used'], block['file_offset']
            old, new = self.plaintext[p:p + n], plaintext[p:p + n]
            if old == new:
                continue
            cipher_at = off + 0x210
            output[cipher_at:cipher_at + n] = bytes(c ^ a ^ b for c, a, b in
                zip(output[cipher_at:cipher_at + n], old, new))
            checksum = struct.pack('<Q', city64(new))
            output[off + 0x200:off + 0x208] = bytes(a ^ b for a, b in
                zip(checksum, block['mask'][0x200:0x208]))
        struct.pack_into('<I', output, len(output) - 4, murmur3(output[:-4]))
        return bytes(output)

    def file_offset(self, plaintext_offset):
        for b in self.blocks:
            if b['plain_offset'] <= plaintext_offset < b['plain_offset'] + b['used']:
                return b['file_offset'] + 0x210 + plaintext_offset - b['plain_offset']
        raise ValueError('Plaintext offset out of bounds')

class Reader:
    def __init__(self, data, alignment_origin=16):
        self.data, self.pos = data, 0
        self.alignment_origin = alignment_origin
        self.scalars, self.classes = [], []

    def read(self, n):
        if n < 0 or self.pos + n > len(self.data):
            raise ValueError(f'Unexpected EOF at {self.pos:#x}, reading {n}')
        result = self.data[self.pos:self.pos + n]
        self.pos += n
        return result

    def number(self, fmt):
        return struct.unpack('<' + fmt, self.read(struct.calcsize('<' + fmt)))[0]

    def align(self, n):
        if n < 1 or n > 65536:
            raise ValueError(f'Unsupported alignment {n} at {self.pos:#x}')
        # Match REE Save Editor read_sized exactly, even for struct sizes such as 12.
        # This bit-mask rule is not equivalent to rounding up to a multiple of n.
        # The original serializer aligns a cursor after the 16-byte DSSS header.
        # Removing that header changes alignment for 24/32-byte structs.
        absolute = self.pos + self.alignment_origin
        target = ((absolute + n - 1) & ~(n - 1)) - self.alignment_origin
        self.read(target - self.pos)

    def clazz(self, path, depth=0):
        if depth > 100:
            raise ValueError('Nesting limit')
        offset = self.pos
        count, hash_ = self.number('I'), self.number('I')
        if count > 50000:
            raise ValueError('Implausible field count')
        obj = {'class': f'{hash_:08x}', 'offset': offset, 'fields': [], 'path': path}
        self.classes.append(obj)
        for _ in range(count):
            start = self.pos
            h, typ = self.number('I'), self.number('i')
            fpath = path + '/' + f'{h:08x}'
            val = self.value(typ, fpath, depth + 1)
            field = {'hash': f'{h:08x}', 'type': typ, 'offset': start, **val, 'path': fpath}
            obj['fields'].append(field)
            if 'value_offset' in field:
                self.scalars.append(field)
            self.align(4)
        return obj

    def sized(self, typ, size):
        self.align(size)
        offset = self.pos
        if typ == 16:
            return {'value_offset': offset, 'size': size, 'hex': self.read(size).hex()}
        fmt = ({1: 'b', 2: 'h', 4: 'i', 8: 'q'}.get(size) if typ == 1 else FORMATS.get(typ))
        if fmt is None or struct.calcsize('<' + fmt) != size:
            raise ValueError(f'Unsupported type/size: {typ}/{size}')
        return {'value_offset': offset, 'size': size, 'value': self.number(fmt)}

    def value(self, typ, path, depth):
        if typ == 17:
            return {'value': self.clazz(path, depth)}
        if typ == 15:
            self.align(4)
            count = self.number('I')
            offset = self.pos
            text = self.read(count * 2).decode('utf-16le', errors='surrogatepass')
            return {'value_offset': offset, 'size': count * 2, 'value': text}
        if typ != -1:
            self.align(4)
            return self.sized(typ, self.number('I'))
        self.align(4)
        member_type, member_size = self.number('i'), self.number('I')
        count, array_type = self.number('I'), self.number('i')
        if count > 1000000 or array_type not in (0, 1):
            raise ValueError('Unsupported array header')
        array = {'member_type': member_type, 'member_size': member_size,
                 'array_type': array_type, 'count': count, 'values': []}
        if array_type == 1:
            marker = self.number('I')
            if marker == 0xffeeffee:
                array['hashes'] = [f'{self.number("I"):08x}' for _ in range(count)]
            else:
                self.pos -= 4
        for i in range(count):
            item_path = path + f'[{i}]'
            if array_type == 1:
                value = self.clazz(item_path, depth + 1)
            elif member_type == 15:
                value = self.value(15, item_path, depth + 1)
            else:
                value = self.sized(member_type, member_size)
            if array_type == 0:
                value = {**value, 'path': item_path, 'type': member_type}
                self.scalars.append(value)
            array['values'].append(value)
        self.align(4)
        return {'value': array}

    def parse(self):
        roots = []
        while self.pos < len(self.data):
            root = self.number('I')
            roots.append({'root': f'{root:08x}', **self.clazz(f'{root:08x}')})
        if self.pos != len(self.data):
            raise ValueError('Trailing bytes')
        return roots

def fields(obj):
    return {x['hash']: x for x in obj['fields']}

def get_class(reader, class_hash):
    matches = [c for c in reader.classes if c['class'] == class_hash]
    if len(matches) != 1:
        raise ValueError(f'Expected one class {class_hash}, found {len(matches)}')
    return matches[0]

def parse_save(save):
    reader = Reader(save.plaintext)
    roots = reader.parse()
    return reader, roots

def summary(save, reader, roots):
    result = {'format': 'DSSS v2 / Mandarin 0x10', 'plaintext_bytes': save.length,
              'outer_hash_valid': True, 'block_hashes_valid': True,
              'blocks': len(save.blocks), 'key_iv_checks_passed': save.key_iv_checks_passed,
              'root_count': len(roots), 'class_count': len(reader.classes),
              'scalar_count': len(reader.scalars), 'kind': 'system' if len(roots) == 4 else 'gameplay'}
    def root_value(root):
        found = [r for r in roots if r['root'] == root]
        return fields(found[0]) if found else {}
    result['region_hash'] = root_value('282b8920')['a4e87d84']['value']
    header = root_value('54fa76ae')
    if header:
        names = {'6182c62b': 'stage_id', '49864485': 'unknown_header_u32',
                 'a6eaf825': 'description_id', 'd628891d': 'timestamp_ticks',
                 '4e8845ca': 'playtime_microseconds', '957de5af': 'playthrough_count_field',
                 'c7119340': 'cleared', '4c5f732d': 'difficulty_id', 'd686b6dc': 'inherit_mode_id'}
        result['header'] = {name: header[h]['value'] for h, name in names.items()}
        result['header']['timestamp_no_timezone'] = str(dt.datetime(1, 1, 1) + dt.timedelta(microseconds=header['d628891d']['value'] // 10))
        result['header']['playtime_hours'] = round(header['4e8845ca']['value'] / 3600000000, 3)
        result['header_semantic_notes'] = {
            'stage_id': 'Unconfirmed label inherited from the supplied summary; raw hash 6182c62b',
            'description_id': 'Unconfirmed label inherited from the supplied summary; raw hash a6eaf825',
            'playthrough_count_field': 'Unconfirmed gameplay meaning; raw hash 957de5af',
            'difficulty_id': 'Name hash matches _DifficultyHash; enum meaning unresolved',
            'inherit_mode_id': 'Name hash matches _InheritModeHash; enum meaning unresolved'}
        inv = fields(get_class(reader, '3a83076c'))
        entries = inv['3185b7a5']['value']['values']
        result['inventory'] = {'equipped_item_id': inv['d34b7230']['value'], 'item_slots': len(entries),
            'items': [{'fields': {h: f['value'] for h, f in fields(item).items()},
                       'offsets': {h: f['value_offset'] for h, f in fields(item).items()}}
                      for item in entries]}
        clock = fields(get_class(reader, '7d74c37a'))
        result['clock'] = {name: clock[h]['value'] for h, name in
            {'0c6dbd95': '_GameElapsedTime', '61ca51b7': '_DemoSpendingTime', 'a4fd8835': '_PauseSpendingTime'}.items()}
    detail = root_value('85e904c1')
    if detail:
        result['save_detail'] = {name: detail[h]['value'] for h, name in
            {'b6182d04': 'title', 'c2468a16': 'display_name', 'bc922b61': 'description', '27945a5a': 'slot_index'}.items()}
    return result

def write_new(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation prevents accidental overwrites, including input files.
    with path.open('xb') as f:
        f.write(data)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    for name in ['inspect', 'dump', 'decrypt', 'roundtrip', 'set-field', 'set-item']:
        p = sub.add_parser(name)
        p.add_argument('input', type=Path)
        p.add_argument('--key', type=lambda s: int(s, 0), default=PS5_ID)
        if name != 'inspect':
            p.add_argument('output', type=Path)
        if name == 'set-field':
            p.add_argument('path', help='Exact scalar path from dump (hashes and array indices)')
            p.add_argument('value', help='JSON primitive, e.g. 123 or false')
        if name == 'set-item':
            p.add_argument('item_id', type=lambda s: int(s, 0))
            p.add_argument('quantity', type=int)
    args = parser.parse_args()
    save = Save(args.input.read_bytes(), args.key)
    reader, roots = parse_save(save)
    if args.command == 'inspect':
        print(json.dumps(summary(save, reader, roots), indent=2, ensure_ascii=True, allow_nan=False))
        return
    if args.command == 'dump':
        content = {'summary': summary(save, reader, roots), 'roots': roots,
                   'scalars': [{**f, 'ciphertext_offset': save.file_offset(f['value_offset'])} for f in reader.scalars]}
        write_new(args.output, json.dumps(content, indent=2, ensure_ascii=True, allow_nan=False).encode())
        return
    if args.command == 'decrypt':
        write_new(args.output, save.plaintext)
        return
    if args.command == 'roundtrip':
        output = save.rebuild(save.plaintext)
        if output != save.original:
            raise ValueError('No-op roundtrip was not identical')
    else:
        if args.command == 'set-field':
            matches = [f for f in reader.scalars if f['path'] == args.path]
            value = json.loads(args.value)
            if len(matches) != 1:
                raise ValueError('Scalar path must resolve to exactly one field')
            field = matches[0]
        else:
            if args.quantity < 0 or args.quantity > 2147483647:
                raise ValueError('Quantity must be between 0 and 2147483647')
            inv = fields(get_class(reader, '3a83076c'))
            entries = inv['3185b7a5']['value']['values']
            matches = [fields(e) for e in entries if fields(e)[ITEM_ID_HASH]['value'] == args.item_id]
            if len(matches) != 1:
                raise ValueError('Item ID must already exist exactly once')
            field, value = matches[0][QUANTITY_HASH], args.quantity
        typ, size = field['type'], field['size']
        fmt = ({1:'b', 2:'h', 4:'i', 8:'q'}.get(size) if typ == 1 else FORMATS.get(typ))
        if fmt is None or struct.calcsize('<' + fmt) != size:
            raise ValueError('Only fixed-size primitive fields can be edited')
        if typ == 2 and type(value) is not bool:
            raise ValueError('Boolean field requires true or false')
        if typ not in (2, 11, 12) and type(value) is not int:
            raise ValueError('Integer field requires an integer')
        if typ in (11, 12):
            import math
            if type(value) not in (int, float) or not math.isfinite(value):
                raise ValueError('Float field requires a finite number')
        plaintext = bytearray(save.plaintext)
        struct.pack_into('<' + fmt, plaintext, field['value_offset'], value)
        output = save.rebuild(plaintext)
        checked = Save(output, args.key)
        if checked.plaintext != bytes(plaintext):
            raise ValueError('Edit verification failed')
        print(f"Changed {field['path']} at plaintext {field['value_offset']:#x}: {field['value']} -> {value}")
    write_new(args.output, output)
    print(f'Wrote {args.output} ({len(output)} bytes)')

# Filled from independently parsed Item records; see FORMAT.txt.
ITEM_ID_HASH = '1b5a30c0'
QUANTITY_HASH = 'd18b252d'

if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, struct.error) as error:
        raise SystemExit(str(error))
