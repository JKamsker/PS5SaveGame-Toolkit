---
title: Create your first plugin
description: Scaffold, understand and test a byte-preserving read-only plugin before researching a real format.
---

# Create your first plugin

Build a working read-only plugin for an intentionally **synthetic** format. By the end, you will have a bounded parser, a paged table and tests that need no private saves. This exercise does not implement support for a real game.

The [plugin guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md) owns the complete rules, including the contribution workflow, integrity requirements, evidence labels, attribution and conformance. Read it before changing a real plugin.

## Prepare your tools

Install Git, PowerShell 7 and the .NET SDK selected by the repository's `global.json` (currently .NET 10). Node.js 24 and npm are needed only if you build the frontend or this documentation site. Clone the repository into a source directory, outside any sample tree:

```powershell
git clone https://github.com/JKamsker/PS5SaveGame-Toolkit.git
cd PS5SaveGame-Toolkit
```

Configure a **separate scratch directory** before builds or tests. Choose a writable path outside the checkout and your original saves:

```powershell
$scratch = Join-Path ([IO.Path]::GetTempPath()) 'save-toolkit-first-plugin'
New-Item -ItemType Directory -Force $scratch, "$scratch/empty-samples" | Out-Null
$env:SAVETOOLKIT_TEST_TEMP = "$scratch/tests"
$env:TEMP = $env:TMP = $env:TMPDIR = $scratch
$env:SAVETOOLKIT_ARTIFACTS = "$scratch/empty-samples"
```

The empty sample directory makes this exercise sample-free. The [scratch guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/RELEASING.md#disk-and-scratch) gives workstation and CI conventions. Never copy private saves into the repository.

## Generate the supported scaffold

Run from the repository root:

```powershell
./scripts/new-plugin.ps1 -Name ExampleGame -Id example-game -TitleIds PPSA00000 -GameName "Example Game"
dotnet build plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame/SaveToolkit.Plugins.ExampleGame.csproj
dotnet test --project plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame.Tests
```

Use a name that does not already exist. `PPSA00000` is a placeholder target, not evidence that a game is supported. The script refuses to overwrite a plugin and the application discovers plugin projects automatically; no solution or API edits are needed.

```text
plugins/ExampleGame/
  README.md
  SaveToolkit.Plugins.ExampleGame/
    SaveToolkit.Plugins.ExampleGame.csproj
    ExampleGamePlugin.cs
    ExampleGameDocument.cs
  SaveToolkit.Plugins.ExampleGame.Tests/
    SaveToolkit.Plugins.ExampleGame.Tests.csproj
    ExampleGameConformanceTests.cs
    ExampleGameDocumentTests.cs
    ExampleGameSampleTests.cs
```

## Understand the contract

<xref:SaveToolkit.Sdk.IGamePlugin> is shared across sessions, so keep the plugin stateless and thread-safe. `Probe` cheaply recognizes bytes; `Open` creates an <xref:SaveToolkit.Sdk.ISaveDocument> with its own model. The framework serializes calls to that document.

The document exposes a <xref:SaveToolkit.Sdk.Views.ViewNode> tree. A <xref:SaveToolkit.Sdk.Values.FieldSpec> describes the field's type and limits; the frontend chooses the control. Plugins perform no filesystem or network I/O and know nothing about HTML or HTTP.

The scaffold's complete plugin is shown below. This excerpt is generated from the current scaffold during the docs build:

[!code-csharp[Generated plugin](../examples/ExampleGamePlugin.cs)]

The one-megabyte file limit and 64-record bound belong to this demonstration. Choose evidence-backed limits for a real format. A title ID alone must never cause a positive match for unrelated bytes.

## Follow the bounded parser and view

The demonstration format contains `STKDEMO1`, a little-endian signed record count and that many little-endian signed 32-bit values. Any remaining bytes are unknown trailing data and must survive unchanged.

<xref:SaveToolkit.Sdk.Binary.BoundedBinaryCursor> validates reads and rejects truncation. `ReadCount` bounds allocation **before** creating the list. The document uses <xref:SaveToolkit.Sdk.Collections.InMemoryCollection`1> to query the complete bounded model and render just one page. <xref:SaveToolkit.Sdk.BoundSaveDocument> registers getter-only bindings, so the fields remain read-only.

[!code-csharp[Generated document](../examples/ExampleGameDocument.cs)]

`Serialize` returns the original relative path and complete original buffer. It preserves the unparsed tail instead of reconstructing only the fields you understand. `Confidence.Verified` applies only to the synthetic fixture's layout; it makes no claim about game semantics. Real uncertain meanings must remain `Inferred` or `Unknown` with an explanation.

Offsets are stable keys only because this read-only demonstration never inserts, removes or reorders records. Real structural formats need native identities that survive those operations.

## Run the tests without private samples

The generated synthetic tests check numeric sorting before paging, search, read-only edit rejection, malformed/truncated inputs and byte-identical serialization including the unknown tail. Run the test command above and read its passed, failed and skipped counts.

The inherited `PluginConformanceTests<T>` checks plugin behavior, including no-op round trips and mutation robustness, when a suitable sample or seed is supplied. The initial scaffold's sample-dependent conformance and sample examples **skip** when the archive is absent. A zero-failure run with skips validates the synthetic exercise, not a real save or gameplay.

Continue with [query a bounded collection](collections.md) for a small, testable change. The full [test contract](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md#tests) explains external samples and conformance expectations.

For shipped SDK preservation contracts, continue with [lossless JSON](lossless-json.md),
[canonical field references](field-references.md), and the [SFO patch substrate](sfo-substrate.md).

## Research before adding a writer

Keep the scaffold read-only while researching the real format. Cite your sources in the plugin README, verify their claims against actual bytes and distinguish known layout from inferred meaning. Replace the synthetic probe, parser, fixtures and support claims together. Document unsupported versions, bounds, integrity algorithms and no-op behavior.

Add an edit only after you can prove all of these for the supported schema:

1. The field's identity, storage width, bounds and gameplay meaning are established to the confidence you publish.
2. Unknown bytes and unsupported records survive unchanged; integrity fields and derived values remain valid.
3. A failed batch leaves bytes and model unchanged. Undo/redo restores the expected values and serialized bytes.
4. Format-specific tests cover no-op round trips, allowed and rejected values, atomic failure, integrity and re-open-after-edit behavior.

Only then add the constrained setter and serialization logic and update `CanEdit`, maturity and evidence together. A setter alone is insufficient: the demonstration serializer intentionally returns the original bytes. Do not mark a plugin `Stable` without in-game validation. Use the [hard rules](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md#hard-rules) and [structural edit contract](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md#structural-edits-add--remove--reorder) for the full requirements.

## Finish the exercise

Review the generated README's required sections yourself. The repository documentation checker reads plugin inventory from `HEAD` (or `--code-ref`), so it does not register or validate an uncommitted demonstration plugin's support claims.

Keep this exercise uncommitted and remove only your throwaway `plugins/ExampleGame` folder when finished. For a real contribution, stay inside your assigned plugin folder and ask the maintainer to update central support/licence tables when outside your scope. Record missing SDK capabilities under `## SDK requests` in your plugin README.
