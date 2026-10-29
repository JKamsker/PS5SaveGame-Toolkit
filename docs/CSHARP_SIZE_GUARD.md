# C# source-size guard

The repository keeps non-generated C# files readable by warning above 300 lines and rejecting above 500 lines. The guard is deliberately a small, dependency-free Python tool because Python is already required for documentation checks.

Run a complete tracked-source report from the repository root:

```powershell
python -B scripts/check_csharp_size.py --all
```

The report reads the committed `HEAD` tree and blobs, independent of staged additions, deletions or renames. Add `--report-only` when inspecting existing debt without making the command fail. Generated files are excluded only by explicit `generatedGlobs` entries in [.csharp-size-policy.json](../.csharp-size-policy.json). Entries must use a repository-relative generated directory prefix such as `generated/**/*.cs` or `src/Generated/**/*.cs`; the `**` means zero or more directory levels, so both `generated/output.cs` and `generated/sub/output.cs` match. Root-wide or extension-only catchalls are rejected by the parser. The policy cannot raise the global 300-line warning or 500-line hard limit; only stricter positive limits are accepted, and over-500 allowances require exact-path legacy exceptions.

## Pull requests and staged work

CI checks the exact `base...HEAD` range. On a pull request its base is `github.event.pull_request.base.sha`; on a branch push or manual run the workflow resolves `origin/main` after fetching it. This works in detached checkouts and does not assume a local `main` branch. Deleted files are ignored, while rename destinations and paths containing spaces are checked.

The opt-in hook checks the Git index, so it examines exactly what will be committed rather than unrelated working-tree edits. Install it without changing global Git configuration:

```powershell
./scripts/install-hooks.ps1
```

The installer writes only to the repository's active hook directory. Before classifying any target, it compares the raw Git blob ID of the working-tree source with `HEAD:.githooks/pre-commit` and refuses a dirty or mismatching source. It recognizes its own wrapper by that committed blob identity plus an exact SHA-256 match; a content mismatch is treated as an unknown hook even if comments contain familiar text. Before hashing or changing anything, it positively classifies the source, target and backup as regular files (including POSIX FIFO/socket/device rejection). If an unknown pre-commit hook already exists, it prepares and validates a temporary wrapper before moving that hook to a sibling `pre-commit.savetoolkit-original` file, then replaces the active path. Copy failures attempt to restore the original active hook. Re-running the installer with an exact wrapper is a no-op (apart from restoring its executable mode when needed), so it never publishes over the active wrapper. Any existing directory, symlink or other unsupported entry at the target or backup path is refused before mutation. A backup without an active target is also refused because it cannot be tied to a verified prior installation. Review that local backup before removing it. The wrapper chains a preserved original only when it remains executable, so a previously disabled hook stays disabled. The hook requires Python 3; CI remains the authoritative check.

When no `--policy` override is supplied, staged mode reads `.csharp-size-policy.json` from the
index and changed/full modes read it from `HEAD`, matching the source object used for C# content.
An explicit `--policy` path intentionally opts into a filesystem policy override.

Legacy exemption keys retain their exact Git spelling, including a literal backslash in a POSIX
filename; only `/` separates Git path components, so slash and backslash spellings are distinct
keys and literal backslash-delimited `..` text remains part of the filename.

## Legacy files

There are currently no C# files above either limit, so the policy starts with an empty `legacyExemptions` list. If an untouched historical file is already over 500 lines, changed-file CI does not fail merely because it was not part of the PR; the full audit keeps the debt visible. A file may be exempted only by an exact repository-relative path, a limit higher than 500, and a non-empty reason in the policy. The exemption is reported every time and applies to that path only. A PR that changes an exempt file still has to stay within its recorded limit; increasing the limit requires a reviewable policy change.

Warnings are advisory, but they should prompt composition or helper extraction before a file grows further. Keep new C# under 300 lines where practical and never cross 500 without the exact exception above.
