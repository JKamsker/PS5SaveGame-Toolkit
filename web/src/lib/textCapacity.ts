const maximumUtf8ByteCapacity = 2_147_483_647;
const encoder = new TextEncoder();

/** TextEncoder replaces lone surrogates. Reject those first so the hint never counts substituted text. */
export function utf8ByteCount(text: string): number | undefined {
  for (const scalar of text) {
    const code = scalar.codePointAt(0)!;
    if (code >= 0xd800 && code <= 0xdfff) return undefined;
  }
  return encoder.encode(text).byteLength;
}

export function validUtf8Capacity(capacity: number): boolean {
  return Number.isInteger(capacity) && capacity >= 0 && capacity <= maximumUtf8ByteCapacity;
}

export function textCapacityError(text: string, capacity: number): string | undefined {
  if (!validUtf8Capacity(capacity)) return "Invalid UTF-8 byte capacity.";
  const used = utf8ByteCount(text);
  if (used === undefined) return "Text must contain valid Unicode scalar sequences.";
  if (used > capacity) return `At most ${capacity} UTF-8 bytes.`;
}

export function textCapacityHint(text: string, capacity: number, compact = false): string {
  if (!validUtf8Capacity(capacity)) return compact ? "Capacity unavailable" : "UTF-8 capacity unavailable. Refresh the view.";
  const used = utf8ByteCount(text);
  if (used === undefined) return compact ? "Invalid Unicode" : "Invalid Unicode. UTF-8 size unavailable; correct the text before saving.";
  const remaining = capacity - used;
  if (compact) return `${used}/${capacity} B · ${Math.abs(remaining)} ${remaining < 0 ? "over" : "left"}`;
  return `${used} / ${capacity} UTF-8 bytes used · ${Math.abs(remaining)} ${Math.abs(remaining) === 1 ? "byte" : "bytes"} ${remaining < 0 ? "over capacity" : "remaining"}`;
}
