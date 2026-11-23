// The "BEST STARTER" button on the character-creation stats panel, the optimize run, and applying the
// result through the editor's own setters. The window itself is in Window.cs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BestStarter.Core;
using HarmonyLib;
using NSEipix.Base;
using NSEipix.View.UI;
using NSMedieval.Controllers;
using NSMedieval.StatsSystem;
using NSMedieval.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BestStarter.Game
{
    [HarmonyPatch(typeof(CharacterStatsEdit), "Start")]
    internal static class Patch_CharacterStatsEditStart
    {
        static void Postfix(CharacterStatsEdit __instance)
        {
            try { EditorUi.AddButton(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Couldn't add the Best Starter button: " + e); }
        }
    }

    internal static class EditorUi
    {
        const string ButtonName = "BestStarterButton";
        /// <summary>Skills the player ticked, per settler index. Empty = auto (the mod assigns jobs).</summary>
        internal static readonly Dictionary<int, HashSet<int>> Manual = new Dictionary<int, HashSet<int>>();
        /// <summary>Jobs the last run gave each auto settler.</summary>
        internal static readonly Dictionary<int, HashSet<int>> AutoJobs = new Dictionary<int, HashSet<int>>();
        internal static SoundButton ButtonTemplate;
        internal static bool Running;
        internal static string Status = "";
        static GameObject button;
        static int progressStep, progressTotal;

        internal static CharacterEditController Ctl => MonoSingleton<CharacterEditController>.Instance;
        internal static HashSet<int> ManualFor(int i) => Manual.TryGetValue(i, out var f) ? f : (Manual[i] = new HashSet<int>());

        internal static void AddButton(CharacterStatsEdit stats)
        {
            var t = Traverse.Create(stats);
            var sources = new[] { "saveWorkerButton", "loadWorkerButton", "reRollWorkerButton" }
                .Select(n => t.Field(n).GetValue<SoundButton>()).Where(b => b != null).ToList();
            var src = sources.FirstOrDefault(b => b.GetComponentInChildren<TMP_Text>(true) != null) ?? sources.FirstOrDefault();
            if (src == null) { Plugin.Log.LogWarning("No editor button found to copy."); return; }
            ButtonTemplate = src;
            var parent = src.transform.parent;
            if (parent.Find(ButtonName) != null) return;

            var go = Ui.CloneButton(src, parent, "BEST STARTER", () => Safe(Window.Open),
                "Best Starter\nChoose each settler's jobs (or leave them on Auto) and let the mod pick backgrounds, " +
                "pseudonym, perks and skill levels from the game's own numbers.");
            go.name = ButtonName;
            go.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
            if (parent.GetComponent<LayoutGroup>() == null)
            {
                var r = (RectTransform)src.transform;
                ((RectTransform)go.transform).anchoredPosition = r.anchoredPosition + new Vector2(0f, -(r.rect.height + 4f));
            }
            button = go;
            var c = Ctl;
            c.EditModeEnabledAction = (Action<bool>)Delegate.Combine(c.EditModeEnabledAction, new Action<bool>(on =>
            {
                if (button != null) button.SetActive(on);
                if (!on) Window.Close();
            }));
            go.SetActive(c.EditModeEnabled);
            Manual.Clear(); AutoJobs.Clear(); Status = "";
            Plugin.Log.LogInfo("Best Starter button added (copied from " + src.name + ")");
        }

        /// <summary>Never let an exception reach the game: its bug reporter treats it as fatal.</summary>
        internal static void Safe(Action a)
        {
            try { a(); }
            catch (Exception e) { Fail(e); }
        }

        internal static void Fail(Exception e)
        {
            Plugin.Log.LogError(e);
            Running = false;
            Status = "Error: " + e.GetType().Name + ": " + e.Message + "\nDetails are in BepInEx/LogOutput.log.";
            try { Window.Refresh(); } catch (Exception e2) { Plugin.Log.LogError(e2); }
        }

        // ---------- run ----------
        internal static void Run(bool team)
        {
            if (Running) return;
            var c = Ctl;
            var workers = c.Workers.ToList();
            var idx = team ? Enumerable.Range(0, workers.Count).ToList() : new List<int> { c.Selected };
            var model = GameData.Build();
            var goals = idx.Select(i => new SettlerGoal { Focus = new HashSet<int>(ManualFor(i)), AllowedIds = GameData.AllowedFor(workers[i]) }).ToList();
            int pool = model.MaxCreationPoints * workers.Count;
            if (!team) pool -= workers.Where((w, i) => i != c.Selected).Sum(w => w.Info.CreationPoints);
            var settings = Plugin.Settings;
            Running = true;
            progressStep = 0; progressTotal = 0;
            Status = "Optimizing...";
            Window.Refresh();
            var task = Task.Run(() =>
            {
                var opt = new Optimizer(model, settings)
                {
                    Progress = (s, n) => { Interlocked.Exchange(ref progressStep, s); Interlocked.Exchange(ref progressTotal, n); },
                };
                return team ? opt.OptimizeTeamAuto(goals, pool) : opt.OptimizeTeam(goals, pool);
            });
            Scheduler.Start(Finish(task, idx, goals, model, team), Fail);
        }

        static IEnumerator Finish(Task<List<Build>> task, List<int> idx, List<SettlerGoal> goals, GameModel model, bool team)
        {
            int shown = -1;
            while (!task.IsCompleted)
            {
                if (progressStep != shown && progressTotal > 0)
                {
                    shown = progressStep;
                    Status = $"Optimizing... trying job split {progressStep} of up to {progressTotal}";
                    Window.Refresh();
                }
                yield return null;
            }
            var c = Ctl;
            var builds = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            if (builds == null)
            {
                if (task.Exception != null) Plugin.Log.LogError(task.Exception);
                Running = false;
                Status = "No valid build fits the points available.";
                Window.Refresh();
                yield break;
            }
            Status = "Applying...";
            Window.Refresh();
            int orig = c.Selected;
            for (int k = 0; k < builds.Count; k++) yield return Apply(c, idx[k], builds[k]);
            c.SetSelected(orig);
            yield return 0.4f;

            for (int k = 0; k < builds.Count; k++)
                if (ManualFor(idx[k]).Count == 0) AutoJobs[idx[k]] = builds[k].Jobs.Count == GameModel.SkillCount ? new HashSet<int>() : builds[k].Jobs;
            var lines = new List<string>();
            for (int k = 0; k < builds.Count; k++)
            {
                var b = builds[k]; var h = c.Workers[idx[k]];
                string perks = b.Perks.Count > 0 ? string.Join(", ", b.Perks.Select(p => p.Name)) : "no perks";
                lines.Add($"<b>{h.GetFullName()}</b>  {b.Old} / {b.Recent}{(b.Pseudo != null ? " / " + b.Pseudo : "")}  <color=#c8b98a>({h.Info.CreationPoints} pts)</color>");
                lines.Add($"    {perks}");
                if (h.Info.CreationPoints != b.Points)
                    Plugin.Log.LogWarning($"{h.GetFullName()}: game counts {h.Info.CreationPoints} points, optimizer expected {b.Points}");
            }
            var gp = c.GetGroupPoints();
            lines.Add($"Team points: {gp.Min} / {gp.Max}" + (team ? "" : "  (other settlers kept)"));
            Status = string.Join("\n", lines);
            Running = false;
            Window.Refresh();
        }

        static IEnumerator Apply(CharacterEditController c, int i, Build b)
        {
            c.SetSelected(i);
            yield return 0.15f;
            // traits first, skill levels last (changing a trait shifts the current levels)
            c.SetBackground(b.Old.Id);
            c.SetBackstory(b.Recent.Id);
            c.SetPseudonym(b.Pseudo != null ? b.Pseudo.Id : "none");
            var want = new HashSet<string>(b.Perks.Select(p => p.Id));
            foreach (var p in c.SelectedHumanoid.Perks.ToList()) if (!want.Contains(p.GetID())) c.RemovePerk(p.GetID());
            foreach (var id in want) if (!c.SelectedHumanoid.Perks.Any(p => p.GetID() == id)) c.AddPerk(id);
            c.NotifyPerksChanged();
            c.NotifyHidePopupList();
            yield return 0.2f;
            for (int s = 0; s < GameModel.SkillCount; s++)
            {
                var ws = c.SelectedHumanoid.Skills.GetSkill((SkillType)(s + 1));
                if (ws != null) c.SetSkillLevel(ws, b.Levels[s]);
            }
            c.NotifyCharacterUpdated();
            c.NotifySelectedWorkerChanged();
            Window.Refresh();
            // the game recounts points 0.1 s later for whoever is selected then, so wait before switching
            yield return 0.35f;
        }
    }
}
