import type { ExactNumberSpec } from "../api/protocol";

// Keep these limits aligned with SDK ExactJsonNumber. No numeric lexeme passes through Number.
const defaultMaxLength = 4096;
const hardMaxLength = 16384;
const defaultMaxExponent = 1_000_000;
const hardMaxExponent = 1_000_000_000;
const grammar = /-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?/y;

function validateLexeme(text: string, maxLength: number, maxExponent: number): string | undefined {
  if (text.length > maxLength) return `At most ${maxLength} characters in a JSON number.`;
  grammar.lastIndex = 0;
  if (grammar.exec(text)?.[0] !== text) return "Expected a JSON number (no whitespace, leading +, or leading zeros).";
  const exponent = text.split(/[eE]/)[1];
  if (exponent !== undefined) {
    const magnitude = BigInt(exponent.replace(/^[+-]/, ""));
    if (magnitude > BigInt(maxExponent)) return `Absolute exponent must be at most ${maxExponent}.`;
  }
  return undefined;
}

function parts(text: string) {
  const [mantissa = "", exponent = "0"] = text.split(/[eE]/);
  const fractionLength = mantissa.includes(".") ? mantissa.length - mantissa.indexOf(".") - 1 : 0;
  const digits = mantissa.replace(/^-/, "").replace(".", "").replace(/^0+/, "");
  return {
    sign: digits.length === 0 ? 0 : text.startsWith("-") ? -1 : 1,
    digits,
    order: BigInt(exponent) - BigInt(fractionLength) + BigInt(digits.length),
  };
}

/** Exact decimal comparison bounded by lexeme length; never expand an exponent into zeros. */
function compare(left: string, right: string): number {
  const a = parts(left), b = parts(right);
  if (a.sign !== b.sign) return a.sign - b.sign;
  if (a.sign === 0) return 0;
  if (a.order !== b.order) return a.sign * (a.order > b.order ? 1 : -1);
  const width = Math.max(a.digits.length, b.digits.length);
  const x = a.digits.padEnd(width, "0"), y = b.digits.padEnd(width, "0");
  return a.sign * (x === y ? 0 : x > y ? 1 : -1);
}

export function exactNumberError(text: string, spec: ExactNumberSpec): string | undefined {
  const maxLength = spec.maxLength ?? defaultMaxLength;
  const maxExponent = spec.maxAbsoluteExponent ?? defaultMaxExponent;
  if (!Number.isInteger(maxLength) || maxLength < 1 || maxLength > hardMaxLength ||
      !Number.isInteger(maxExponent) || maxExponent < 0 || maxExponent > hardMaxExponent)
    return "Invalid exact-number resource limits.";
  const error = validateLexeme(text, maxLength, maxExponent);
  if (error) return error;
  for (const bound of [spec.min, spec.max]) {
    if (bound !== undefined && validateLexeme(bound, hardMaxLength, hardMaxExponent)) return "Invalid exact-number range bound.";
  }
  if (spec.min !== undefined && spec.max !== undefined && compare(spec.min, spec.max) > 0) return "Invalid exact-number range.";
  if (spec.min !== undefined && compare(text, spec.min) < 0) return `Must be at least ${spec.min}.`;
  if (spec.max !== undefined && compare(text, spec.max) > 0) return `Must be at most ${spec.max}.`;
  return undefined;
}
