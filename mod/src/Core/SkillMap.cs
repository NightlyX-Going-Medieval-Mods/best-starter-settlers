// Which game attributes measure each skill's main job. The curves themselves are read from the game
// (Attributes.json "skillModifiers"); this table only picks the attributes, and is the one place a
// human choice is made, so it's kept small and visible. Only "higher is better" attributes are used.
namespace BestStarter.Core
{
    public static class SkillMap
    {
        public static (SkillKind kind, string[] attrs) For(string skillId)
        {
            switch (skillId)
            {
                case "AnimalHandling": return (SkillKind.Work, new[] { "TameChanceMultiplier", "TameAnimalSpeed" });
                case "Botanical": return (SkillKind.Work, new[] { "HarvestSpeed", "HarvestAmount" });
                case "Carpentry": return (SkillKind.Work, new[] { "WoodWorkingSpeed" });
                case "Construction": return (SkillKind.Work, new[] { "ConstructionSpeed" });
                case "Culinary": return (SkillKind.Work, new[] { "CookingSpeed" });
                case "Intelectual": return (SkillKind.Research, new[] { "ResearchSpeed" });
                case "Marksman": return (SkillKind.Other, new[] { "RangedDmg" });
                case "Medicine": return (SkillKind.Other, new[] { "TendingQuality" });
                case "Melee": return (SkillKind.Other, new[] { "MeleeDmg" });
                case "Smithing": return (SkillKind.Work, new[] { "SmithingSpeed" });
                case "Speechcraft": return (SkillKind.Other, new[] { "TradingValue" });
                case "Tailoring": return (SkillKind.Work, new[] { "MakeClothesSpeed" });
                case "Mining": return (SkillKind.Work, new[] { "MineSpeed", "MineAmount" });
                default: return (SkillKind.Work, new string[0]);   // Art: no curve in the game data
            }
        }
    }
}
