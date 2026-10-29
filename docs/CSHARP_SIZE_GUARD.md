# C# source-size guard

The repository keeps non-generated C# files readable by warning above 300 lines and rejecting above 500 lines. The guard is deliberately a small, dependency-free Python tool because Python is already required for documentation checks.

Run a complete tracked-source report from the repository root:

```powershell
python -B scripts/check_csharp_size.py --all
```

The report reads committed `HEAD` blobs and prints every warning, approved legacy exception and hard-limit error. Add `--report-only` when inspecting existing debt without making the command fail. Generated files are excluded only by the explicit `generatedGlobs` entries in [.csharp-size-policy.json](../.csharp-size-policy.json); do not add a broad catch-all to hide source.

## Pull requests and staged work

CI checks the exact `base...HEAD` range. On a pull request its base is `github.event.pull_request.base.sha`; on a branch push or manual run the workflow resolves `origin/main` after fetching it. This works in detached checkouts and does not assume a local `main` branch. Deleted files are ignored, while rename destinations and paths containing spaces are checked.

The opt-in hook checks the Git index, so it examines exactly what will be committed rather than unrelated working-tree edits. Install it without changing global Git configuration:

```powershell
./scripts/install-hooks.ps1
```

The installer writes only to the repository's active hook directory. If a pre-commit hook already exists, it moves that hook to a sibling `pre-commit.savetoolkit-original` file and runs it after the size guard. Review that local backup before removing it. The hook requires Python 3; CI remains the authoritative check.

## Legacy files

There are currently no C# files above either limit, so the policy starts with an empty `legacyExemptions` list. If an untouched historical file is already over 500 lines, changed-file CI does not fail merely because it was not part of the PR; the full audit keeps the debt visible. A file may be exempted only by an exact repository-relative path, a limit higher than 500, and a non-empty reason in the policy. The exemption is reported every time and applies to that path only. A PR that changes an exempt file still has to stay within its recorded limit; increasing the limit requires a reviewable policy change.

Warnings are advisory, but they should prompt composition or helper extraction before a file grows further. Keep new C# under 300 lines where practical and never cross 500 without the exact exception above.
