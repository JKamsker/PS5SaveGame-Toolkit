// Mirror of src/SaveToolkit.Sdk (views/values) and src/SaveToolkit.Api/Contracts.
// Keep in sync with the C# records; JSON is camelCase, enums are strings, nulls are omitted.

/** Integers travel as decimal strings (u64-safe); floats as numbers; enums/datetimes as strings. */
export type FieldValue = null | boolean | number | string;

interface SpecBase {
  readOnly?: boolean;
  unit?: string;
}

export interface BoolSpec extends SpecBase { kind: "bool" }
export interface IntegerSpec extends SpecBase {
  kind: "integer";
  min: string;
  max: string;
  format?: "Decimal" | "Hex" | "Duration";
  unitsPerSecond?: number;
}
export interface FloatSpec extends SpecBase { kind: "float"; min?: number; max?: number; singlePrecision?: boolean }
export interface TextSpec extends SpecBase { kind: "text"; maxLength?: number; multiline?: boolean }
export interface EnumOption { value: string; label: string; description?: string }
export interface EnumSpec extends SpecBase { kind: "enum"; options: EnumOption[]; allowCustom?: boolean }
export interface FlagBit { bit: number; label: string; description?: string }
export interface FlagsSpec extends SpecBase { kind: "flags"; bits: FlagBit[]; bitWidth: number }
export interface DateTimeSpec extends SpecBase { kind: "datetime"; hasTimeZone?: boolean }

export type FieldSpec = BoolSpec | IntegerSpec | FloatSpec | TextSpec | EnumSpec | FlagsSpec | DateTimeSpec;

export type Confidence = "Verified" | "Inferred" | "Unknown";
export type Tone = "Normal" | "Muted" | "Info" | "Success" | "Warning" | "Danger";

export interface SectionNode {
  type: "section";
  title: string;
  children: ViewNode[];
  description?: string;
  badge?: string;
  collapsed?: boolean;
  lazyId?: string;
}
export interface TabsNode { type: "tabs"; tabs: { title: string; children: ViewNode[] }[] }
export interface FieldNode {
  type: "field";
  valueId: string;
  label: string;
  spec: FieldSpec;
  value: FieldValue;
  description?: string;
  confidence?: Confidence;
  warning?: string;
}
export interface TableColumn { key: string; label: string; description?: string; width?: "Narrow" | "Normal" | "Wide" }
export interface TableRow { key: string; cells: Record<string, ViewNode> }
export interface TableNode { type: "table"; columns: TableColumn[]; rows: TableRow[]; title?: string; description?: string }
export interface TextNode { type: "text"; text: string; tone?: Tone; monospace?: boolean }
export interface StatsNode { type: "stats"; items: { label: string; value: string; hint?: string }[] }
export interface ImageNode { type: "image"; assetId: string; alt: string }
export interface ActionNode {
  type: "action";
  actionId: string;
  label: string;
  description?: string;
  tone?: Tone;
  confirm?: string;
  parameters?: FieldNode[];
}
export interface HexNode { type: "hex"; blobId: string; length: number; title?: string }
export interface QueryableColumn {
  key: string;
  label: string;
  spec?: FieldSpec;
  description?: string;
  width?: "Narrow" | "Normal" | "Wide";
}
export interface QueryableTableNode {
  type: "queryableTable";
  collectionId: string;
  title: string;
  columns: QueryableColumn[];
  totalCount: number;
  description?: string;
}

export type ViewNode = SectionNode | TabsNode | FieldNode | TableNode | TextNode | StatsNode | ImageNode | ActionNode | HexNode | QueryableTableNode;

// ---- API contracts ----

export type Maturity = "ReadOnly" | "Experimental" | "Stable";

export interface GameTitle { titleId: string; name: string; region?: string }

export interface Plugin {
  id: string;
  name: string;
  version: string;
  description?: string;
  maturity: Maturity;
  games: GameTitle[];
  references: string[];
}

export interface PluginMatch { pluginId: string; pluginName: string; score: number; label?: string; maturity: Maturity }

export interface SaveFileInfo { id: string; path: string; length: number; matches: PluginMatch[]; openDocumentId?: string }

export interface SaveDirectory {
  id: string;
  name: string;
  rawName: string;
  isBackup: boolean;
  generation: number;
  twinId?: string;
  twinIdentical: boolean;
  hasIcon: boolean;
  sfo?: { mainTitle?: string; subTitle?: string; detail?: string; saveDataDirectory?: string };
  files: SaveFileInfo[];
}

export interface TitleGroup { titleId: string; gameName?: string; directories: SaveDirectory[] }

export interface DocumentSummary { id: string; fileId: string; filePath: string; title: string; pluginId: string; isDirty: boolean }

export interface Workspace { id: string; name: string; createdAt: string; titles: TitleGroup[]; documents: DocumentSummary[] }

export interface WorkspaceSummary { id: string; name: string; createdAt: string; directoryCount: number; fileCount: number; titleIds: string[] }

/** `revision` increases on every structural change; send it back with edits and actions. */
export interface DocumentState { canUndo: boolean; canRedo: boolean; isDirty: boolean; revision?: number }

export interface SaveDocument {
  id: string;
  fileId: string;
  filePath: string;
  title: string;
  plugin: Plugin;
  canEdit: boolean;
  state: DocumentState;
  view: ViewNode;
}

export interface ValueChange { valueId: string; oldValue: FieldValue; newValue: FieldValue }
export interface FieldError { valueId: string; message: string }

export interface EditResult {
  ok: boolean;
  errors: FieldError[];
  changes: ValueChange[];
  warnings: string[];
  message?: string;
  structureChanged: boolean;
  /** The request used a view that a structural change replaced; refetch the document and retry. */
  stale?: boolean;
  state: DocumentState;
}

export interface CollectionQuery {
  q?: string;
  filters?: Record<string, string>;
  sort?: string;
  descending?: boolean;
  offset?: number;
  limit?: number;
  cursor?: string;
}

export interface QueryablePage {
  collectionId: string;
  columns: QueryableColumn[];
  rows: TableRow[];
  totalCount: number;
  filteredCount: number;
  offset: number;
  limit: number;
  nextCursor?: string;
  revision: number;
}

export interface TitleInfo { titleId: string; name: string; support: "None" | "View" | "Edit"; pluginIds: string[] }

export type ExportScope = "ChangedFiles" | "ChangedDirectories" | "Everything";

export interface ExportPlan { files: { relativePath: string; length: number; reason: string }[]; changedCount: number }

// ---- Reference packs (src/SaveToolkit.Sdk/References, src/SaveToolkit.Api/Contracts/ReferenceDtos.cs) ----

export type ReferencePackOrigin = "Embedded" | "User";
export type ReferenceDatasetKind = "Records" | "Assets";

/** Who made a dataset and under which terms; shown wherever its data appears. */
export interface ReferenceAttribution {
  sourceUrl: string;
  author: string;
  license: string;
  licenseUrl: string;
  /** ISO date (yyyy-MM-dd). */
  retrieved: string;
  modifications: string;
}

export interface ReferenceDatasetInfo {
  packId: string;
  id: string;
  kind: ReferenceDatasetKind;
  attribution: ReferenceAttribution;
  title?: string;
  itemCount: number;
}

export interface ReferencePackInfo {
  id: string;
  version: string;
  title: string;
  origin: ReferencePackOrigin;
  description?: string;
  pluginIds: string[];
  titleIds: string[];
  gameBuilds: string[];
  locale?: string;
  datasets: ReferenceDatasetInfo[];
}

export interface ReferencePacks { packs: ReferencePackInfo[]; errors: { origin: string; message: string }[] }

/** One dataset pinned to an open document; `used` once the plugin resolved it. */
export interface DocumentSource {
  packId: string;
  packTitle: string;
  packVersion: string;
  origin: ReferencePackOrigin;
  locale?: string;
  datasetId: string;
  kind: ReferenceDatasetKind;
  title?: string;
  itemCount: number;
  attribution: ReferenceAttribution;
  used: boolean;
}

export interface DocumentSources { datasets: DocumentSource[] }

export interface ReferenceRecord {
  key: string;
  name: string;
  category?: string;
  limit?: number;
  description?: string;
  assetId?: string;
  source?: string;
  attributes?: Record<string, string>;
  packId: string;
  datasetId: string;
}

export interface ReferencePage {
  catalogId: string;
  packId: string;
  items: ReferenceRecord[];
  total: number;
  offset: number;
  limit: number;
  categories: string[];
}
