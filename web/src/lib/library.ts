import { extractZipEntry, readZipIndex, type ZipEntry } from "./zipIndex";

/** A file found in the chosen folder, with its path relative to that folder. */
export interface ScannedFile {
  path: string;
  file: File;
}

/** What gets uploaded when the user opens a game: the file plus the path the server should see. */
export interface UploadSource {
  file: File;
  path: string;
}

export interface LibraryGame {
  /** Stable within a scan: `folder:PPSA02530`, `zip:PPSA02530_saves_dec.zip`. */
  key: string;
  titleId: string | null;
  kind: "folder" | "zip";
  /** Shown when the title id is unknown to the server catalog. */
  fallbackName: string;
  sources: UploadSource[];
  /** Distinct save directories, console backups (sdimg_sce_bu_*) excluded. */
  saveDirs: number;
  totalBytes: number;
  icon: Blob | null;
}

export interface ScanResult {
  games: LibraryGame[];
  skipped: number;
}

const TITLE_ID = /^[A-Z]{4}\d{5}$/i;
const TITLE_IN_NAME = /([A-Z]{4}\d{5})/i;
const ICON = /(^|\/)sce_sys\/icon0\.png$/i;

const isBackupDir = (segment: string) => segment.toLowerCase().startsWith("sdimg_sce_bu_");
const isSaveDir = (segment: string) => segment.toLowerCase().startsWith("sdimg_");

interface FolderGroup {
  titleId: string;
  files: UploadSource[];
  dirs: Set<string>;
  icon: { file: File; backup: boolean } | null;
}

/** Groups scanned files into games: PPSAxxxxx/sdimg_* folder dumps and save-dump zips. */
export async function groupIntoGames(files: ScannedFile[]): Promise<ScanResult> {
  const folders = new Map<string, FolderGroup>();
  const zips: ScannedFile[] = [];
  let skipped = 0;

  for (const entry of files) {
    if (entry.path.toLowerCase().endsWith(".zip")) {
      zips.push(entry);
      continue;
    }
    const segments = entry.path.split("/");
    const t = segments.findIndex((s, i) => TITLE_ID.test(s) && segments.slice(i + 1, -1).some(isSaveDir));
    if (t < 0) {
      skipped++;
      continue;
    }
    const titleId = segments[t]!.toUpperCase();
    const group = folders.get(titleId) ?? { titleId, files: [], dirs: new Set<string>(), icon: null };
    folders.set(titleId, group);
    const relative = segments.slice(t).join("/");
    group.files.push({ file: entry.file, path: relative });
    const dir = segments.slice(t + 1).find(isSaveDir)!;
    if (!isBackupDir(dir)) group.dirs.add(dir.toLowerCase());
    if (ICON.test(relative) && (!group.icon || (group.icon.backup && !isBackupDir(dir)))) {
      group.icon = { file: entry.file, backup: isBackupDir(dir) };
    }
  }

  const games: LibraryGame[] = [...folders.values()].map((g) => ({
    key: `folder:${g.titleId}`,
    titleId: g.titleId,
    kind: "folder",
    fallbackName: g.titleId,
    sources: g.files,
    saveDirs: g.dirs.size,
    totalBytes: g.files.reduce((n, f) => n + f.file.size, 0),
    icon: g.icon?.file ?? null,
  }));

  for (const zip of zips) {
    const game = await describeZip(zip);
    if (game) games.push(game);
    else skipped++;
  }

  games.sort((a, b) => (a.titleId ?? a.fallbackName).localeCompare(b.titleId ?? b.fallbackName));
  return { games, skipped };
}

async function describeZip(zip: ScannedFile): Promise<LibraryGame | null> {
  let entries: ZipEntry[] | null;
  try {
    entries = await readZipIndex(zip.file);
  } catch {
    entries = null;
  }
  if (!entries) return null;

  const dirs = new Set<string>();
  let titleId = TITLE_IN_NAME.exec(zip.file.name)?.[1]?.toUpperCase() ?? null;
  let icon: ZipEntry | null = null;
  for (const e of entries) {
    const segments = e.name.split("/");
    const d = segments.findIndex(isSaveDir);
    if (d < 0 || d === segments.length - 1) continue;
    titleId ??= segments.slice(0, d).find((s) => TITLE_ID.test(s))?.toUpperCase() ?? null;
    if (!isBackupDir(segments[d]!)) {
      dirs.add(segments[d]!.toLowerCase());
      if (!icon && ICON.test(e.name)) icon = e;
    }
  }
  if (dirs.size === 0) return null;

  let iconBlob: Blob | null = null;
  if (icon) {
    try {
      iconBlob = await extractZipEntry(zip.file, icon);
    } catch {
      iconBlob = null;
    }
  }
  return {
    key: `zip:${zip.path}`,
    titleId,
    kind: "zip",
    fallbackName: zip.file.name.replace(/\.zip$/i, ""),
    sources: [{ file: zip.file, path: zip.file.name }],
    saveDirs: dirs.size,
    totalBytes: zip.file.size,
    icon: iconBlob,
  };
}

/** Recursively lists a directory handle (File System Access API), bounded to avoid runaway scans. */
export async function listDirectory(handle: FileSystemDirectoryHandle, maxFiles = 100_000, maxDepth = 8): Promise<ScannedFile[]> {
  const out: ScannedFile[] = [];
  const walk = async (dir: FileSystemDirectoryHandle, prefix: string, depth: number): Promise<void> => {
    for await (const child of dir.values()) {
      if (out.length >= maxFiles) return;
      if (child.kind === "file") out.push({ path: prefix + child.name, file: await (child as FileSystemFileHandle).getFile() });
      else if (depth < maxDepth && !child.name.startsWith(".")) await walk(child as FileSystemDirectoryHandle, `${prefix}${child.name}/`, depth + 1);
    }
  };
  await walk(handle, "", 0);
  return out;
}
