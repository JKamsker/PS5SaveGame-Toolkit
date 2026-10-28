// Mirror of src/SaveToolkit.Sdk (views/values) and src/SaveToolkit.Api/Contracts.
// Keep in sync with the C# records; JSON is camelCase, enums are strings, nulls are omitted.

/** Exact lexemes and union choices are tagged; existing scalar wire representations are unchanged. */
export interface ExactNumberValue { kind: "exactNumber"; text: string }
export interface UnionValue { kind: "union"; type: string; value: FieldValue }
export type FieldValue = null | boolean | number | string | ExactNumberValue | UnionValue;

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
export interface ExactNumberSpec extends SpecBase {
  kind: "exactNumber";
  maxLength?: number;
  maxAbsoluteExponent?: number;
  min?: string;
  max?: string;
}
export interface NullableSpec extends SpecBase { kind: "nullable"; inner: FieldSpec }
export interface UnionOption { id: string; label: string; spec: FieldSpec }
export interface UnionSpec extends SpecBase { kind: "union"; options: UnionOption[] }

export type FieldSpec = BoolSpec | IntegerSpec | FloatSpec | TextSpec | EnumSpec | FlagsSpec | DateTimeSpec | ExactNumberSpec | NullableSpec | UnionSpec;

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

export type ViewNode = SectionNode | TabsNode | FieldNode | TableNode | TextNode | StatsNode | ImageNode | ActionNode | HexNode;

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

export interface DocumentState { canUndo: boolean; canRedo: boolean; isDirty: boolean }

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
  state: DocumentState;
}

export interface TitleInfo { titleId: string; name: string; support: "None" | "View" | "Edit"; pluginIds: string[] }

export type ExportScope = "ChangedFiles" | "ChangedDirectories" | "Everything";

export interface ExportPlan { files: { relativePath: string; length: number; reason: string }[]; changedCount: number }
