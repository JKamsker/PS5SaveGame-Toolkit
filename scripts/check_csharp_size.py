"""Check non-generated C# source size without inspecting the working tree by accident.

The staged mode reads blobs from the Git index, while changed mode reads the committed
HEAD side of an explicit base...HEAD range. Full audit reads tracked files at HEAD. This
keeps hooks and detached-checkout CI deterministic and makes path handling Git's job.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence


DEFAULT_WARNING_LINES = 300
DEFAULT_ERROR_LINES = 500


@dataclass(frozen=True)
class LegacyExemption:
    path: str
    max_lines: int
    reason: str


@dataclass(frozen=True)
class Policy:
    warning_lines: int
    error_lines: int
    generated_globs: tuple[str, ...]
    legacy_exemptions: dict[str, LegacyExemption]


@dataclass(frozen=True)
class SourceBlob:
    path: str
    content: bytes


class GuardError(RuntimeError):
    """An actionable repository or policy error."""


def _normalise_path(path: str) -> str:
    normalised = path.replace("\\", "/")
    while normalised.startswith("./"):
        normalised = normalised[2:]
    return normalised


def load_policy(path: Path) -> Policy:
    try:
        return load_policy_bytes(path.read_bytes(), str(path))
    except OSError as error:
        raise GuardError(f"Cannot read policy {path}: {error}") from error


def load_policy_bytes(content: bytes, source: str) -> Policy:
    try:
        raw = json.loads(content.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise GuardError(f"Policy {source} is not valid UTF-8 JSON: {error}") from error
    if not isinstance(raw, dict):
        raise GuardError("C# size policy must be a JSON object")

    warning = _positive_int(raw.get("warningLines", DEFAULT_WARNING_LINES), "warningLines")
    error = _positive_int(raw.get("errorLines", DEFAULT_ERROR_LINES), "errorLines")
    if warning > DEFAULT_WARNING_LINES or error > DEFAULT_ERROR_LINES:
        raise GuardError(
            f"warningLines must be <= {DEFAULT_WARNING_LINES} and errorLines must be <= "
            f"{DEFAULT_ERROR_LINES}; stricter positive limits are allowed"
        )
    if warning >= error:
        raise GuardError("warningLines must be smaller than errorLines")
    generated = _string_list(raw.get("generatedGlobs", []), "generatedGlobs")
    for pattern in generated:
        if not _is_narrow_generated_glob(pattern):
            raise GuardError(
                f"generatedGlobs entry '{pattern}' must be a repository-relative generated "
                "directory pattern such as 'src/Generated/**/*.cs'"
            )

    exemptions_raw = raw.get("legacyExemptions", [])
    if not isinstance(exemptions_raw, list):
        raise GuardError("legacyExemptions must be an array")
    exemptions: dict[str, LegacyExemption] = {}
    for item in exemptions_raw:
        if not isinstance(item, dict):
            raise GuardError("Each legacy exemption must be an object")
        path_value = item.get("path")
        reason = item.get("reason")
        if not isinstance(path_value, str) or not path_value.strip():
            raise GuardError("Each legacy exemption needs a non-empty path")
        if not isinstance(reason, str) or not reason.strip():
            raise GuardError(f"Legacy exemption {_normalise_path(path_value)} needs a reason")
        policy_segments = path_value.replace("\\", "/").split("/")
        if path_value.startswith("/") or any(segment in ("", ".", "..") for segment in policy_segments):
            raise GuardError(f"Legacy exemption {path_value} must be a repository-relative path")
        if path_value in exemptions:
            raise GuardError(f"Duplicate legacy exemption for {path_value}")
        max_lines = _positive_int(item.get("maxLines"), f"maxLines for {path_value}")
        if max_lines <= DEFAULT_ERROR_LINES or max_lines <= error:
            raise GuardError(
                f"Legacy exemption {path_value} must allow more than {DEFAULT_ERROR_LINES} "
                "and the selected errorLines; otherwise it is not an explicit >500-line exception"
            )
        exemptions[path_value] = LegacyExemption(path_value, max_lines, reason.strip())
    return Policy(warning, error, tuple(generated), exemptions)


_GENERATED_GLOB = re.compile(
    r"^(?P<prefix>(?:[A-Za-z0-9_.-]+/)*[Gg]enerated)/\*\*/\*\.cs$"
)


def _is_narrow_generated_glob(pattern: str) -> bool:
    """Only allow explicit generated-directory prefixes; reject root-wide catchalls."""
    if pattern.startswith("/") or ".." in pattern.split("/"):
        return False
    return _GENERATED_GLOB.fullmatch(pattern) is not None


def _positive_int(value: object, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise GuardError(f"{name} must be a positive integer")
    return value


def _string_list(value: object, name: str) -> list[str]:
    if not isinstance(value, list) or any(not isinstance(item, str) or not item for item in value):
        raise GuardError(f"{name} must be an array of non-empty strings")
    return [_normalise_path(item) for item in value]


def run_git(root: Path, arguments: Sequence[str], *, input_bytes: bytes | None = None) -> bytes:
    command = ["git", "-C", str(root), *arguments]
    try:
        result = subprocess.run(command, input=input_bytes, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, check=False)
    except OSError as error:
        raise GuardError(f"Cannot run Git ({' '.join(arguments)}): {error}") from error
    if result.returncode:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise GuardError(f"Git {' '.join(arguments)} failed ({result.returncode}): {detail}")
    return result.stdout


def parse_name_status(data: bytes) -> list[str]:
    """Return destination paths for added, copied, modified and renamed entries."""
    fields = data.split(b"\0")
    paths: list[str] = []
    index = 0
    while index < len(fields) and fields[index]:
        status = fields[index].decode("ascii", errors="replace")
        index += 1
        if not status:
            break
        if status[0] in "RC":
            if index + 1 >= len(fields):
                raise GuardError("Git returned an incomplete rename/copy record")
            index += 1  # old path; only the destination has source content
            path_bytes = fields[index]
            index += 1
        else:
            if index >= len(fields):
                raise GuardError("Git returned an incomplete name-status record")
            path_bytes = fields[index]
            index += 1
        paths.append(path_bytes.decode("utf-8", errors="surrogateescape"))
    return paths


def staged_paths(root: Path) -> list[str]:
    data = run_git(root, ["diff", "--cached", "--name-status", "--find-renames", "-z",
                          "--diff-filter=ACMRT"])
    return parse_name_status(data)


def changed_paths(root: Path, base: str) -> list[str]:
    if not base:
        raise GuardError("A base commit is required for changed-file mode")
    try:
        run_git(root, ["cat-file", "-e", f"{base}^{{commit}}"])
    except GuardError as error:
        raise GuardError(
            f"Base commit '{base}' is unavailable. Fetch the PR base (for CI, origin/main) "
            "or pass a complete commit ID; shallow/detached checkouts cannot infer it."
        ) from error
    data = run_git(root, ["diff", "--name-status", "--find-renames", "-z",
                          "--diff-filter=ACMRT", f"{base}...HEAD"])
    return parse_name_status(data)


def read_blob(root: Path, path: str, *, staged: bool) -> bytes:
    object_name = f":{path}" if staged else f"HEAD:{path}"
    return run_git(root, ["show", object_name])


def committed_tree(root: Path) -> list[tuple[str, str]]:
    """Return exact HEAD paths and object IDs from NUL-delimited tree records."""
    data = run_git(root, ["ls-tree", "-r", "-z", "HEAD"])
    entries: list[tuple[str, str]] = []
    for record in data.split(b"\0"):
        if not record:
            continue
        try:
            metadata, path_bytes = record.split(b"\t", 1)
            fields = metadata.split()
            object_id = fields[2].decode("ascii")
        except (IndexError, UnicodeDecodeError, ValueError) as error:
            raise GuardError("Git returned an invalid HEAD tree record") from error
        if path_bytes.lower().endswith(b".cs"):
            entries.append((path_bytes.decode("utf-8", errors="surrogateescape"), object_id))
    return entries


def read_object_blobs(root: Path, entries: Sequence[tuple[str, str]]) -> list[SourceBlob]:
    """Read committed blobs by object ID so newline-containing paths never enter a request line."""
    try:
        process = subprocess.Popen(["git", "-C", str(root), "cat-file", "--batch"],
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE)
    except OSError as error:
        raise GuardError(f"Cannot run Git cat-file --batch: {error}") from error
    assert process.stdin is not None and process.stdout is not None
    requests = b"".join(f"{object_id}\n".encode("ascii") for _, object_id in entries)
    stdout, stderr = process.communicate(requests)
    if process.returncode:
        detail = stderr.decode("utf-8", errors="replace").strip()
        raise GuardError(f"Git cat-file --batch failed ({process.returncode}): {detail}")
    offset = 0
    blobs: list[SourceBlob] = []
    for path, _ in entries:
        end = stdout.find(b"\n", offset)
        if end < 0:
            raise GuardError(f"Git returned an incomplete blob header for {path}")
        header = stdout[offset:end].split()
        offset = end + 1
        if len(header) == 2 and header[1] == b"missing":
            raise GuardError(f"HEAD does not contain tracked path {path}")
        if len(header) != 3 or header[1] != b"blob":
            raise GuardError(f"Git returned an invalid blob header for {path}")
        try:
            size = int(header[2])
        except ValueError as error:
            raise GuardError(f"Git returned an invalid blob size for {path}") from error
        content = stdout[offset:offset + size]
        if len(content) != size or stdout[offset + size:offset + size + 1] != b"\n":
            raise GuardError(f"Git returned a truncated blob for {path}")
        blobs.append(SourceBlob(path, content))
        offset += size + 1
    return blobs


def is_csharp(path: str) -> bool:
    return path.lower().endswith(".cs")


def is_generated(path: str, policy: Policy) -> bool:
    return any(path.startswith(pattern[:-len("/**/*.cs")] + "/")
               for pattern in policy.generated_globs)


def collect_blobs(root: Path, paths: Iterable[str], policy: Policy, *, staged: bool) -> list[SourceBlob]:
    selected = [path for path in sorted(set(paths))
                if is_csharp(path) and not is_generated(path, policy)]
    blobs: list[SourceBlob] = []
    for path in selected:
        try:
            blobs.append(SourceBlob(path, read_blob(root, path, staged=staged)))
        except GuardError as error:
            if staged and "pathspec" in str(error).lower():
                raise GuardError(f"Staged C# path disappeared from the index: {path}") from error
            raise
    return blobs


def count_lines(content: bytes, path: str) -> int:
    try:
        text = content.decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise GuardError(f"Cannot decode {path} as UTF-8: {error}") from error
    return len(text.splitlines())


def evaluate(blobs: Iterable[SourceBlob], policy: Policy, *, report_only: bool) -> int:
    warnings = 0
    errors = 0
    for blob in blobs:
        lines = count_lines(blob.content, blob.path)
        exemption = policy.legacy_exemptions.get(blob.path)
        if lines > policy.warning_lines:
            warnings += 1
            suffix = " (approved legacy file)" if exemption else ""
            print(f"WARNING {blob.path}: {lines} lines exceeds {policy.warning_lines}{suffix}")
        if lines <= policy.error_lines:
            continue
        if exemption and lines <= exemption.max_lines:
            print(f"INFO {blob.path}: {lines} lines is covered by legacy exception "
                  f"(limit {exemption.max_lines}; {exemption.reason})")
            continue
        errors += 1
        print(f"ERROR {blob.path}: {lines} lines exceeds hard limit {policy.error_lines}; "
              "split the file or add a narrowly justified exact-path legacy exception")
    print(f"C# size audit: {warnings} warning(s), {errors} error(s), "
          f"{len(list(blobs)) if not isinstance(blobs, list) else len(blobs)} source file(s) checked")
    if report_only and errors:
        print("REPORT ONLY: hard-limit debt is visible but did not fail this audit")
        return 0
    return 1 if errors else 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--staged", action="store_true", help="read C# blobs from the Git index")
    modes.add_argument("--changed", action="store_true", help="check files in base...HEAD")
    modes.add_argument("--all", action="store_true", help="audit all tracked C# files at HEAD")
    parser.add_argument("--base", help="base commit for --changed")
    parser.add_argument("--root", type=Path, help="repository root (defaults to this script's parent)")
    parser.add_argument("--policy", type=Path, help="policy JSON (defaults to .csharp-size-policy.json)")
    parser.add_argument("--report-only", action="store_true", help="print findings but never fail for hard-limit debt")
    return parser


def main(arguments: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(arguments)
    root = (args.root or Path(__file__).resolve().parents[1]).resolve()
    try:
        if args.policy is not None:
            policy = load_policy(args.policy.resolve())
        else:
            policy_object = ":.csharp-size-policy.json" if args.staged else "HEAD:.csharp-size-policy.json"
            policy_source = "staged index" if args.staged else "HEAD"
            policy = load_policy_bytes(run_git(root, ["show", policy_object]), policy_source)
        if args.staged:
            blobs = collect_blobs(root, staged_paths(root), policy, staged=True)
        elif args.changed:
            blobs = collect_blobs(root, changed_paths(root, args.base), policy, staged=False)
        else:
            tree_entries = [entry for entry in committed_tree(root)
                            if not is_generated(entry[0], policy)]
            blobs = read_object_blobs(root, tree_entries)
        return evaluate(blobs, policy, report_only=args.report_only)
    except GuardError as error:
        print(f"C# size guard: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
