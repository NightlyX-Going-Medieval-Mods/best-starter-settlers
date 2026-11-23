# In-game mod: API notes

Findings from decompiling `Assembly-CSharp.dll` (game build of 2026-11-17) on 2026-11-23.
Only names and behaviour are recorded here; no decompiled code lives in this repo.

Loader: BepInEx 5 (Unity 2022.3 Mono), Harmony patches. The game's own **Mods menu only loads data mods**,
so the plugin ships as `BepInEx/plugins/BestStarter/BestStarter.dll`.

## Reading game data live (replaces the CSVs)

All are `Repository<TRepo, TItem>.Instance` singletons (namespace `NSMedieval.Repository`, base `NSEipix.Repository`):

| Data | Repository | Notes |
|---|---|---|
| Perks | `PerkRepository` (`Worker/Perk.json`) | Editor offers `GetAvailableOnStartPerks()` = all perks without `ForbidOnStart` (none today) |
| Old backgrounds | `BackgroundRepository` | `GetAvailableBackgrounds(ignoreTypes)` |
| Recent backgrounds | `BackStoryRepository` | Editor lists `GetAllItems()`, **unfiltered** (none have body-type limits today) |
| Pseudonyms | `PseudonymRepository` | `GetAvailablePseudonyms(ignoreTypes)` |
| Interests | `GoalPreferenceRepository`, `GoalPreferenceLevelRepository` | |
| Budget | `GenerationSettingsRepository...Settings.MaxCreationPoints` | 90 |
| Animals | `AnimalBaseRepository` | `canHaulAsPet`, `minTameSkill`, `minTrainSkill` |
| Roles / skill curve | `RoleRepository`, `SkillLevelsRepository` | skill curve still to inspect |

Names: `UiUtils.Localize.GetText(LocKeyUtils.GetName(item.LocKeys), bodyType)` (player's language).

## Editor controller: `NSMedieval.Controllers.CharacterEditController`

`MonoSingleton<CharacterEditController>.Instance`. Every setter acts on **`SelectedHumanoid`** (`Workers[Selected]`).

- `Workers`, `Selected`, `SetSelected(i)`, `EditModeEnabled`, `GetGroupPoints()` -> `IntRange(used, max)`
- `SetBackground(id)`, `SetBackstory(id)` (recent), `SetPseudonym(id | "none")`
- `AddPerk(id)`, `RemovePerk(id)`, `SetFreshPerks(list)`
- `SetSkillLevel(WorkerSkill, level)`: absolute level, clamped up to the trait-given base
  (`HumanoidUtils.GetBaseSkillPoints`).
- `ApplyPreset(WorkerInstancePreset)` is the game's own "load settler" flow: traits first, skills last, with
  `TaskController.WaitFor` delays. Copy that order.

### Pitfalls when applying a whole team
1. **Order:** set backgrounds, pseudonym and perks first, then skill levels. Swapping a trait subtracts its old
   modifiers from the current levels (`OnSkillModifierRemoved`), which would wreck levels set earlier.
2. **Points update is deferred:** `UpdateGroupPoints` runs 0.1 s later on whoever is `SelectedHumanoid` *then*.
   Wait (~0.3 s, via `MonoSingleton<TaskController>.Instance.WaitFor`) after each settler before `SetSelected(next)`.
3. Interests are recomputed by the setters (`RecalcuateGoalPreferencesAndSkillModifiers`, game's spelling).

## Rules confirmed again (match core.js)
- Perk conflicts in the editor are **one-directional** (a chosen perk greys out its own `ConflictsWith`);
  core.js treats them symmetrically, which is stricter and therefore always valid.
- Body-type limits: `HumanoidUtils.GetPhysicalIgnoreTypes` ignores every *other* body type, so
  `ignoreCharacteristicType` = "only for". Applied to old backgrounds and pseudonyms, **not** perks.
- Max 10 perks (`CharacterStatsEdit.maxPerks`, add button hidden at 10).

## UI hook: `NSMedieval.UI.CharacterStatsEdit` (the stats panel)
- Patch `Start` (postfix). Private `[SerializeField] SoundButton` fields to clone, the same trick Hearthtalk uses:
  `reRollWorkerButton`, `saveWorkerButton`, `loadWorkerButton`, `skillsRandomizeButton`, `perksRandomizeButton`.
  Read with `Traverse.Create(__instance).Field("saveWorkerButton")`, `Instantiate` next to it, strip
  `I2.Loc.Localize`, set the `TMP_Text`.
- Events to refresh our UI: `SelectedWorkerChangedAction`, `PerksChangedAction`, `SkillChangedAction`,
  `CreationPointsUpdatedAction`, `EditModeEnabledAction` on the controller.

## Runtime gotchas (found in-game)
- **The BepInEx plugin GameObject gets destroyed** after start-up: `Plugin.StartCoroutine` throws a
  `NullReferenceException` in `MonoBehaviour.IsObjectMonoBehaviour`. Use `Scheduler` (ticks from
  `Canvas.willRenderCanvases`) for anything delayed; Hearthtalk does the same.
- **Any unhandled exception in a UI callback is reported as a fatal error** by the game's `BugReporterManager`.
  Wrap every callback (`EditorUi.Safe`).
- Logs: `BepInEx/LogOutput.log` (mod messages) and
  `%USERPROFILE%/AppData/LocalLow/Foxy Voxel/Going Medieval/Player.log` (Unity exceptions and stack traces).

## Assemblies to reference (from `Going Medieval_Data/Managed`, never shipped)
`Assembly-CSharp.dll` (includes NSEipix), `UnityEngine.CoreModule.dll`, `UnityEngine.UI.dll`,
`Unity.TextMeshPro.dll`, plus BepInEx `BepInEx.dll` and `0Harmony.dll`. Target `net472`.
