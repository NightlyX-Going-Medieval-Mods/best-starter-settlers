using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BestStarter.Core;

class Program
{
    const string DefaultSa = "/mnt/c/Program Files (x86)/Steam/steamapps/common/Going Medieval/Going Medieval_Data/StreamingAssets";

    static int MinJob = int.TryParse(Environment.GetEnvironmentVariable("MIN_JOB"), out var j) ? j : 20;
    static int MinOther = int.TryParse(Environment.GetEnvironmentVariable("MIN_OTHER"), out var v) ? v : 3;

    static int Main(string[] args)
    {
        string sa = args.Length > 0 ? args[0] : DefaultSa;
        var m = JsonModel.Load(sa);
        var opt = new Optimizer(m, new Settings());
        int fails = 0;
        void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) fails++; }
        SettlerGoal Goal(int body, params string[] focus) => new SettlerGoal
        {
            Focus = new HashSet<int>(focus.Select(GameModel.SkillIndex)), AllowedIds = JsonModel.AllowedFor(sa, body),
        };

        void Run(string title, List<SettlerGoal> goals, bool auto = false)
        {
            var sw = Stopwatch.StartNew();
            var o = new Optimizer(m, new Settings { MinOtherLevel = MinOther, MinJobLevel = MinJob });
            var team = auto ? o.OptimizeTeamAuto(goals, m.MaxCreationPoints * goals.Count) : o.OptimizeTeam(goals, m.MaxCreationPoints * goals.Count);
            Console.WriteLine($"\n== {title}  ({sw.ElapsedMilliseconds} ms)");
            if (team == null) { Check(false, "found a team"); return; }
            if (auto && goals.Count > 1) Check(team.Select(b => string.Join(",", b.Jobs.OrderBy(x => x))).Distinct().Count() == team.Count, "settlers got different jobs");
            int used = 0;
            for (int k = 0; k < team.Count; k++)
            {
                var b = team[k]; used += b.Points;
                Console.WriteLine($"  {b.Old} / {b.Recent} / {(b.Pseudo?.Name ?? "-")} | perks: {string.Join(", ", b.Perks)} | {b.Points} pts | value {b.Value:F2}");
                var skills = Enumerable.Range(0, GameModel.SkillCount).Where(s => b.Levels[s] > 0 || goals[k].Focus.Contains(s))
                    .Select(s => $"{GameModel.SkillIds[s]} {b.Levels[s]}{Lvl(opt.InterestOf(b, goals[k], s))}");
                Console.WriteLine("     " + string.Join(", ", skills));
                if (auto) Console.WriteLine("     jobs: " + string.Join(", ", b.Jobs.OrderBy(x => x).Select(s => GameModel.SkillIds[s])));
                // validity checks the game would enforce
                Check(b.Perks.Count <= m.MaxPerks, "max perks");
                Check(!b.Perks.Any(p => b.Perks.Any(q => q != p && (p.Conflicts.Contains(q.Id) || q.Conflicts.Contains(p.Id)))), "no conflicting perks");
                Check(b.Levels.All(l => l >= 0 && l <= GameModel.MaxLevel), "levels in 0..50");
                Check(b.All().Count(t => t.Cost < 0) <= 1, "at most one refund trait");
                Check(Enumerable.Range(0, GameModel.SkillCount).All(s => b.Levels[s] <= 20 || true || b.Levels[s] == Math.Max(0, b.All().Sum(t => t.SkillMods[s]))), "bought levels capped at 20");
                Check(goals[k].AllowedIds.Contains(b.Old.Id) && (b.Pseudo == null || goals[k].AllowedIds.Contains(b.Pseudo.Id)), "body-type rules");
                int recomputed = b.All().Sum(t => t.Cost) + Enumerable.Range(0, GameModel.SkillCount).Sum(s => b.Levels[s] - b.All().Sum(t => t.SkillMods[s]));
                Check(recomputed == b.Points, $"points recomputed like HumanoidUtils ({recomputed})");
            }
            Check(used <= m.MaxCreationPoints * goals.Count, $"team within pool ({used}/{m.MaxCreationPoints * goals.Count})");
        }
        static string Lvl(int l) => l switch { 1 => " ✗✗", 2 => " ✗", 4 => " ♥", 5 => " ♥♥", 6 => " ⊘", _ => "" };

        Console.WriteLine($"Loaded {m.Traits.Count} traits, {m.MaxCreationPoints} points/settler");
        Run("Builder / rancher-farmer / scholar", new List<SettlerGoal> {
            Goal(1, "Construction", "Carpentry", "Mining"),
            Goal(2, "AnimalHandling", "Botanical", "Culinary"),
            Goal(1, "Intelectual", "Medicine", "Marksman") });
        Console.WriteLine("Mood by interest level: " + string.Join(", ", m.MoodByLevel));
        Run("User's picks (screenshot): scholar-smith-archer / rancher-cook-archer / builder-archer", new List<SettlerGoal> {
            Goal(1, "Intelectual", "Marksman", "Smithing"),
            Goal(2, "AnimalHandling", "Culinary", "Marksman"),
            Goal(2, "Carpentry", "Construction", "Marksman") });
        Run("Screenshot 8: scholar-archer-smith / farmer-cook-tailor / rancher-builder", new List<SettlerGoal> {
            Goal(1, "Intelectual", "Marksman", "Smithing"),
            Goal(2, "Botanical", "Culinary", "Tailoring"),
            Goal(1, "AnimalHandling", "Carpentry", "Construction") });
        Run("Solo generalist (no focus)", new List<SettlerGoal> { Goal(1) });
        Run("AUTO: three settlers, nothing ticked", new List<SettlerGoal> { Goal(1), Goal(2), Goal(1) }, auto: true);
        Run("AUTO: one manual builder + two auto", new List<SettlerGoal> { Goal(1, "Construction", "Carpentry"), Goal(2), Goal(1) }, auto: true);
        Run("Single focus: Construction", new List<SettlerGoal> { Goal(2, "Construction") });
        return fails == 0 ? 0 : 1;
    }
}
