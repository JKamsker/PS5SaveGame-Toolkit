#!/usr/bin/env python3
"""Run real-file integrity, parse, mutation, and reversibility checks."""
import argparse
from pathlib import Path
import struct
from pragmata_save import Save, parse_save, fields, get_class, QUANTITY_HASH
from hashes import murmur3

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', type=Path, help='Extracted PPSA02530 directory')
args = parser.parse_args()
paths = sorted(p for p in args.directory.rglob('*.bin') if p.name.startswith('data'))
if not paths:
    raise SystemExit('No data*.bin files found')
for path in paths:
    data = path.read_bytes()
    save = Save(data)
    reader, roots = parse_save(save)
    assert save.key_iv_checks_passed == len(save.blocks), 'Key/IV metadata mismatch'
    assert save.rebuild(save.plaintext) == data, 'No-op roundtrip mismatch'
    if any(c['class'] == '3a83076c' for c in reader.classes):
        inv = fields(get_class(reader, '3a83076c'))
        target = fields(inv['3185b7a5']['value']['values'][5])[QUANTITY_HASH]
        plain = bytearray(save.plaintext)
        struct.pack_into('<i', plain, target['value_offset'], target['value'] + 1)
        modified = save.rebuild(plain)
        checked = Save(modified)
        check_reader, _ = parse_save(checked)
        matches = [f for f in check_reader.scalars if f['path'] == target['path']]
        assert len(matches) == 1 and matches[0]['value'] == target['value'] + 1
        assert checked.plaintext == plain
        assert checked.rebuild(save.plaintext) == data, 'Reversed edit mismatch'
    # Damage encrypted content, then repair ONLY the outer hash. The inner
    # checksum must still reject it, independently of the outer hash.
    corrupt = bytearray(data)
    corrupt[16 + 0x210] ^= 1
    struct.pack_into('<I', corrupt, len(corrupt) - 4, murmur3(corrupt[:-4]))
    try:
        Save(corrupt)
    except ValueError as error:
        assert 'CityHash64 mismatch' in str(error), str(error)
    else:
        raise AssertionError('Corrupt block accepted')
    print(f'{path.name}: PASS ({len(roots)} roots, {len(reader.classes)} classes)')
print(f'{len(paths)} files passed; no files were modified.')
