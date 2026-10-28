# Restored community identifiers, schema declarations and enum facts

Author/project: kvasszn / REE Save Editor; praydog (RSZ dump tooling).
Pinned revision: `4125e497f1ae84cc746bb4a99da612c48bea161e`.
Retrieved: 2026-10-28. Licence for each dataset:
**No licence published; reused with attribution per owner decision 2026-10-28**.
No licence file is published at this revision; the pack's required `licenseUrl`
points to the pinned input rather than claiming a licence document exists.

| Input | SHA-256 (downloaded bytes) | Derived dataset / transformation |
| --- | --- | --- |
| [strings_pragmata.txt](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/strings_pragmata.txt) | `ce4616cd876ac4b02a5fc541782c49d00a5f55b8b46eac8956aa46d5f709665d` | `pragmata-restored-identifiers.json`: select exactly five hash-matching technical identifiers: `Slot`, `Detail`, `_IsFirstGetWeapon`, `_IsFirstGetActiveSkill`, `_AcquireContentsHashes`. No dialogue, localized strings or other game text. |
| [rszpragmata.json](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/rszpragmata.json) | `0857b7c165e10e9ceeef65eab80ef84157262bcc03f3da8f50108a3b95d4dad8` | `pragmata-field-schema.json`: 459 declarations whose class and field spellings occur in the existing observed-hash allowlist. Retain original type, storage type, array flag, declared size/alignment and exact class/field hash keys. RSZ layout facts are metadata, not save-parser offsets or rules. |
| [enumspragmata.json](https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/enumspragmata.json) | `c6a828b65ca065cd05003395e6d1b7a8d08872190dacca904899a615f9527c3b` | `pragmata-enum-members.json`: 230 numeric values across eight types referenced by those declarations. Collapse the reverse lookup half and preserve all name aliases per value. No unrelated enums or guessed item-ID names. |

The restored resource is also embedded as `Resources/RestoredHashNames.json`;
the original 408 MIT identifiers remain separately attributed. Runtime resource
loading verifies Murmur3/UTF-8/seed `0xffffffff` for every name. Sample tests find
all five restored hashes in the supplied 12 files and verify no-op byte identity.

Schema records are **Inferred** declarations from a community PC dump. A known
class/field spelling does not mean every declared field is present in PS5 saves.
Tests cross-index actual class/field pairs and enum values across all samples;
they do not claim the entire PC schema is a verified PS5 layout. Enum labels
require the exact declaring class, field, integer storage type and numeric value.
Array elements inherit their field declaration; their indices are not enum IDs.
For a four-byte enum declared U32, signed-reader negatives are normalized to the
same uint32 bits for lookup only; saved values and editing specs are unchanged.
Unknown values and undeclared flag combinations retain raw controls. These facts
never establish legal edit caps, progression, ownership, completion or unlock rules.

The independent generator reads only these hash-pinned inputs, the existing MIT
schema/mission inputs and committed metadata; it never executes downloaded code
or reads saves. Attribution appears in the pack/Sources, README, notices,
`Info.References`, restored-name provenance and primitive/raw field descriptions.
