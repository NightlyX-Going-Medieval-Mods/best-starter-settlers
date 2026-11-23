# Going Medieval – Settler Planner

A static web page for **min-maxing starting settlers** in *Going Medieval* (Foxy Voxel, Unity) with
**Advanced Customization**. You set what each settler should be good at. It picks the backgrounds,
pseudonym and perks, and spends the shared creation-point pool on skill levels.

All data comes from the player's own game install. Nothing is scraped from wikis: those were outdated or blocked.

## Run it

```bash
cd ~/vincent/going-medieval
python3 -m http.server 8000      # then open http://localhost:8000 (Ctrl+C to stop)
```

You need a server because the page `fetch()`es `data/*.csv`, and browsers block that on `file://`.
After changing JS, reload with **Ctrl+F5** so the cached scripts are replaced.

## Layout

```
going-medieval/
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

## Ideas / not done yet

- Score perk **chemistry** (`positiveChemistry` / `negativeChemistry` in Perk.json, not yet extracted) to avoid clashing teams.
- Score useful perk attributes (e.g. GlobalXpGain, MovementSpeed) instead of only showing them.
- Long-term goal, a separate project: a BepInEx mod for relationships/children, and maybe an in-game "template" button using this optimizer.
