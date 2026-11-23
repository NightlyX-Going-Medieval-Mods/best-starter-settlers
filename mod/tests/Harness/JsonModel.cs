// Builds the optimizer's GameModel from the game's StreamingAssets JSON (the in-game mod reads the
// same data from the game's repositories instead).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BestStarter.Core;

static class JsonModel
{
    static IEnumerable<JsonElement> Repo(string sa, string file)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(sa, file)).TrimStart('﻿'));
        return doc.RootElement.GetProperty("repository").EnumerateArray()
            .Where(e => !(e.TryGetProperty("hideInGame", out var h) && h.GetBoolean())).ToList();
    }
    static IEnumerable<JsonElement> Arr(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public static GameModel Load(string sa)
    {
        var m = new GameModel();
        var attrs = Repo(sa, "StatsSystem/Attributes.json").ToDictionary(a => a.GetProperty("id").GetString());
        for (int s = 0; s < GameModel.SkillCount; s++)
        {
            string id = GameModel.SkillIds[s];
            var (kind, names) = SkillMap.For(id);
            var d = new SkillDef { Id = id, Name = id, Kind = kind };
            foreach (var n in names)
            {
                var mod = Arr(attrs[n], "skillModifiers").First(x => x.GetProperty("skillId").GetString() == id);
                d.Curves.Add(new KeyValuePair<string, float[]>(n, Arr(mod, "perLevelModifier").Select(x => x.GetSingle()).ToArray()));
            }
            m.Skills[s] = d;
        }
        foreach (var p in Repo(sa, "Worker/GoalPreference.json"))
        {
            int rs = p.TryGetProperty("relatedSkill", out var r) ? r.GetInt32() : 0;
            if (rs > 0) m.PrefSkill[p.GetProperty("id").GetString()] = rs - 1;
        }
        var effectors = Repo(sa, "StatsSystem/Effectors.json").ToDictionary(e => e.GetProperty("id").GetString());
        foreach (var l in Repo(sa, "Worker/GoalPreferenceLevelData.json"))
        {
            int lv = l.GetProperty("goalPreferenceLevel").GetInt32();
            m.XpMult[lv] = l.GetProperty("relatedSkillMultiplier").GetSingle();
            float mood = 0;
            foreach (var id in Arr(l, "effectors"))
                if (effectors.TryGetValue(id.GetString(), out var eff))
                    foreach (var fx in Arr(eff, "effects"))
                        if (fx.GetProperty("type").GetString() == "MoodModify")
                            foreach (var par in Arr(fx, "parameters"))
                                if (par.GetProperty("key").GetString() == "BaseValue") mood += float.Parse(par.GetProperty("value").GetString(), System.Globalization.CultureInfo.InvariantCulture);
            m.MoodByLevel[lv] = mood;
        }
        var xp = Repo(sa, "Worker/SkillLevels.json").First();
        m.XpCurve = Arr(xp, "levels").Select(x => x.GetSingle()).ToArray();
        m.MaxCreationPoints = Repo(sa, "Worker/GenerationRules.json").First().GetProperty("maxCreationPoints").GetInt32();

        void Add(string file, TraitKind kind)
        {
            foreach (var e in Repo(sa, file))
            {
                var t = new Trait { Id = e.GetProperty("id").GetString(), Kind = kind };
                t.Name = t.Id;
                t.Cost = e.TryGetProperty("creationPointCost", out var c) ? c.GetInt32() : 0;
                foreach (var sm in Arr(e, "skillModifiers"))
                {
                    int i = GameModel.SkillIndex(sm.GetProperty("key").GetString());
                    if (i >= 0) t.SkillMods[i] += (int)Math.Round(sm.GetProperty("value").GetSingle());
                }
                foreach (var gp in Arr(e, "goalPreferences"))
                    t.Prefs.Add(new KeyValuePair<string, int>(gp.GetProperty("key").GetString(), gp.GetProperty("value").GetInt32()));
                foreach (var am in Arr(e, "attributeModifiers"))
                    t.Attrs[am.GetProperty("key").GetString()] = am.GetProperty("value").GetSingle();
                foreach (var cw in Arr(e, "conflictsWith")) t.Conflicts.Add(cw.GetString());
                t.ForbidOnStart = e.TryGetProperty("forbidOnStart", out var f) && f.GetBoolean();
                m.Traits.Add(t);
            }
        }
        Add("Worker/Background.json", TraitKind.OldBackground);
        Add("Worker/BackStory.json", TraitKind.RecentBackground);
        Add("Worker/Pseudonym.json", TraitKind.Pseudonym);
        Add("Worker/Perk.json", TraitKind.Perk);
        return m;
    }

    /// <summary>Old backgrounds/pseudonyms whose "only for" body type allows this gender (1 Male, 2 Female).</summary>
    public static HashSet<string> AllowedFor(string sa, int bodyType)
    {
        var ok = new HashSet<string>();
        foreach (var f in new[] { "Worker/Background.json", "Worker/Pseudonym.json" })
            foreach (var e in Repo(sa, f))
            {
                var only = Arr(e, "ignoreCharacteristicType").Select(x => x.GetInt32()).Where(x => x == 1 || x == 2).ToList();
                if (only.Count == 0 || only.Contains(bodyType)) ok.Add(e.GetProperty("id").GetString());
            }
        return ok;
    }
}
