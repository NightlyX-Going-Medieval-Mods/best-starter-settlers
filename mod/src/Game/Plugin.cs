using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BestStarter.Core;
using HarmonyLib;

namespace BestStarter.Game
{
    [BepInPlugin(Guid, "Best Starter", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "beststarter";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        static ConfigEntry<float> learningXp;
        static ConfigEntry<int> maxRefundTraits;
        static ConfigEntry<float> moodWeight;
        static ConfigEntry<int> maxStartLevel, minJobLevel, minOtherLevel;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            learningXp = Config.Bind("Scoring", "LearningXpTotal", 38800f,
                "XP a settler is assumed to earn in total, shared across their jobs, when valuing interests (Eager x2.5, " +
                "Passionate x4...) and XP perks. 38800 = level 0 to 20 at normal speed (the game's SkillLevels.json). 0 = ignore.");
            moodWeight = Config.Bind("Scoring", "MoodWeight", 0.04f,
                "Output-equivalent of one mood point while working (the game gives +6 mood for a Passionate job, +3 Eager, " +
                "-2 Unwilling, -4 Resentful). 0.04 = each mood point counts like 4% more output. 0 = ignore mood.");
            maxRefundTraits = Config.Bind("Scoring", "MaxRefundTraits", 1,
                "Max traits with a negative point cost per settler (backgrounds, pseudonym and perks together). Their downsides are mostly mood and social effects, so they're capped.");
            maxStartLevel = Config.Bind("Scoring", "MaxStartLevel", 20,
                "Highest level creation points are spent to reach in a job (levels from backgrounds can be higher). " +
                "Points left over are spread evenly over the other skills the settler doesn't dislike. 0 = no cap.");
            minJobLevel = Config.Bind("Scoring", "MinJobLevel", 20,
                "Every job starts at least this high (up to MaxStartLevel). Paid for before perks, so it always happens. 0 = off.");
            minOtherLevel = Config.Bind("Scoring", "MinOtherLevel", 3,
                "Every other skill the settler doesn't dislike starts at least this high. Paid for before perks. 0 = off.");
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"Best Starter {Version} loaded");
        }

        internal static Settings Settings => new Settings { LearningXp = learningXp.Value, MoodWeight = moodWeight.Value, MaxRefundTraits = maxRefundTraits.Value, MaxStartLevel = maxStartLevel.Value, MinJobLevel = minJobLevel.Value, MinOtherLevel = minOtherLevel.Value };
    }
}
