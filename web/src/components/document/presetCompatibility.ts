import type { SaveDocument, ToolkitPreset } from "../../api/protocol";

/** Advisory only: the plugin-authoritative server check always runs before applying. */
export function compatibilityReason(expected: ToolkitPreset["compatibility"], document: SaveDocument): string | undefined {
  if (expected.pluginId !== document.plugin.id || expected.pluginVersion !== document.plugin.version) return "Different plugin version.";
  if (document.titleId && !expected.titleIds.some(title => title.toLowerCase() === document.titleId?.toLowerCase())) return "Different title.";
  const capability = document.presetCapability;
  if (!capability) return expected.gameBuild || expected.formatVariant || expected.referencePackIdentity || expected.schemaFingerprint || expected.fileRole || expected.capabilitySchemaVersion
    ? "The current document cannot verify this preset's semantic boundaries." : expected.presetKind && expected.presetKind !== "toolkit.action-parameters" ? "Different preset kind." : undefined;
  if (expected.presetKind !== capability.presetKind) return "Different preset kind.";
  const boundaries: [string, string | undefined, string | undefined][] = [
    ["build", expected.gameBuild, capability.gameBuild], ["variant", expected.formatVariant, capability.formatVariant],
    ["reference pack", expected.referencePackIdentity, capability.referencePackIdentity],
    ["schema", expected.schemaFingerprint, capability.schemaFingerprint], ["file role", expected.fileRole, capability.fileRole],
    ["capability schema", expected.capabilitySchemaVersion, capability.schemaVersion],
  ];
  for (const [label, saved, current] of boundaries) {
    if (current && saved !== current) return saved ? `Different ${label}.` : `Preset is missing the required ${label} boundary.`;
    if (!current && saved) return `Current ${label} is unknown.`;
  }
  return undefined;
}
