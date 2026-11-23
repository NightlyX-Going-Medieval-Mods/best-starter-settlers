// Team optimizer. Every number it scores comes from the game's own data:
//   output(skill) = geometric mean over the skill's attributes of
//                   attribute curve(level) × perk multipliers on it   (Attributes.json, Perk.json)
//                 × GlobalWorkSpeed × MotorFunction     (work jobs; research skips MotorFunction)
// Value = Σ over focus skills and settlers of ln(output): a 10% gain counts the same in every job and for
// every settler, so no job or settler is favoured and points spread where they help most in relative terms.
// The level used is the level after a fixed amount of learning (Settings.LearningXp), sped up or slowed
// down by the interest's XP multiplier and GlobalXpGain, so interests and XP perks are valued through
// the game's XP curve. Points follow HumanoidUtils: trait costs + Σ(level − level given by traits).
using System;
using System.Collections.Generic;
using System.Linq;

namespace BestStarter.Core
{
    public sealed class Optimizer
    {
        const int N = GameModel.SkillCount, Max = GameModel.MaxLevel;
        static readonly double[] Lambdas = { 0, 0.002, 0.004, 0.007, 0.01, 0.014, 0.02, 0.03, 0.045, 0.07, 0.1, 0.15 };

        readonly GameModel m;
        readonly Settings cfg;
        int jobMin;   // MinJobLevel, stepped down by OptimizeTeam when the points can't cover it
        readonly double[][] baseOut = new double[N][];   // Π attribute curves at each integer level

        public Optimizer(GameModel model, Settings settings)
        {
            m = model; cfg = settings;
            for (int s = 0; s < N; s++)
            {
                var t = new double[Max + 1];
                for (int l = 0; l <= Max; l++)
                {
                    double v = 1;
                    var curves = m.Skills[s].Curves;
                    if (curves.Count == 0) v = 0.9 + 1.1 * l / Max;   // no curve in game data (Art): crafting-like line
                    foreach (var c in curves) v *= c.Value[Math.Min(l, c.Value.Length - 1)];
                    t[l] = curves.Count > 1 ? Math.Pow(v, 1.0 / curves.Count) : v;
                }
                baseOut[s] = t;
            }
        }

        // ---------- evaluation of one trait set ----------
        sealed class Ctx
        {
            public Trait Old, Recent, Pseudo;
            public List<Trait> Perks = new List<Trait>();
            public int[] Base = new int[N];      // level given by traits (may be negative)
            public int[] Pref = new int[N];      // interest level per skill (0 = none)
            public double[] Mult = new double[N]; // perk attribute multipliers × job multipliers
            public double XpGain = 1;
            public int LaborPref;                 // interest in physical labor (everyone hauls)
            public int TraitCost;
            public bool[] Focus;
        }

        Ctx Make(SettlerGoal g, Trait old, Trait recent, Trait pseudo, IList<Trait> perks)
        {
            var c = new Ctx { Old = old, Recent = recent, Pseudo = pseudo, Perks = perks.ToList(), Focus = FocusOf(g) };
            var attrs = new Dictionary<string, double>();
            // game order for interests: perks, recent, old, pseudonym
            var ordered = new List<Trait>(perks);
            if (recent != null) ordered.Add(recent);
            if (old != null) ordered.Add(old);
            if (pseudo != null) ordered.Add(pseudo);
            var byPref = new List<KeyValuePair<string, int>>();
            foreach (var t in ordered)
            {
                c.TraitCost += t.Cost;
                for (int s = 0; s < N; s++) c.Base[s] += t.SkillMods[s];
                foreach (var a in t.Attrs) attrs[a.Key] = (attrs.TryGetValue(a.Key, out var v) ? v : 1) * a.Value;
                foreach (var p in t.Prefs)
                {
                    int i = byPref.FindIndex(x => x.Key == p.Key);
                    if (i < 0) byPref.Add(p); else byPref[i] = new KeyValuePair<string, int>(p.Key, GameModel.Combine(byPref[i].Value, p.Value));
                }
            }
            foreach (var p in byPref)
            {
                if (m.PrefSkill.TryGetValue(p.Key, out int s)) c.Pref[s] = GameModel.Combine(c.Pref[s], p.Value);
                else if (p.Key == GameModel.LaborPref) c.LaborPref = p.Value;
            }
            double A(string k) => attrs.TryGetValue(k, out var v) ? v : 1;
            // the learning budget is the settler's time, shared across their jobs
            c.XpGain = A("GlobalXpGain") / Math.Max(1, c.Focus.Count(f => f));
            for (int s = 0; s < N; s++)
            {
                var d = m.Skills[s];
                double mult = 1;
                foreach (var cv in d.Curves) mult *= A(cv.Key);
                if (d.Curves.Count > 1) mult = Math.Pow(mult, 1.0 / d.Curves.Count);
                if (d.Kind == SkillKind.Work) mult *= A("GlobalWorkSpeed") * A("MotorFunction");
                else if (d.Kind == SkillKind.Research) mult *= A("GlobalWorkSpeed");
                c.Mult[s] = mult;
            }
            return c;
        }

        static bool[] FocusOf(SettlerGoal g)
        {
            var f = new bool[N];
            for (int s = 0; s < N; s++) f[s] = g.Focus.Count == 0 || g.Focus.Contains(s);
            return f;
        }

        /// <summary>Projected (fractional) level after the learning budget.</summary>
        readonly Dictionary<(int, int, double), double> projMemo = new Dictionary<(int, int, double), double>();
        double Projected(int level, int pref, double xpGain)
        {
            var key = (level, pref, xpGain);
            if (projMemo.TryGetValue(key, out var hit)) return hit;
            return projMemo[key] = ProjectedRaw(level, pref, xpGain);
        }
        double ProjectedRaw(int level, int pref, double xpGain)
        {
            double xp = m.XpCurve[level] + cfg.LearningXp * m.XpMult[pref] * xpGain;
            if (xp >= m.XpCurve[Max]) return Max;
            int l = level;
            while (l < Max && m.XpCurve[l + 1] <= xp) l++;
            double span = m.XpCurve[l + 1] - m.XpCurve[l];
            return l + (span > 0 ? (xp - m.XpCurve[l]) / span : 0);
        }

        /// <summary>ln(output), half at the start (bought level) and half after the learning budget (projected
        /// level), so both the first raid and the long run count. Incapable or zero output is floored.</summary>
        double LogOut(Ctx c, int s, int level)
        {
            if (c.Pref[s] == 6) return Math.Log(0.05);
            double now = BaseAt(s, level) * c.Mult[s], later = Out(c, s, level);
            return 0.5 * (Math.Log(Math.Max(0.05, now)) + Math.Log(Math.Max(0.05, later)));
        }

        double BaseAt(int s, double p)
        {
            int lo = (int)p; double fr = p - lo;
            var t = baseOut[s];
            return lo >= Max ? t[Max] : t[lo] + (t[lo + 1] - t[lo]) * fr;
        }

        /// <summary>Output at the projected level (after learning).</summary>
        double Out(Ctx c, int s, int level)
        {
            if (c.Pref[s] == 6) return 0;   // Incapable: the job is disabled and gains no XP
            return BaseAt(s, Projected(level, c.Pref[s], c.XpGain)) * c.Mult[s];
        }

        static int Lo(int b) => Math.Min(Max, Math.Max(0, b));

        /// <summary>Highest level points may buy for a skill that starts at <paramref name="lo"/> (traits can exceed it).</summary>
        int Cap(int lo) => cfg.MaxStartLevel <= 0 ? Max : Math.Max(lo, Math.Min(Max, cfg.MaxStartLevel));

        /// <summary>Skills that get the minimum level and leftover points: not a job, not Unwilling/Resentful/Incapable.</summary>
        static bool Spreadable(Ctx c, int s) => !c.Focus[s] && c.Pref[s] != 1 && c.Pref[s] != 2 && c.Pref[s] != 6;

        /// <summary>Starting level after the minimum-level rule.</summary>
        int Floor(Ctx c, int s)
        {
            int lo = Lo(c.Base[s]);
            if (c.Focus[s] && c.Pref[s] != 6) return Math.Max(lo, Math.Min(jobMin, Cap(lo)));
            return Spreadable(c, s) ? Math.Max(lo, Math.Min(cfg.MinOtherLevel, Cap(lo))) : lo;
        }

        /// <summary>ln of the mood factor for working with this interest (game's MoodModify values × MoodWeight).</summary>
        double MoodLog(int pref) => pref <= 0 || pref >= m.MoodByLevel.Length ? 0 : Math.Log(Math.Max(0.05, 1 + cfg.MoodWeight * m.MoodByLevel[pref]));

        /// <summary>Mood part of a settler's value: every job they hold plus physical labor, which everyone does.</summary>
        double MoodValue(Ctx c)
        {
            double v = MoodLog(c.LaborPref);
            for (int s = 0; s < N; s++) if (c.Focus[s] && c.Pref[s] != 6) v += MoodLog(c.Pref[s]);
            return v;
        }

        /// <summary>Value − λ·points with skill levels bought at price λ. −∞ if a focus skill is Incapable.</summary>
        double Net(Ctx c, double lam)
        {
            double v = 0; int cost = c.TraitCost;
            for (int s = 0; s < N; s++)
            {
                int f = Floor(c, s); cost += f - c.Base[s];
                if (!c.Focus[s]) continue;
                if (c.Pref[s] == 6 && FocusIsExplicit(c)) return double.NegativeInfinity;
                v += c.Pref[s] == 6 ? Math.Log(0.05) : BestScan(s, f, c.Pref[s], c.XpGain, lam) + Math.Log(c.Mult[s]);
            }
            return v + MoodValue(c) - lam * cost;
        }

        // max over L ≥ lo of ln(base output at L) − λ(L − lo). Independent of perk multipliers (they add a
        // constant in log space), so it's cached per (skill, lo, interest, XP gain, λ).
        readonly Dictionary<(int, int, int, double, double), double> scanMemo = new Dictionary<(int, int, int, double, double), double>();
        double BestScan(int s, int lo, int pref, double xpGain, double lam)
        {
            var key = (s, lo, pref, xpGain, lam);
            if (scanMemo.TryGetValue(key, out var hit)) return hit;
            double best = double.NegativeInfinity;
            var t = baseOut[s];
            for (int l = lo, cap = Cap(lo); l <= cap; l++)
            {
                double later = BaseAt(s, Projected(l, pref, xpGain)), now = t[l];
                best = Math.Max(best, 0.5 * (Math.Log(Math.Max(1e-6, now)) + Math.Log(Math.Max(1e-6, later))) - lam * (l - lo));
            }
            return scanMemo[key] = best;
        }

        static bool FocusIsExplicit(Ctx c) => c.Focus.Any(f => !f);

        // ---------- trait search for one settler at price λ ----------
        Ctx BestTraits(SettlerGoal g, double lam)
        {
            bool Allowed(Trait t) => g.AllowedIds == null || t.Kind == TraitKind.RecentBackground || t.Kind == TraitKind.Perk || g.AllowedIds.Contains(t.Id);
            var olds = m.Traits.Where(t => t.Kind == TraitKind.OldBackground && Allowed(t)).ToList();
            var recents = m.Traits.Where(t => t.Kind == TraitKind.RecentBackground).ToList();
            var pseudos = m.Traits.Where(t => t.Kind == TraitKind.Pseudonym && Allowed(t)).ToList();
            var perks = m.Traits.Where(t => t.Kind == TraitKind.Perk && !t.ForbidOnStart).ToList();
            if (olds.Count == 0 || recents.Count == 0) return null;

            var empty = new Trait[0];
            double n0 = Net(Make(g, null, null, null, empty), lam);
            double H(Trait t) => Net(Make(g, t.Kind == TraitKind.OldBackground ? t : null, t.Kind == TraitKind.RecentBackground ? t : null,
                t.Kind == TraitKind.Pseudonym ? t : null, t.Kind == TraitKind.Perk ? new[] { t } : empty), lam) - n0;
            List<Trait> Top(List<Trait> l, int k) => l.Select(t => (t, h: H(t))).OrderByDescending(x => x.h).Take(k).Select(x => x.t).ToList();

            var topOld = Top(olds, 8); var topRecent = Top(recents, 8);
            var topPseudo = new List<Trait> { null }; topPseudo.AddRange(Top(pseudos, 6));
            var perkPool = Top(perks, 20);

            var bases = new List<(Ctx c, double n)>();
            foreach (var o in topOld) foreach (var r in topRecent) foreach (var p in topPseudo)
            {
                if (Refunds(o, r, p) > cfg.MaxRefundTraits) continue;
                var c = Make(g, o, r, p, empty); bases.Add((c, Net(c, lam)));
            }
            if (bases.Count == 0) return null;
            Ctx best = null; double bestNet = double.NegativeInfinity;
            foreach (var (b, _) in bases.OrderByDescending(x => x.n).Take(15))
            {
                var (c, n) = ImprovePerks(g, b, lam, perkPool);
                if (n > bestNet) { best = c; bestNet = n; }
            }
            if (best != null) { var (c, n) = ImprovePerks(g, best, lam, perks); best = c; }   // full pool, add/drop/swap
            return best;
        }

        static int Refunds(params Trait[] ts) => ts.Count(t => t != null && t.Cost < 0);

        bool CanAdd(Ctx cur, List<Trait> chosen, Trait p) =>
            chosen.Count < m.MaxPerks && !chosen.Contains(p)
            && !chosen.Any(c => c.Conflicts.Contains(p.Id) || p.Conflicts.Contains(c.Id))
            && (p.Cost >= 0 || Refunds(cur.Old, cur.Recent, cur.Pseudo) + chosen.Count(c => c.Cost < 0) < cfg.MaxRefundTraits);

        (Ctx, double) ImprovePerks(SettlerGoal g, Ctx start, double lam, List<Trait> pool)
        {
            var cur = start; double curNet = Net(cur, lam);
            for (int it = 0; it < 30; it++)
            {
                Ctx bestC = null; double bestN = curNet + 1e-9;
                void Try(List<Trait> ps) { var c = Make(g, cur.Old, cur.Recent, cur.Pseudo, ps); double n = Net(c, lam); if (n > bestN) { bestN = n; bestC = c; } }
                foreach (var p in pool) if (CanAdd(cur, cur.Perks, p)) Try(new List<Trait>(cur.Perks) { p });
                foreach (var q in cur.Perks)
                {
                    var rest = cur.Perks.Where(x => x != q).ToList(); Try(rest);
                    foreach (var p in pool) if (p != q && CanAdd(cur, rest, p)) Try(new List<Trait>(rest) { p });
                }
                if (bestC == null) break;
                cur = bestC; curNet = bestN;
            }
            return (cur, curNet);
        }

        // ---------- spend the shared pool on skill levels ----------
        List<Build> Allocate(List<Ctx> team, int pool)
        {
            var lv = team.Select(c => Enumerable.Range(0, N).Select(s => Floor(c, s)).ToArray()).ToList();
            int left = pool;
            foreach (var c in team) { left -= c.TraitCost; for (int s = 0; s < N; s++) left -= Floor(c, s) - c.Base[s]; }
            if (left < 0) return null;
            while (left > 0)
            {
                double bestR = 1e-12; int bk = -1, bs = -1, bn = 0;
                for (int k = 0; k < team.Count; k++)
                    for (int s = 0; s < N; s++)
                    {
                        if (!team[k].Focus[s]) continue;
                        int L = lv[k][s]; double o = LogOut(team[k], s, L);
                        for (int n = 1; n <= Math.Min(Cap(Lo(team[k].Base[s])) - L, left); n++)
                        {
                            double r = (LogOut(team[k], s, L + n) - o) / n;
                            if (r > bestR) { bestR = r; bk = k; bs = s; bn = n; }
                        }
                    }
                if (bk < 0) break;
                lv[bk][bs] += bn; left -= bn;
            }
            Spread(team, lv, ref left);
            var res = new List<Build>();
            for (int k = 0; k < team.Count; k++)
            {
                var c = team[k];
                var b = new Build { Old = c.Old, Recent = c.Recent, Pseudo = c.Pseudo, Perks = c.Perks, Levels = lv[k], Points = c.TraitCost };
                for (int s = 0; s < N; s++) { b.Points += lv[k][s] - c.Base[s]; if (c.Focus[s]) b.Value += LogOut(c, s, lv[k][s]); }
                b.Value += MoodValue(c);
                res.Add(b);
            }
            return res;
        }

        /// <summary>Points the jobs can't use (every job at the cap) go evenly to the other skills: always raise the
        /// lowest one, preferring higher interest on ties. Skills the settler dislikes or can't do are skipped.</summary>
        void Spread(List<Ctx> team, List<int[]> lv, ref int left)
        {
            var given = new int[team.Count];   // spread points per settler, so each gets a fair share
            while (left > 0)
            {
                int bk = -1, bs = -1;
                for (int k = 0; k < team.Count; k++)
                    for (int s = 0; s < N; s++)
                    {
                        var c = team[k]; int p = c.Pref[s];
                        if (!Spreadable(c, s) || lv[k][s] >= Cap(Lo(c.Base[s]))) continue;
                        if (bk < 0 || given[k] < given[bk]
                            || (given[k] == given[bk] && (lv[k][s] < lv[bk][bs]
                                || (lv[k][s] == lv[bk][bs] && Rank(p) > Rank(team[bk].Pref[bs]))))) { bk = k; bs = s; }
                    }
                if (bk < 0) return;
                lv[bk][bs]++; given[bk]++; left--;
            }
        }

        static int Rank(int pref) => pref == 5 ? 3 : pref == 4 ? 2 : 1;

        /// <summary>Best builds for the given settlers sharing <paramref name="pool"/> creation points.
        /// The job minimum steps down by 5 only when the points can't pay for it (many jobs per settler).</summary>
        public List<Build> OptimizeTeam(IList<SettlerGoal> goals, int pool)
        {
            for (jobMin = Math.Max(0, cfg.MinJobLevel); ; jobMin = Math.Max(0, jobMin - 5))
            {
                var r = OptimizeTeamAt(goals, pool);
                if (r != null || jobMin == 0) return r;
            }
        }

        List<Build> OptimizeTeamAt(IList<SettlerGoal> goals, int pool)
        {
            List<Build> best = null; double bestV = double.NegativeInfinity;
            foreach (double lam in Lambdas)
            {
                var team = goals.Select(g => CachedBestTraits(g, lam)).ToList();
                if (team.Any(c => c == null)) continue;
                var builds = Allocate(team, pool);
                if (builds == null) continue;
                double v = builds.Sum(b => b.Value);
                if (v > bestV + 1e-9) { bestV = v; best = builds; }
            }
            return best;
        }

        // BestTraits depends only on the focus set, the allowed ids and λ, so repeated calls during the
        // job-split search are cached.
        readonly Dictionary<string, Ctx> traitCache = new Dictionary<string, Ctx>();
        Ctx CachedBestTraits(SettlerGoal g, double lam)
        {
            string key = jobMin + "|" + lam.ToString("R") + "|" + string.Join(",", g.Focus.OrderBy(x => x)) + "|"
                         + (g.AllowedIds == null ? "all" : string.Join(",", g.AllowedIds.OrderBy(x => x)));
            if (!traitCache.TryGetValue(key, out var c)) traitCache[key] = c = BestTraits(g, lam);
            return c;
        }

        /// <summary>Progress callback for long runs (step, total).</summary>
        public Action<int, int> Progress;

        /// <summary>
        /// Like <see cref="OptimizeTeam"/>, but settlers with an empty focus are "auto": the skills nobody chose are
        /// split among them. The split is searched to maximise Σ over all skills of ln(best settler's output),
        /// i.e. the colony is as good at each job as its best worker. The split itself is not an opinion: the
        /// seeds below are only starting points for the search, and the best result by that one score wins.
        /// Each returned build's <see cref="Build.Jobs"/> holds the skills it was optimised for.
        /// </summary>
        public List<Build> OptimizeTeamAuto(IList<SettlerGoal> goals, int pool)
        {
            var auto = Enumerable.Range(0, goals.Count).Where(k => goals[k].Focus.Count == 0).ToList();
            if (auto.Count == 0 || goals.Count == 1)
            {
                var b = OptimizeTeam(goals, pool);
                if (b != null) for (int k = 0; k < b.Count; k++) b[k].Jobs = Jobs(goals[k]);
                return b;
            }
            var chosen = new HashSet<int>(goals.SelectMany(g => g.Focus));
            var free = Enumerable.Range(0, N).Where(s => !chosen.Contains(s)).ToList();
            if (free.Count < auto.Count) free = Enumerable.Range(0, N).ToList();   // everything taken: autos may overlap

            var seeds = new List<List<int>[]> { ClusterSeed(free, auto.Count) };
            var rng = new Random(12345);
            for (int i = 0; i < 2; i++) seeds.Add(RandomSeed(free, auto.Count, rng));

            List<Build> best = null; double bestV = double.NegativeInfinity;
            int step = 0, total = seeds.Count * 3;
            foreach (var seed in seeds)
            {
                var split = seed;
                for (int iter = 0; iter < 3; iter++)
                {
                    Progress?.Invoke(++step, total);
                    var g2 = goals.Select((g, k) => new SettlerGoal { AllowedIds = g.AllowedIds,
                        Focus = auto.Contains(k) ? new HashSet<int>(split[auto.IndexOf(k)]) : g.Focus }).ToList();
                    var builds = OptimizeTeam(g2, pool);
                    if (builds == null) break;
                    // effective output = job output × the mood factor of that settler's interest in it
                    var outp = builds.Select((b, k) => Enumerable.Range(0, N).Select(s => EffectiveOutput(b, g2[k], s)).ToArray()).ToList();
                    double v = Enumerable.Range(0, N).Sum(s => Math.Log(Math.Max(0.05, outp.Max(o => o[s]))))
                               + builds.Select((b, k) => MoodLog(Make(g2[k], b.Old, b.Recent, b.Pseudo, b.Perks).LaborPref)).Sum();
                    if (v > bestV + 1e-9)
                    {
                        bestV = v; best = builds;
                        for (int k = 0; k < builds.Count; k++) builds[k].Jobs = new HashSet<int>(g2[k].Focus);
                    }
                    // give every free skill to the auto settler who is now best at it
                    var next = auto.Select(_ => new List<int>()).ToArray();
                    foreach (int s in free) next[Enumerable.Range(0, auto.Count).OrderByDescending(a => outp[auto[a]][s]).First()].Add(s);
                    // a settler only has so many hours: keep job counts within one of each other. The busiest
                    // settler hands over the job the least busy one is relatively best at.
                    while (true)
                    {
                        int hi = Enumerable.Range(0, next.Length).OrderByDescending(x => next[x].Count).First();
                        int lo = Enumerable.Range(0, next.Length).OrderBy(x => next[x].Count).First();
                        if (next[hi].Count - next[lo].Count <= 1) break;
                        int s = next[hi].OrderByDescending(x => outp[auto[lo]][x] / Math.Max(1e-6, outp[auto[hi]][x])).First();
                        next[hi].Remove(s); next[lo].Add(s);
                    }
                    if (Enumerable.Range(0, next.Length).All(a => new HashSet<int>(next[a]).SetEquals(split[a]))) break;
                    split = next;
                }
            }
            return best;
        }

        static HashSet<int> Jobs(SettlerGoal g) => new HashSet<int>(g.Focus.Count == 0 ? Enumerable.Range(0, N) : g.Focus);

        static List<int>[] RandomSeed(List<int> free, int parts, Random rng)
        {
            var order = free.OrderBy(_ => rng.Next()).ToList();
            var res = Enumerable.Range(0, parts).Select(_ => new List<int>()).ToArray();
            for (int i = 0; i < order.Count; i++) res[i % parts].Add(order[i]);
            return res;
        }

        /// <summary>Groups skills that the game's traits tend to raise together (backgrounds, pseudonyms and
        /// perks with positive modifiers or interests in both), in balanced groups.</summary>
        List<int>[] ClusterSeed(List<int> free, int parts)
        {
            var sim = new double[N, N];
            foreach (var t in m.Traits)
            {
                var up = Enumerable.Range(0, N).Where(s => t.SkillMods[s] > 0
                    || t.Prefs.Any(p => p.Value >= 4 && m.PrefSkill.TryGetValue(p.Key, out int ps) && ps == s)).ToList();
                foreach (int a in up) foreach (int b in up) if (a != b) sim[a, b] += 1;
            }
            int cap = (free.Count + parts - 1) / parts;
            var res = Enumerable.Range(0, parts).Select(_ => new List<int>()).ToArray();
            var left = new List<int>(free);
            // seed each group with the skill least similar to the groups so far
            for (int p = 0; p < parts && left.Count > 0; p++)
            {
                int pick = left.OrderBy(s => res.Take(p).SelectMany(g => g).Sum(o => sim[s, o])).ThenBy(s => s).First();
                res[p].Add(pick); left.Remove(pick);
            }
            while (left.Count > 0)
            {
                var (s, g) = left.SelectMany(x => Enumerable.Range(0, parts).Where(p => res[p].Count < cap)
                    .Select(p => (x, p, score: res[p].Average(o => sim[x, o]))))
                    .OrderByDescending(t => t.score).Select(t => (t.x, t.p)).First();
                res[g].Add(s); left.Remove(s);
            }
            return res;
        }

        /// <summary>Output of one skill for a finished build (for display).</summary>
        public double SkillOutput(Build b, SettlerGoal g, int s)
        {
            var c = Make(g, b.Old, b.Recent, b.Pseudo, b.Perks);
            return Out(c, s, b.Levels[s]);
        }

        /// <summary>Job output × mood factor (what the auto job split compares between settlers).</summary>
        public double EffectiveOutput(Build b, SettlerGoal g, int s)
        {
            var c = Make(g, b.Old, b.Recent, b.Pseudo, b.Perks);
            return Out(c, s, b.Levels[s]) * Math.Exp(c.Pref[s] == 6 ? 0 : MoodLog(c.Pref[s]));
        }

        public int InterestOf(Build b, SettlerGoal g, int s) => Make(g, b.Old, b.Recent, b.Pseudo, b.Perks).Pref[s];
    }
}
