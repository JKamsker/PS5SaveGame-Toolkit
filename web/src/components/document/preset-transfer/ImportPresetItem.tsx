import { memo } from "react";
import type { ImportChoice, ImportItem } from "../../../api/presetTransfer";
import type { PresetCompatibilitySummary } from "../../../api/protocol";

// Changing one conflict choice must not rerender every other record in a full library.
export const ImportPresetItem = memo(function ImportPresetItem({ item, status, choice, busy, reviewed, onChoice }: {
  item: ImportItem; status?: PresetCompatibilitySummary; choice?: ImportChoice; busy: boolean; reviewed: boolean;
  onChoice: (choice: ImportChoice) => void;
}) {
  const reason = !status?.compatible ? status?.reason ?? "Compatibility is unknown."
    : status.readOnly ? "This save is read-only."
    : !status.canPreview ? status.previewReason ?? "The current plugin cannot preview these actions."
    : status.unsupportedActions.length > 0 ? "The current plugin cannot apply these actions." : undefined;
  return <li>
    <strong>{item.resultingName ?? item.name}</strong>
    <p>{item.compatibility.titleIds.join(", ")} · {item.compatibility.gameBuild ?? "build unknown"} · {item.actionCount} actions</p>
    <p className="presets-meta">{reason ? `Stored for later. Cannot apply to this save: ${reason}` : "Matching known boundaries; plugin validation is required before applying."}</p>
    {item.replacesName && <p className="notice notice--Warning">Replaces “{item.replacesName}”.</p>}
    {!reviewed && (item.conflicts.length > 0 || item.repeatedPackageName) && <fieldset disabled={busy}>
      <legend>Resolve duplicate for “{item.name}”</legend>
      {item.repeatedPackageName && <p>Another preset in this package has the same name.</p>}
      <label><input type="radio" name={`import-${item.index}`} checked={choice?.decision === "keepBoth"}
        onChange={() => onChoice({ index: item.index, decision: "keepBoth" })} /> Keep both (use a distinct name)</label>
      {item.conflicts.map((target, index) => <label key={target.id}><input type="radio" name={`import-${item.index}`}
        checked={choice?.replaceId === target.id} onChange={() => onChoice({ index: item.index, decision: "replace", replaceId: target.id })} /> Replace existing “{target.name}” (match {index + 1})</label>)}
    </fieldset>}
    <details><summary>Technical details</summary><p>Plugin {item.compatibility.pluginId} · version {item.compatibility.pluginVersion}</p><p>Variant {item.compatibility.formatVariant ?? "unknown"} · role {item.compatibility.fileRole ?? "unknown"} · capability schema {item.compatibility.capabilitySchemaVersion ?? "legacy"}</p><p>Kind {item.compatibility.presetKind} · schema fingerprint {item.compatibility.schemaFingerprint ?? "unknown"}</p><p>Reference identity {item.compatibility.referencePackIdentity ?? "unknown"}</p>{item.conflicts.map((target, index) => <p key={target.id}>Match {index + 1}: {target.id}</p>)}</details>
  </li>;
});
