// Plain data model for the optimizer. No Unity or game types here, so the same code runs
// inside the game (filled from the game's repositories) and in the offline test harness
// (filled from the game's StreamingAssets JSON).
using System;
using System.Collections.Generic;

namespace BestStarter.Core
{
    public enum TraitKind { OldBackground, RecentBackground, Pseudonym, Perk }

    public sealed class Trait
    {
        public string Id = "";
        public string Name = "";
        public TraitKind Kind;
        public int Cost;
        /// <summary>Skill level modifiers, indexed like <see cref="GameModel.Skills"/>.</summary>
        public int[] SkillMods = new int[GameModel.SkillCount];
        /// <summary>Interest changes in the game's order of definition: (preference id, level 1..6).</summary>
        public List<KeyValuePair<string, int>> Prefs = new List<KeyValuePair<string, int>>();
        /// <summary>Attribute multipliers (perks only), e.g. GlobalWorkSpeed = 1.25.</summary>
        public Dictionary<string, float> Attrs = new Dictionary<string, float>();
        public HashSet<string> Conflicts = new HashSet<string>();
        public bool ForbidOnStart;
        public override string ToString() => Name;
    }

    /// <summary>How the game turns a skill level into in-game output.</summary>
    public enum SkillKind
    {
        Work,      // time divided by GlobalWorkSpeed and MotorFunction (construction, crafting, farming...)
        Research,  // GlobalWorkSpeed applies, MotorFunction does not (ProductionStepWorker)
        Other,     // combat, medicine quality, trading: no work-speed multipliers
    }

    public sealed class SkillDef
    {
        public string Id = "";       // game SkillType name, e.g. "Botanical"
        public string Name = "";     // localized
        public SkillKind Kind;
        /// <summary>Attributes whose product is this skill's output, each with its per-level curve (51 values).</summary>
        public List<KeyValuePair<string, float[]>> Curves = new List<KeyValuePair<string, float[]>>();
    }

    public sealed class GameModel
    {
        public const int SkillCount = 14;
        public const int MaxLevel = 50;

        /// <summary>Same order as the game's SkillType enum (value - 1).</summary>
        public static readonly string[] SkillIds =
        {
            "AnimalHandling", "Botanical", "Carpentry", "Construction", "Culinary", "Intelectual", "Marksman",
            "Medicine", "Melee", "Smithing", "Speechcraft", "Tailoring", "Mining", "Art",
        };

        public SkillDef[] Skills = new SkillDef[SkillCount];
        public List<Trait> Traits = new List<Trait>();
        /// <summary>Interest (goal preference) id -> skill index, or absent if it isn't a skill interest.</summary>
        public Dictionary<string, int> PrefSkill = new Dictionary<string, int>();
        /// <summary>XP multiplier per interest level 0..6 (0 = none = 1x).</summary>
        public float[] XpMult = { 1f, 0.2f, 0.5f, 1f, 2.5f, 4f, 0f };
        /// <summary>Mood while doing a job, per interest level 0..6 (GoalPreferenceLevelData effectors → MoodModify).</summary>
        public float[] MoodByLevel = { 0f, -4f, -2f, 0f, 3f, 6f, 0f };
        /// <summary>Interest that every settler uses (hauling and other physical labor), not tied to a skill.</summary>
        public const string LaborPref = "PhysicalLaborPreference";
        /// <summary>Total XP needed for each level (51 values).</summary>
        public float[] XpCurve = new float[MaxLevel + 1];
        public int MaxCreationPoints = 90;
        public int MaxPerks = 10;

        public static int SkillIndex(string id) => Array.IndexOf(SkillIds, id);

        /// <summary>The game's interest stacking rule (GoalPreferenceLevelRepository.GetCumulativeLevelData).</summary>
        public static int Combine(int current, int add)
        {
            if (add <= 0) return current;
            if (current <= 0) return add;
            if (current == add) return current;
            return Math.Min(5, Math.Max(1, current + add - 3));
        }
    }

    /// <summary>What the player wants from one settler.</summary>
    public sealed class SettlerGoal
    {
        /// <summary>Skills to be good at. Empty = all skills count equally (generalist).</summary>
        public HashSet<int> Focus = new HashSet<int>();
        /// <summary>Old backgrounds / pseudonyms allowed for this settler's body type (null = all).</summary>
        public HashSet<string> AllowedIds;
    }

    public sealed class Settings
    {
        /// <summary>XP a settler is assumed to earn in total (shared across their jobs) when projecting interests and XP perks.
        /// Default 38800 = the XP from level 0 to level 20 (SkillLevels.json) at 1x speed.</summary>
        public float LearningXp = 38800f;
        /// <summary>Output-equivalent of one point of mood while doing a job (0.04 = +4% per mood point, so a
        /// Passionate job's +6 mood counts like +24% output, an Unwilling job's -2 like -8%). 0 = ignore mood.</summary>
        public float MoodWeight = 0.04f;
        /// <summary>Max traits with a negative point cost per settler (backgrounds, pseudonym and perks together).
        /// Their downsides are mostly mood and social effects that aren't fully measured.</summary>
        public int MaxRefundTraits = 1;
        /// <summary>Highest level points are spent to reach in a job (levels given by traits may be higher).
        /// Points left over are spread evenly over the settlers' other skills. 0 = no cap.</summary>
        public int MaxStartLevel = 20;
        /// <summary>Every skill that isn't a job (and that the settler doesn't dislike) starts at least this high.
        /// It's paid for before perks, so it always happens. 0 = off.</summary>
        public int MinOtherLevel = 3;
        /// <summary>Every job starts at least this high (capped by MaxStartLevel). Paid for before perks. 0 = off.</summary>
        public int MinJobLevel = 20;
    }

    public sealed class Build
    {
        public Trait Old, Recent, Pseudo;       // Pseudo may be null
        public List<Trait> Perks = new List<Trait>();
        public int[] Levels = new int[GameModel.SkillCount];
        public int Points;                       // creation points as the game counts them
        public double Value;                     // Σ ln(output) over the focus skills
        public HashSet<int> Jobs = new HashSet<int>();   // skills this build was optimised for

        public IEnumerable<Trait> All()
        {
            yield return Old; yield return Recent;
            if (Pseudo != null) yield return Pseudo;
            foreach (var p in Perks) yield return p;
        }
    }
}
