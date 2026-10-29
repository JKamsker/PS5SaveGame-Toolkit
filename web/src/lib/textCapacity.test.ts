import { expect, it } from "vitest";
import { parseDraft } from "./values";
import { textCapacityHint, utf8ByteCount } from "./textCapacity";

it.each([["", 0], ["ASCII", 5], ["é", 2], ["e\u0301", 3], ["日本", 6], ["🎮", 4], ["\uFEFF", 3], ["\0", 1]] as const)("counts logical UTF-8 bytes and enforces the inclusive boundary (%s)", (text, bytes) => {
  expect(utf8ByteCount(text)).toBe(bytes);
  expect(parseDraft({ kind: "text", maxUtf8Bytes: bytes }, text)).toEqual({ ok: true, value: text });
  expect(parseDraft({ kind: "text", maxUtf8Bytes: bytes }, text + "a").ok).toBe(false);
});

it.each(["\ud800", "\udc00", "a\ud800b"])("does not count replacement bytes for malformed scalars", text => {
  expect(utf8ByteCount(text)).toBeUndefined();
  expect(parseDraft({ kind: "text", maxUtf8Bytes: 100 }, text).ok).toBe(false);
  expect(textCapacityHint(text, 100)).toContain("Invalid Unicode");
});

it.each([-1, 0.5, NaN, Infinity, 2_147_483_648])("rejects invalid capacity metadata (%s)", maxUtf8Bytes => {
  expect(parseDraft({ kind: "text", maxUtf8Bytes }, "").ok).toBe(false);
});

it("keeps legacy UTF-16 limits and line policies independent", () => {
  expect(parseDraft({ kind: "text", maxLength: 2, maxUtf8Bytes: 4 }, "🎮").ok).toBe(true);
  expect(parseDraft({ kind: "text", maxLength: 1, maxUtf8Bytes: 4 }, "🎮").ok).toBe(false);
  expect(parseDraft({ kind: "text", maxUtf8Bytes: 3 }, "a\nb").ok).toBe(false);
  expect(parseDraft({ kind: "text", maxUtf8Bytes: 3, multiline: true }, "a\nb").ok).toBe(true);
  expect(parseDraft({ kind: "text" }, "🎮".repeat(100)).ok).toBe(true);
});
