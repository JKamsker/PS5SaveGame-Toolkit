---
title: Open your first save
description: Run the toolkit locally and inspect a decrypted save without changing the original.
---

# Open your first save

You need a supported computer, a release bundle and an **already decrypted** PS5 save dump. Keep the original dump in a separate backup folder. Use your save manager to decrypt and later restore saves; the toolkit does neither.

## 1. Download and verify

Choose the bundle matching your system from [GitHub Releases](https://github.com/JKamsker/PS5SaveGame-Toolkit/releases): `win-x64`, `linux-x64`, `osx-arm64` or `osx-x64`. Download its matching `.sha256` file too. Use the release asset, rather than the automatic source archive.

On Windows, open PowerShell beside the downloads and replace `VERSION`:

```powershell
$archive = 'SaveToolkit-VERSION-win-x64.zip'
$expected = ((Get-Content -LiteralPath "$archive.sha256" -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($actual -ine $expected) { throw 'Checksum mismatch. Download the archive again.' }
```

On Linux, run `sha256sum --check <archive>.sha256`. On macOS, run `shasum -a 256 --check <archive>.sha256`, substituting your actual archive filename. The [complete user guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/USER_GUIDE.md#download-and-verify) covers the combined checksum manifest and platform details.

## 2. Extract and launch

Extract the **whole** archive into a writable folder. Open its inner `SaveToolkit-<version>-<runtime>` folder. Keep the executable, configuration and `wwwroot` together.

- **Windows:** double-click `start.cmd`, or run `.\start.cmd` from PowerShell.
- **macOS / Linux:** run `chmod +x start.sh SaveToolkit.Api`, then `./start.sh`.

Leave the server terminal running and open **http://127.0.0.1:5180**. No .NET or Node.js installation is needed for a release. Keep the server bound to loopback: it is a local, single-user application.

## 3. Import a copy

Drop a decrypted zip or folder into the local interface, branded **Savebench**. Choose the detected document and inspect its title, plugin, supported version and warnings. A raw fallback view means the toolkit can inspect bytes without a matching game parser; it does not establish edit support.

Start by browsing fields and searching a table. Leave values unchanged for this first inspection. A displayed name may come from reference data and does not prove that the save owns or has unlocked the item.

## 4. Decide your next step

Read [working with saves](../guides/working-with-saves.md) before editing or exporting. All game plugins are experimental and edits are not console-validated. Work on a spare slot and retain the untouched original even after an export succeeds.

Export anything you want to keep before stopping the server. Workspaces are transient and do not survive a restart. Press **Ctrl+C** in the server terminal when finished.

If the browser cannot connect or the import is unrecognized, use [troubleshooting](../guides/troubleshooting.md). For storage, ports and complete supported-title limits, follow the [authoritative user guide](https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/USER_GUIDE.md).
