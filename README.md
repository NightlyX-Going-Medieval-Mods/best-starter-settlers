# Going Medieval – Settler Planner

A static web page for **min-maxing starting settlers** in *Going Medieval* (Foxy Voxel, Unity) with
**Advanced Customization**. You set what each settler should be good at. It picks the backgrounds,
pseudonym and perks, and spends the shared creation-point pool on skill levels.

All data comes from the player's own game install. Nothing is scraped from wikis: those were outdated or blocked.

## Run it

```bash
cd path/to/best-starter-settlers   # the folder containing index.html
python3 -m http.server 8000      # then open http://localhost:8000 (Ctrl+C to stop)
```

You need a server because the page `fetch()`es `data/*.csv`, and browsers block that on `file://`.
After changing JS, reload with **Ctrl+F5** so the cached scripts are replaced.

## Layout

```
best-starter-settlers/
├── index.html            page shell: tabs Team Builder / Explore Options / Roles / How it works
├── style.css             dark "medieval" theme
├── core.js               ALL logic: CSV parsing, game rules, team optimizer. No DOM, so Node can test it
├── app.js                UI only: rendering, inputs, presets/templates, localStorage (key gm-planner-vN)
├── data/                 generated CSVs. Don't hand-edit; regenerate with tools/extract_data.py
│   ├── all_options.csv   all 231 pickable options (the page's main input)
│   ├── old_backgrounds.csv / recent_backgrounds.csv / pseudonyms.csv / perks.csv   (per-category copies)
│   ├── interests.csv, interest_levels.csv, skills.csv, roles.csv, generation_rules.csv
└── tools/
    └── extract_data.py   rebuilds data/ from the game install (stdlib only, read-only on the game)
```

## Where the data comes from

Game install (Steam, Windows; WSL path):
`/mnt/c/Program Files (x86)/Steam/steamapps/common/Going Medieval/Going Medieval_Data/`

| CSV | Source |
|---|---|
| options (backgrounds, pseudonyms, perks) | `StreamingAssets/Worker/Background.json` (old/pre-plague), `BackStory.json` (recent/post-plague), `Pseudonym.json`, `Perk.json` |
| interests, interest levels | `Worker/GoalPreference.json`, `Worker/GoalPreferenceLevelData.json` |
| roles | `Roles/Role.json` |
| generation rules | `Worker/GenerationRules.json` (`maxCreationPoints` = 90) |
| English names and descriptions | `resources.assets`: a Unity localization table. Each entry is a length-prefixed key, 2 ints (2nd = language count), then one string per language, English first. 10 keys have no text (e.g. `Wright`); the ID is used instead. |

**After a game update**, regenerate and check what changed:
```bash
python3 tools/extract_data.py "/mnt/c/Program Files (x86)/Steam/steamapps/common/Going Medieval/Going Medieval_Data" data
git diff data/   # if under git; otherwise diff against a copy
```

CSV conventions: list fields are `;`-separated, key/value pairs are `key=value`.
`interests` values are level codes 1–6 (Resentful, Unwilling, Indifferent, Eager, Passionate, Incapable).
Skill IDs keep the game's spelling (`Intelectual`, `Botanical` = Botany).

## Game rules (checked in the decompiled game code)

Found by decompiling `Managed/Assembly-CSharp.dll` with ILSpy, 2026-11-22. These are implemented in `core.js`:

| Rule | Code reference |
|---|---|
| Settler cost = Σ trait `creationPointCost` + Σ (skill level − level given by traits). Levels from traits are free, and each extra level costs 1 point. | `HumanoidUtils.UpdateCreationPoints`, `GetSkillCreationPoints`, `GetBaseSkillPoints` |
| Team budget = `maxCreationPoints` (90) × settlers, **shared**, no per-settler cap | `CharacterEditController.GetGroupPoints` |
| Skill levels clamp to **0..50**, so a negative trait modifier still costs points (−3 Art ⇒ 3 points) | `WorkerSkill.SetLevel` |
| **Interests can't be bought**. They come only from perks, recent background, old background and pseudonym (in that order) | `HumanoidInstanceGoalPreferences.RecalculateDefaultGoalPreferences` |
| Interest stacking: same level → unchanged; otherwise `clamp(a + b − 3, 1, 5)`. Done per preference, then per skill (Hunting + Ranged both → Marksman) | `GoalPreferenceLevelRepository.GetCumulativeLevelData` |
| XP multiplier per interest: Eager ×2.5, Passionate ×4, Unwilling ×0.5, Resentful ×0.2, Incapable ×0 | `GoalPreferenceLevelData.json`, `WorkerSkill.AddExperience` |
| Up to **10 perks at any age** in the editor; age brackets only apply to random settlers | `CharacterStatsEdit.maxPerks` |
| `ignoreCharacteristicType` = **"only for"** that body type. Enforced for backgrounds/pseudonyms (Monk = male, Nun = female, The Spear Maiden, The Paramour); **perks aren't filtered** in the editor | `HumanoidUtils.GetPhysicalIgnoreTypes`, `CharacterStatsEdit.OnPerkAddClick` |
| Age doesn't affect points/perks. Old-age death starts at 66 | `HumanType.json` `oldAgeDeathRanges` |

## Optimizer model (core.js)

- Each settler has, per skill, a **priority** `w` and a **target** `t`. The value of a final level `L` is
  `w·min(L,t) + 0.2·w·(L−t above target, up to max level) + interestWeight·w·bonus(interest)`,
  where bonus is Passionate +2, Eager +1, Unwilling −1, Resentful −2, Incapable −3.
- `optimizeTeam` tries several "prices per point" λ. For each λ, every settler picks traits maximizing
  value − λ·points (`bestTraits`: pruned search over backgrounds × pseudonym × perk sets, then a local search
  that adds, drops or swaps single perks with exact interest stacking). `allocate` then spends the leftover pool on
  skill levels across the team, best value first. The best team is kept, and 6 alternatives per settler are returned.
- Options: must-have perks, banned perks, locks, max point-refund (negative-cost) perks (default 1), max perks (10),
  max level (50), interest weight (5).
- **Not scored:** perk attributes (move speed, XP gain, mood rules) and perk chemistry. They're shown so the user can judge them.

Test without a browser:
```bash
node -e "const c=require('./core.js'),fs=require('fs');const r=f=>fs.readFileSync('data/'+f+'.csv','utf8');
const d=c.buildData({interests:r('interests'),interest_levels:r('interest_levels'),all_options:r('all_options'),generation_rules:r('generation_rules'),roles:r('roles')});
console.log(c.optimizeTeam(d,[{weights:{Intelectual:3},targets:{Intelectual:30},gender:'Male',lock:{},mustPerks:['Wise']}],
 {pool:90,cap:50,maxPerks:10,interestWeight:5,allowBad:true,maxBad:1,bannedPerks:new Set()}).settlers[0].final)"
```

## Current state and decisions

- Default template: **"Best start: builder / farmer / scholar (3)"**. Fleet Footed + Wise are must-haves for all three, and the scholar also has Erudite.
  Animal Handling (animals can haul) and Culinary are deliberately boosted. The user is creating these settlers in-game.
- Older templates are kept: Solo generalist (1), Duo (2), two other Trios.
- Bump `STORE` in `app.js` (`gm-planner-vN`) whenever the saved-state shape or presets change, so old saved settings reset.

## Tooling on this machine

- .NET 8 SDK in `~/.dotnet` and `ilspycmd` 9.1 (newer versions need SDK 10). WSL lacks libicu, so run with:
  ```bash
  export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$HOME/.dotnet/tools:$PATH DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
  M="/mnt/c/Program Files (x86)/Steam/steamapps/common/Going Medieval/Going Medieval_Data/Managed"
  ilspycmd -p -o /tmp/gm-decomp -r "$M" "$M/Assembly-CSharp.dll"   # ~1 min, ~3,700 .cs files
  ```
  Keep decompiled game code **out of this folder**: it's the developer's copyrighted code, used only as reference.
- The game is a Unity 2022.3 **Mono** build, so BepInEx 5 + Harmony code mods are possible later. BepInEx is not installed.

## In-game mod (`mod/`)

A BepInEx 5 plugin that puts the optimizer on the character-creation screen. It reads all data **live from the
game's repositories** (no CSVs), so it follows game updates and data mods. API notes: `mod/NOTES.md`.

- **Use:** turn on *Advanced Customization* and click **BEST STARTER**. A skills × settlers grid opens (a clone of
  the game's editor popup). Click cells to pick a settler's jobs (gold); columns left on **Auto** get jobs from the
  mod (green). Then *Optimize team* or *Optimize <name> only*. It sets backgrounds, pseudonym, perks and skill levels
  through the editor's own setters; cells always show the live level and interest.
- **Auto job split:** unpicked skills are divided among Auto settlers to maximise Σ over all skills of
  ln(best settler's output), i.e. the colony is as good at each job as its best worker
  (`Optimizer.OptimizeTeamAuto`: alternating search from a trait-similarity seed plus two random seeds). Job counts
  per Auto settler stay within one of each other, since a settler only has so many hours.
  The learning budget (`LearningXpTotal`) is shared across a settler's jobs.
- **Scoring (no opinions):** each focus skill's output = the game's per-level attribute curve (`Attributes.json`),
  times perk multipliers on those attributes, times `GlobalWorkSpeed` × `MotorFunction` for work jobs (as the game's
  production code does). A job counts half at its starting level and half after learning, where interests and XP
  perks speed learning through the game's XP curve (`LearningXpTotal`, default 38,800 = level 0→20).
  Interests also count for mood: the game's own mood values while doing a job (Passionate +6, Eager +3,
  Unwilling −2, Resentful −4, from `GoalPreferenceLevelData` effectors) × `MoodWeight` (default 0.04 = 4% output per
  mood point), for every job held plus physical labor, which everyone does. Value = Σ ln(output), so +10% counts the
  same in every job and for every settler. Human choices: which attribute measures each skill
  (`mod/src/Core/SkillMap.cs`) and the two config numbers above.
- **Start levels (a rule, paid before perks):** every job starts at `MinJobLevel` (default 20, stepped down by 5
  only when the points can't cover it, e.g. 5 jobs per settler) and every other skill the settler doesn't dislike at
  `MinOtherLevel` (default 3). Points buy a job no higher than `MaxStartLevel` (default 20; backgrounds can give
  more). Points left after that go to perks and traits by score, then evenly over the other skills (lowest first,
  fair share per settler), skipping Unwilling, Resentful and Incapable skills.
- **Not scored:** movement speed, sleep and social effects. Negative-cost traits (backgrounds, pseudonym and perks
  together) are capped at `MaxRefundTraits` (default 1) per settler because their downsides are mostly mood effects.
- **Config:** `BepInEx/config/beststarter.cfg` (created on first launch).

```bash
cd mod
dotnet build -c Release -p:Deploy=true     # compiles against your game DLLs, copies to BepInEx/plugins/BestStarter/
cd tests/Harness && dotnet run             # offline check of the optimizer on the game's JSON (no game needed)
```

Install for players: BepInEx 5 (x64) in the game folder, then `BepInEx/plugins/BestStarter/BestStarter.dll`.
The game's own *Mods* menu is for data mods only and won't load it.
Uninstall: delete `BepInEx/plugins/BestStarter/`. Remove BepInEx too by deleting `winhttp.dll`,
`doorstop_config.ini`, `.doorstop_version`, `changelog.txt` and `BepInEx/`.

## Ideas / not done yet

- Score perk **chemistry** (`positiveChemistry` / `negativeChemistry` in Perk.json, not yet extracted) to avoid clashing teams.
- Score useful perk attributes (e.g. GlobalXpGain, MovementSpeed) instead of only showing them.
- Long-term goal, a separate project: a BepInEx mod for relationships/children, and maybe an in-game "template" button using this optimizer.
