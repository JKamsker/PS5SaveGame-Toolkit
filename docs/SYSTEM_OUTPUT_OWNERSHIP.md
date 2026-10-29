# Exact system-file output ownership

Plugins normally own only the game file passed to `IGamePlugin.Open`. A plugin that must update
existing metadata in the same save directory can opt in to `IAdditionalOutputOwner`:

```csharp
public sealed class CaptionDocument : ISaveDocument, IAdditionalOutputOwner
{
    public IReadOnlyList<string> AdditionalOutputPaths { get; } =
        ["sce_sys/param.sfo"];

    // Serialize returns the opened file and the full save-set path for param.sfo.
}
```

Declarations are exact paths relative to the opened save directory. The host accepts only an
existing file below `sce_sys/`; wildcards, parent traversal, sibling directories, duplicates, and
missing files are rejected while the document is opened. The declaration is an ownership claim,
not permission to invent a file or to infer game semantics.

`ISaveSet.FindFile` remains game-file-only for compatibility. Use `ISaveSet.FindAnyFile` or
`ISaveDirectory.FindAnyFile` when a plugin or host needs to locate a system file. A serialized
system output must use its full `ISaveFile.RelativePath`, just like any other `FileOutput`.

The framework reserves every declared path for the lifetime of the opened document. A second
document or group cannot claim the same path until the first owner is closed. Before export, the
host rechecks the original SHA-256 fingerprints for the opened game file and all declarations.
If any source changed, the export is rejected without publishing overrides.

The declaration binding is based on the immutable imported bytes. During opening, the host compares
the current file bytes with the imported `DiskSaveFile.Sha256` before accepting the reservation; a
source changed after a plugin read it cannot become the new baseline. Failed group construction and
candidate verification dispose every staged document before returning the original error, leaving a
clean replacement owner usable when one was being replaced.

An opted-in ordinary document must serialize its opened primary file and every declared output
exactly once while dirty. Missing or duplicate paths fail export and verified primary download before
any overrides are published. Clean sessions keep the existing filtered empty-output behavior, and
legacy documents without declarations may continue to serialize additional game files.

For `Everything` and `ChangedDirectories` exports, immutable fingerprints are also checked for clean
ordinary sessions whose directories are included. This validates source integrity without forcing
clean sessions to produce overrides. The primary file is checked against its imported hash before
the open binding is accepted, and source read failures identify the owned relative path through the
export error.

An overridden `sce_sys/param.sfo` is parsed again by `OverlaySaveSet` under a 1 MiB bound. Invalid
or oversized metadata rejects verification, while an untouched directory retains its imported
`ParamSfo` object. This validation is host-side and does not establish console acceptance.

Groups use the same declaration and reservation rules. Their staged output must include every
selected member and every declared output exactly once. Group fingerprints, candidate
verification, rollback and export therefore cover the metadata path in the same transaction.

Backup mirroring applies to declared system files only when the console twin has identical
original bytes and is not independently owned or protected. Loose-save and changed-twin paths
are never mirrored.

This capability does not add a metadata history or automatic caption behavior. A consumer that
changes gameplay and `param.sfo` should return both outputs from one action so existing session
history, undo/redo, review and export remain one atomic unit. The lossless existing-entry SFO
patch helper and a title-specific, user-authored caption action are separate follow-up work.
