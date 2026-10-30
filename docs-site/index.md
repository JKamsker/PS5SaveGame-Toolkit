---
title: Documentation
description: Inspect a decrypted save, understand the plugin model, and build against the toolkit's generated APIs.
_layout: landing
---

<div class="docs-hero">
<p class="eyebrow">PS5 SAVE GAME TOOLKIT</p>

# Understand your saves.<br>Build with confidence.

<p class="lead">A local toolkit for decrypted PS5 saves. Start with a safe inspection workflow, learn how plugins describe game data, and explore the APIs generated from the code.</p>

<p class="hero-actions"><a class="docs-button" href="getting-started/quickstart.md">Get started</a><a class="docs-secondary" href="tutorials/first-plugin.md">Create your first plugin →</a></p>
</div>

<div class="docs-paths">
<a class="docs-path" href="getting-started/quickstart.md"><span class="eyebrow">01 · START HERE</span><strong>Open your first save</strong><span>Download, verify, launch locally, and inspect a decrypted dump.</span></a>
<a class="docs-path" href="guides/working-with-saves.md"><span class="eyebrow">02 · USER GUIDE</span><strong>Make a careful change</strong><span>Read confidence labels, review changes, undo, and export.</span></a>
<a class="docs-path" href="tutorials/first-plugin.md"><span class="eyebrow">03 · PLUGIN DEVELOPMENT</span><strong>Teach the toolkit a format</strong><span>Build and test a bounded read-only plugin using the supported scaffold.</span></a>
<a class="docs-path" href="reference/index.md"><span class="eyebrow">04 · GENERATED REFERENCE</span><strong>Find the exact contract</strong><span>Browse C# SDK types and the local HTTP API, rebuilt with the source.</span></a>
</div>

## Edit and review your save

- [Save, reuse and transfer presets](guides/presets-and-transfer.md) through separate library import
  and revision-bound save application.
- [Review changes](guides/metadata-review.md) with canonical fields, aliases, member context and
  explicit coverage limits.
- [Change a Tsushima caption](guides/manual-caption.md) within its UTF-8 capacity and export the
  exact companion metadata.
- [Preview actions and effects](guides/action-effects.md), including current scopes, no-op and
  stale recovery, and the limits of synthetic effect evidence.

## Current SDK tutorials

After the scaffold and collection walkthrough, continue through the shipped preservation and
presentation contracts:

- [Preserve JSON scalar spelling](tutorials/lossless-json.md) keeps BOM, trivia, duplicate keys
  and exact numbers while bounding scalar patches and snapshots.
- [Project canonical fields](tutorials/field-references.md) reuses one binding across projections
  with canonical spec, confidence, warning and description metadata.
- [Patch an existing SFO value](tutorials/sfo-substrate.md) plans a strict UTF-8 in-place update
  while retaining every other byte.

GenericJson and XorJson use the lossless JSON substrate, eligible UnrealGvas projections use
canonical field references, and the manual Tsushima caption uses the SFO substrate. The tutorials
link those consumers and tests. The central [SDK request ledger](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/SDK_REQUESTS.md)
tracks remaining adoption and evidence separately; automatic gameplay-to-caption synchronization
and production Gearbox action effects are not delivered by these journeys.

## Before you begin

The toolkit works with **already decrypted** saves. It does not decrypt or re-sign console containers. The editor runs on your computer at `http://127.0.0.1:5180`; this website contains documentation only.

Keep an untouched backup and try edits on a disposable spare slot. Game plugins are experimental: a successful export does not establish that the console or game will accept the change.

## How the pieces fit

Your browser renders the plugin's abstract view. The local server owns imports, validation, history and export. A plugin recognizes bytes, opens an in-memory document, and describes fields and tables through the SDK.

Read the [plugin model](tutorials/first-plugin.md#understand-the-contract), browse <xref:SaveToolkit.Sdk.IGamePlugin>, or inspect the [HTTP reference](reference/http.md).

## Common questions

### Do I need .NET or Node.js to use a release?

No. The [release bundles](https://github.com/JKamsker/PS5SaveGame-Toolkit/releases) include the application runtime and built interface. Developers need the SDK selected by `global.json`; the [plugin tutorial](tutorials/first-plugin.md) explains the tooling.

### Does a recognized file mean every edit is supported?

No. Recognition, byte layout and gameplay meaning are separate claims. Review the plugin's title/version/write limits in the [support table](https://github.com/JKamsker/PS5SaveGame-Toolkit#supported-games-and-formats) and its linked README.

### Where are the complete contributor rules?

The repository's [plugin guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/PLUGIN_GUIDE.md) is authoritative. These tutorials are a guided route through those contracts; the SDK and HTTP references are generated automatically.
