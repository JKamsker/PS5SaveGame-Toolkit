---
title: Open your first save
description: Open the in-browser editor and inspect a decrypted save without changing the original.
---

# Open your first save

You need a current desktop browser and an **already decrypted** PS5 save dump. Keep the original dump in a separate backup folder. Use your save manager to decrypt and later restore saves; the toolkit does neither.

The editor runs entirely in your browser: the game plugins are compiled to WebAssembly and your saves are never uploaded anywhere. There is no server to install.

## 1. Open the editor

Use the hosted editor under `app/` on this documentation site, or run a release copy locally:

1. Download `SaveToolkit-VERSION-web.zip` and its `.sha256` file from [GitHub Releases](https://github.com/JKamsker/PS5SaveGame-Toolkit/releases) (the release asset, not the automatic source archive). On Linux, run `sha256sum --check <archive>.sha256`; on macOS, `shasum -a 256 --check <archive>.sha256`; on Windows, follow the [user guide's checksum steps](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/USER_GUIDE.md#download-and-verify).
2. Extract the **whole** archive and open a terminal in its `SaveToolkit-<version>-web` folder.
3. Serve that folder with any static web server, for example `python3 -m http.server 8080 --bind 127.0.0.1` (Windows: `py -m http.server 8080 --bind 127.0.0.1`), and open **http://127.0.0.1:8080/**. Opening `index.html` directly from the file manager does not work.

The first load downloads the editor runtime (about 22 MB) and shows its progress; later loads use the browser cache.

## 3. Import a copy

Drop a decrypted zip or folder into the local interface, branded **Savebench**. Choose the detected document and inspect its title, plugin, supported version and warnings. A raw fallback view means the toolkit can inspect bytes without a matching game parser; it does not establish edit support.

Start by browsing fields and searching a table. Leave values unchanged for this first inspection. A displayed name may come from reference data and does not prove that the save owns or has unlocked the item.

## 4. Decide your next step

Read [working with saves](../guides/working-with-saves.md) before editing or exporting. All game plugins are experimental and edits are not console-validated. Work on a spare slot and retain the untouched original even after an export succeeds.

Export anything you want to keep before closing or reloading the tab. Workspaces live only in that tab's memory. Presets and imported reference packs are kept in the browser's storage for this site.

If the editor does not load or the import is unrecognized, use [troubleshooting](../guides/troubleshooting.md). For storage, local serving and complete supported-title limits, follow the [authoritative user guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/USER_GUIDE.md).
