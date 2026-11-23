// Reads everything the optimizer needs from the game's own repositories (no CSV, follows game updates
// and data mods automatically).
using System;
using System.Collections.Generic;
using System.Linq;
using BestStarter.Core;
using NSEipix.Repository;
using NSMedieval.Model;
using NSMedieval.Repository;
using NSMedieval.State;
using NSMedieval.StatsSystem;
using NSMedieval.Types;
using NSMedieval.UI.Utils;
using UnityEngine;
using Attribute = NSMedieval.StatsSystem.Attribute;

namespace BestStarter.Game
{
    internal static class GameData
    {
        internal static string SkillName(int s) => UiUtils.Localize.GetText("skill_name_" + GameModel.SkillIds[s]);

        internal static GameModel Build()
        {
            var m = new GameModel();
            var attrRepo = Repository<AttributeRepository, Attribute>.Instance;
            for (int s = 0; s < GameModel.SkillCount; s++)
            {
                string id = GameModel.SkillIds[s];
                var (kind, names) = SkillMap.For(id);
                var st = (SkillType)Enum.Parse(typeof(SkillType), id);
                var d = new SkillDef { Id = id, Name = SkillName(s), Kind = kind };
                foreach (var n in names)
                {
                    if (!Enum.IsDefined(typeof(AttributeType), n)) continue;
                    var attr = attrRepo.GetByType((AttributeType)Enum.Parse(typeof(AttributeType), n));
                    var mod = attr?.SkillModifiers?.FirstOrDefault(x => x.Type == st);
                    if (mod?.PerLevelModifier == null || mod.PerLevelModifier.Length == 0) continue;
                    d.Curves.Add(new KeyValuePair<string, float[]>(n, mod.PerLevelModifier.ToArray()));
                }
                m.Skills[s] = d;
            }

            foreach (var p in Repository<GoalPreferenceRepository, GoalPreference>.Instance.GetAllItems())
                if (p.RelatedSkill != SkillType.None) m.PrefSkill[p.GetID()] = (int)p.RelatedSkill - 1;
            var lvRepo = Repository<GoalPreferenceLevelRepository, GoalPreferenceLevelData>.Instance;
            var effRepo = Repository<EffectorRepository, StatEffector>.Instance;
            for (int l = 1; l <= 6; l++)
            {
                var d = lvRepo.GetDataByPreferenceLevel(l);
                if (d == null) continue;
                m.XpMult[l] = d.RelatedSkillMultiplier;
                float mood = 0;
                foreach (var id in d.Effectors)
                {
                    var eff = effRepo.GetByID(id);
                    if (eff?.Effects == null) continue;
                    foreach (var fx in eff.Effects)
                        if (fx.Type == EffectorType.MoodModify && fx.Parameters != null && fx.Parameters.TryGetValue("BaseValue", out var bv)
                            && float.TryParse(bv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                            mood += f;
                }
                m.MoodByLevel[l] = mood;
            }
            var xp = Repository<SkillLevelsRepository, SkillLevels>.Instance.GetXpRequirements(SkillType.Construction);
            if (xp != null && xp.Length > GameModel.MaxLevel) m.XpCurve = xp.Take(GameModel.MaxLevel + 1).ToArray();
            m.MaxCreationPoints = Repository<GenerationSettingsRepository, GenerationSettings>.Instance.Settings.MaxCreationPoints;

            foreach (var b in Repository<BackgroundRepository, Background>.Instance.GetAllItems())
                if (!b.HideInGame) m.Traits.Add(FromBase(b, TraitKind.OldBackground));
            foreach (var b in Repository<BackStoryRepository, BackStory>.Instance.GetAllItems())
                if (!b.HideInGame) m.Traits.Add(FromBase(b, TraitKind.RecentBackground));
            foreach (var p in Repository<PseudonymRepository, Pseudonym>.Instance.GetAllItems())
            {
                if (p.HideInGame) continue;
                var t = New(p.GetID(), p.LocKeys, TraitKind.Pseudonym, p.CreationPointCost, p.SkillModifiers, p.GoalPreferences);
                m.Traits.Add(t);
            }
            foreach (var p in Repository<PerkRepository, Perk>.Instance.GetAllItems())
            {
                if (p.HideInGame) continue;
                var t = New(p.GetID(), p.LocKeys, TraitKind.Perk, p.CreationPointCost, p.SkillModifiers, p.GoalPreferences);
                if (p.AttributeModifiers != null)
                    foreach (var a in p.AttributeModifiers)
                        t.Attrs[a.Key.ToString()] = (t.Attrs.TryGetValue(a.Key.ToString(), out var v) ? v : 1f) * a.Value;
                if (p.ConflictsWith != null) t.Conflicts.UnionWith(p.ConflictsWith);
                t.ForbidOnStart = p.ForbidOnStart;
                m.Traits.Add(t);
            }
            return m;
        }

        static Trait FromBase(BackgroundBase b, TraitKind kind) =>
            New(b.GetID(), b.LocKeys, kind, b.CreationPointCost, b.SkillModifiers, b.GoalPreferences);

        static Trait New(string id, LocKeys[] locKeys, TraitKind kind, int cost, List<SkillValuePair> skills, List<StringIntPair> prefs)
        {
            var t = new Trait { Id = id, Kind = kind, Cost = cost };
            try { t.Name = UiUtils.Localize.GetText(LocKeyUtils.GetName(locKeys)); } catch { }
            if (string.IsNullOrEmpty(t.Name)) t.Name = id;
            if (skills != null)
                foreach (var sv in skills)
                    if (sv.Key != SkillType.None) t.SkillMods[(int)sv.Key - 1] += Mathf.RoundToInt(sv.Value);
            if (prefs != null)
                foreach (var gp in prefs) t.Prefs.Add(new KeyValuePair<string, int>(gp.Key, gp.Value));
            return t;
        }

        /// <summary>Old backgrounds and pseudonyms this settler may take (the editor's own body-type filter).</summary>
        internal static HashSet<string> AllowedFor(HumanoidInstance h)
        {
            var ignore = HumanoidUtils.GetPhysicalIgnoreTypes(new List<WorkerCharacteristicType>(), h.Info.BodyType, h.Info.Height, h.Info.WeightCoefficient);
            var ok = new HashSet<string>();
            foreach (var b in Repository<BackgroundRepository, Background>.Instance.GetAvailableBackgrounds(ignore)) ok.Add(b.GetID());
            foreach (var p in Repository<PseudonymRepository, Pseudonym>.Instance.GetAvailablePseudonyms(ignore)) ok.Add(p.GetID());
            return ok;
        }
    }
}
