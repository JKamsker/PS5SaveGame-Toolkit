# Enabled scalar controls (0.6.0)

All controls are **Inferred / Experimental**, not console-validated. Integrity
coverage is unknown; **the game may reject the edited save**. See the README's
evidence table and [#31](https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31).

These are names observed in the supplied PPSA01474 corpus, not a claim about other
titles/builds. Fields are located by parsed descriptors, never absolute offsets.
Existing scalar boolean preferences are recognized by their actual wire type;
unknown numeric/string/object fields remain read-only. In this corpus there are
70 distinct writable controls (nine gameplay and 61 preference controls).
The F6 `bolts/set-target` action now previews and changes the same CareerBolts
atom to achieve an inferred career-minus-spent target. It preserves SpentBolts,
reuses conservative width/corpus bounds and adds no storage writer. It requires
preview/apply, rejects row/query scope and reports the integrity warning.

## Gameplay

`player/CareerBolts` and `player/SpentBolts` retain actual unsigned width and
conservative observed maxima. Both slots must have supported unsigned scalar
encodings before either becomes writable. The final calculated balance must be nonnegative,
except when restoring the exact original imported counter pair for lossless undo.
These seven `player/` boolean fields change neither collectibles nor unlocks:

```text
InfiniteHealthEnabled
InfiniteAmmoEnabled
GoldWeaponsEnabled
MirrorModeEnabled
HomeRunHitterEnabled
RPGEnabled
ConfettiHeadshotEnabled
```

## Preferences

Each name is prefixed with `preferences/`. Both supplied primary preference
records contain these same 61 boolean identities (descriptor order differs):

```text
AdaptiveTriggersEnabled
AimAssistEnabled
AimModeToggle
AutoFireToggle
AutoParkourMode
BigObjectivePOIsEnabled
CameraInvertX
CameraInvertY
CameraShakeEnabled
CasualModeEnabled
CenterpointDot
ChromaticAberrationEnabled
ClampToOffscreenLedgesOutOfCombat
ClassicControlsEnabled
ControllerSpeakerEnabled
DPadMovementEnabled
DepthOfFieldEnabled
DisableTutorials
EdgeBlur
FlightAssistEnabled
FlyerInvertX
FlyerInvertY
GadgetToggle
GlideFallToggle
GoreEnabled
GrindAssistEnabled
HDREnabled
HFRBoostEnabled
HighContrastBackgroundEnabled
HoverBootAutoKick
HoverBootToggle
HudParallaxEnabled
LevelUpSlow
LockOnEnabled
LockStrafeEnabled
LookAtWaypoint
MeleeToggle
MidnightMode
MonoAudio
OutlinesEnabled
PhaseDashFXEnabled
ProfanityEnabled
Pulse3DEnabled
RumbleEnabled
ScreenFX
ShaderOutline
ShowCompleteTime
ShowTimePlayed
ShowWeaponLevelUps
SimplifiedControlsEnabled
SkipCutsceneShortcut
SpeetleBoostToggle
StrafeAimEnabled
SubtitlesEnabled
TextSubtitlesEnabled
TraversalPrompts
UseEnglishAudio
VoiceChatEnabled
WallJumpAssistEnabled
WaypointsAssistToggle
WeaponWheelPause
```

Storage-name/type facts were independently checked against the private samples;
the schema reference is Neptuwunium's [Rivet DDL schema, a631c96e](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json),
CC0-1.0 per its [file-specific license sidecar](https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json.license)
([license text](https://creativecommons.org/publicdomain/zero/1.0/)). Retrieved and
verified 2026-10-28; toolkit contributors selected factual names and wrote original
policy notes, retaining no runtime offsets or preference values. This factual
name list is CC0-1.0. No upstream implementation or
runtime offsets were copied. Menu meanings and console behavior remain inferred;
this list does not import game descriptions, art or user preference values.
