"""Small read-only checks used by inspect_samples.py. No upstream code is executed.

LZ4 block grammar: https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md
Container references are cited in PPSA04018.md and PPSA04027.md.
"""
import struct
import re
import xml.etree.ElementTree as xml

MAX_DECODED = 64 * 1024 * 1024


def decode_lz4(data, expected):
    if not 0 <= expected <= MAX_DECODED:
        raise ValueError("Output limit")
    output = bytearray()
    position = 0

    def read_length(initial):
        nonlocal position
        length = initial
        if initial == 15:
            while True:
                extra = data[position]
                position += 1
                length += extra
                if extra != 255:
                    break
        return length

    while position < len(data):
        token = data[position]
        position += 1
        literals = read_length(token >> 4)
        if position + literals > len(data) or len(output) + literals > expected:
            raise ValueError("Literal bounds")
        output.extend(data[position:position + literals])
        position += literals
        if position == len(data):
            break
        offset = struct.unpack_from("<H", data, position)[0]
        position += 2
        length = read_length(token & 15) + 4
        if offset == 0 or offset > len(output) or len(output) + length > expected:
            raise ValueError("Match bounds")
        for _ in range(length):
            output.append(output[-offset])
    if len(output) != expected:
        raise ValueError("Decoded length mismatch")
    return bytes(output)


def inspect(data):
    result = {}
    if data.startswith(b"SNFHFZLC"):
        count, header_size = struct.unpack_from("<II", data, 8)
        chunks = [struct.unpack_from("<III", data, 16 + i * 12) for i in range(count)]
        output = bytearray()
        position = header_size
        for compressed, expanded, end in chunks:
            if len(output) + expanded > MAX_DECODED or position + compressed > len(data):
                raise ValueError("Container output limit")
            output.extend(decode_lz4(data[position:position + compressed], expanded))
            position += compressed
            if end not in (0, position):
                raise ValueError("Chunk end mismatch")
        result["witcher_lz4"] = {"count": count, "header_size": header_size,
                                  "first_entry": chunks[0], "decoded_size": len(output),
                                  "SAV3_offset": output.find(b"SAV3")}
    if data.startswith(b"VASC") and data[25:29] == b"FZLC":
        count = struct.unpack_from("<I", data, 29)[0]
        chunks = [struct.unpack_from("<III", data, 33 + i * 12) for i in range(count)]
        output = bytearray()
        for offset, compressed, expanded in chunks:
            if len(output) + expanded > MAX_DECODED or offset + compressed > len(data):
                raise ValueError("Container bounds")
            raw = data[offset:offset + compressed]
            decoded = decode_lz4(raw[8:], expanded) if raw.startswith(b"4ZLX") else raw
            if len(decoded) != expanded:
                raise ValueError("Chunk size mismatch")
            output.extend(decoded)
        result["cyberpunk_lz4"] = {"count": count, "first_entry": chunks[0], "decoded_size": len(output)}
    if data.startswith(b"1TAD"):
        size = struct.unpack_from("<I", data, 8)[0]
        count, unknown_count = struct.unpack_from("<HH", data, 12)
        sections = [struct.unpack_from("<III", data, 16 + 12 * i) for i in range(count)]
        result["dat1"] = {"declared_size": size, "sections": count, "unknown_count": unknown_count,
                          "all_sections_in_bounds": all(offset + size <= len(data) for _, offset, size in sections),
                          "first_section": sections[0] if sections else None}
    if data[16:32] == b"RESD" * 4:
        position = 0
        count = 0
        while position + 16 <= len(data) and count < 1000:
            count += 1
            next_position = struct.unpack_from("<I", data, position + 4)[0]
            if next_position == 0:
                break
            if next_position <= position or next_position >= len(data):
                raise ValueError("Invalid chunk link")
            position = next_position
        result["ff7_chunks"] = {"count_with_tail": count, "tail_offset": position,
                                "tail_ends_at_eof": position + 16 == len(data)}
    if data.find(b"<PreInstall>") >= 0:
        start = data.find(b"<PreInstall>")
        end = data.find(b"\x00", start)
        try:
            xml.fromstring(data[start:end])
            compliant = True
        except xml.ParseError:
            compliant = False
        result["astro_xml"] = {"offset": start, "end": end, "root": "PreInstall", "xml_compliant": compliant,
                               "opening_tags": len(re.findall(rb"<[A-Za-z_0-9]+>", data[start:end]))}
    return result


assert decode_lz4(b"\x50hello", 5) == b"hello"
