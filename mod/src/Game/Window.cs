// The Best Starter window: a skills × settlers grid. Built from a clone of the game's own editor popup
// (ListPopupView) so the frame, title bar, close button, background blur and Escape key behave natively;
// the popup's list and portrait are hidden and replaced by our content.
using System;
using System.Collections.Generic;
using System.Linq;
using BestStarter.Core;
using HarmonyLib;
using NSEipix.Base;
using NSEipix.View.UI;
using NSMedieval.Manager;
using NSMedieval.StatsSystem;
using NSMedieval.UI.ScenarioEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BestStarter.Game
{
    internal static class Window
    {
        const float LabelWidth = 170f, CellWidth = 112f, RowHeight = 28f, HeaderHeight = 44f;
        // job cells keep the button's normal look with coloured bold text; other cells are dimmed
        static readonly Color ManualText = new Color(1f, 0.78f, 0.38f);
        static readonly Color AutoText = new Color(0.56f, 0.9f, 0.6f);
        static readonly Color OffText = new Color(0.62f, 0.64f, 0.68f);
        static readonly Color JobTint = Color.white;
        static readonly Color OffTint = new Color(0.45f, 0.45f, 0.5f);

        static GameObject root, panel, blur, outClick;
        static TMP_Text title;
        static RectTransform content;
        static TextMeshProUGUI status;
        static readonly List<GameObject> headers = new List<GameObject>();
        static readonly List<GameObject[]> cells = new List<GameObject[]>();   // [skill][settler]
        static readonly List<GameObject> actionButtons = new List<GameObject>();
        static GameObject selectedOnlyButton;

        static bool IsOpen => root != null && panel != null && panel.activeSelf;

        // ---------- open / close ----------
        internal static void Open()
        {
            if (!Ensure()) { Plugin.Log.LogWarning("Best Starter: popup template not found"); return; }
            Build();
            panel.SetActive(true);
            if (blur != null) blur.SetActive(true);
            if (outClick != null) outClick.SetActive(true);
            root.transform.SetAsLastSibling();
            if (MonoSingleton<GlobalKeybindingManager>.IsInstantiated())
                MonoSingleton<GlobalKeybindingManager>.Instance.SubscribeToEscapeKey(Close, root);
            Refresh();
        }

        internal static void Close()
        {
            if (root == null) return;
            if (MonoSingleton<GlobalKeybindingManager>.IsInstantiated())
                MonoSingleton<GlobalKeybindingManager>.Instance.UnsubscribeFromEscapeKey(Close, root);
            if (panel != null) panel.SetActive(false);
            if (blur != null) blur.SetActive(false);
            if (outClick != null) outClick.SetActive(false);
        }

        /// <summary>Clone the editor's popup once per screen visit.</summary>
        static bool Ensure()
        {
            if (root != null && panel != null) return true;
            var tpl = Object.FindObjectOfType<ListPopupView>(true);
            if (tpl == null) return false;
            var tr = Traverse.Create(tpl);
            var tPanel = tr.Field("panel").GetValue<GameObject>();
            var tBlur = tr.Field("blur").GetValue<GameObject>();
            var tTitle = tr.Field("popupTitle").GetValue<TMP_Text>();
            var tClose = tr.Field("closeButton").GetValue<SoundButton>();
            var tOut = tr.Field("outClickCloseButton").GetValue<SoundButton>();
            var tHide = new List<Transform>();
            foreach (var f in new[] { "genericListGroup", "apparanceEditListGroup", "perksListGroup" })
                if (tr.Field(f).GetValue() is Component c && c != null) tHide.Add(c.transform);
            if (tr.Field("scrollViewPerks").GetValue<GameObject>() is GameObject sv && sv != null) tHide.Add(sv.transform);
            foreach (var ri in tPanel.GetComponentsInChildren<RawImage>(true)) tHide.Add(ri.transform);   // the portrait

            // clone under an inactive holder so none of its scripts wake up, then strip the popup's logic
            var holder = new GameObject("BestStarterHolder");
            holder.SetActive(false);
            var clone = Object.Instantiate(tpl.gameObject, holder.transform);
            clone.name = "BestStarterWindow";
            Object.DestroyImmediate(clone.GetComponent<ListPopupView>());
            Transform Map(Transform t) => t == null ? null : Find(clone.transform, PathOf(tpl.transform, t));
            panel = Map(tPanel?.transform)?.gameObject;
            blur = Map(tBlur?.transform)?.gameObject;
            title = Map(tTitle?.transform)?.GetComponent<TMP_Text>();
            var close = Map(tClose?.transform)?.GetComponent<SoundButton>();
            outClick = Map(tOut?.transform)?.gameObject;
            if (panel == null) { Object.Destroy(holder); return false; }

            var keep = new List<Transform> { title?.transform, close?.transform }.Where(t => t != null).ToList();
            HideContent(panel.transform, keep, tHide.Select(Map).Where(t => t != null).ToList());
            foreach (var b in new[] { close, outClick != null ? outClick.GetComponent<SoundButton>() : null })
                if (b != null) { b.onClick = new Button.ButtonClickedEvent(); b.onClick.AddListener(Close); }
            if (title != null) { foreach (var l in title.GetComponents<I2.Loc.Localize>()) Object.DestroyImmediate(l); title.text = "BEST STARTER"; }

            clone.transform.SetParent(tpl.transform.parent, false);
            Object.Destroy(holder);
            root = clone;
            root.SetActive(true);
            panel.SetActive(false);
            if (blur != null) blur.SetActive(false);
            if (outClick != null) outClick.SetActive(false);

            // our content area: below the title bar, inside the frame
            var go = new GameObject("BestStarterContent", typeof(RectTransform));
            go.transform.SetParent(panel.transform, false);
            content = (RectTransform)go.transform;
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = 8f; v.padding = new RectOffset(6, 6, 4, 6);
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            Plugin.Log.LogInfo("Best Starter window created");
            return true;
        }

        /// <summary>Hide every branch of the popup that holds its list or portrait, but keep the frame and title bar.</summary>
        static void HideContent(Transform node, List<Transform> keep, List<Transform> hide)
        {
            foreach (Transform child in node)
            {
                bool hasKeep = keep.Any(k => k.IsChildOf(child));
                bool hasHide = hide.Any(h => h.IsChildOf(child));
                if (hasKeep && hasHide) HideContent(child, keep, hide);
                else if (hasHide) child.gameObject.SetActive(false);
            }
        }

        static List<int> PathOf(Transform root, Transform t)
        {
            var path = new List<int>();
            for (var x = t; x != null && x != root; x = x.parent) path.Insert(0, x.GetSiblingIndex());
            return path;
        }

        static Transform Find(Transform root, List<int> path)
        {
            var x = root;
            foreach (int i in path) { if (i >= x.childCount) return null; x = x.GetChild(i); }
            return x;
        }

        // ---------- content ----------
        static void Build()
        {
            foreach (Transform c in content) Object.Destroy(c.gameObject);
            headers.Clear(); cells.Clear(); actionButtons.Clear();
            var ctl = EditorUi.Ctl;
            int n = ctl.Workers.Count;
            var tpl = EditorUi.ButtonTemplate;

            const float StatusHeight = 110f, IntroHeight = 46f, ActionsHeight = 42f, Gap = 8f;
            float gridHeight = HeaderHeight + GameModel.SkillCount * RowHeight + GameModel.SkillCount * 3f;
            var prt = (RectTransform)panel.transform;
            float top = 56f;
            if (title != null)
            {
                var corners = new Vector3[4];
                ((RectTransform)title.transform).GetWorldCorners(corners);
                float t = prt.rect.yMax - prt.InverseTransformPoint(corners[0]).y + 8f;
                if (t > 20f && t < 120f) top = t;
            }
            float width = Mathf.Max(LabelWidth + n * (CellWidth + 4f) + 40f, 760f);
            float height = top + 10f + IntroHeight + Gap + gridHeight + Gap + ActionsHeight + Gap + StatusHeight + 14f;
            Center(prt, width, height);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(14f, 14f); content.offsetMax = new Vector2(-14f, -top);
            var vl = content.GetComponent<VerticalLayoutGroup>(); if (vl != null) vl.spacing = Gap;

            var intro = Ui.Text(content, title,
                "Click a cell to choose that settler's jobs. Columns left on <color=#8fe69a>AUTO</color> get jobs " +
                "from the mod: it finds the split where the colony is best at every job. Click a name to go back to Auto.\n" +
                "Cell = current level.  <color=#ffc760><b>Gold</b></color> = your pick, <color=#8fe69a><b>green</b></color> = auto job, dim = not a job.  " +
                "++ Passionate, + Eager, - Unwilling, -- Resentful, x Incapable.", 14f);
            Ui.Size(intro.gameObject, -1, IntroHeight);

            // the grid: header row + one row per skill, packed tightly
            var gridGo = new GameObject("BestStarter_Grid", typeof(RectTransform));
            gridGo.transform.SetParent(content, false);
            var gv = gridGo.AddComponent<VerticalLayoutGroup>();
            gv.spacing = 3f; gv.childControlWidth = true; gv.childControlHeight = true;
            gv.childForceExpandWidth = true; gv.childForceExpandHeight = false;
            Ui.Size(gridGo, -1, gridHeight);
            var grid = gridGo.transform;
            // header row: settler names
            var head = Ui.Row(grid, HeaderHeight);
            Ui.Size(Ui.Text(head, title, "", 14f).gameObject, LabelWidth, HeaderHeight);
            for (int k = 0; k < n; k++)
            {
                int settler = k;
                var b = Ui.CloneButton(tpl, head, "", () => EditorUi.Safe(() => { EditorUi.ManualFor(settler).Clear(); Refresh(); }),
                    "Click to put this settler back on Auto.");
                Ui.FitText(b, 16f);
                Ui.Size(b, CellWidth, HeaderHeight, 1f);
                headers.Add(b);
            }
            // one row per skill
            for (int s = 0; s < GameModel.SkillCount; s++)
            {
                var row = Ui.Row(grid, RowHeight, 4f);
                var label = Ui.Text(row, title, GameData.SkillName(s), 16f, TextAlignmentOptions.MidlineLeft);
                Ui.Size(label.gameObject, LabelWidth, RowHeight);
                var line = new GameObject[n];
                for (int k = 0; k < n; k++)
                {
                    int settler = k, skill = s;
                    var b = Ui.CloneButton(tpl, row, "", () => EditorUi.Safe(() =>
                    {
                        var f = EditorUi.ManualFor(settler);
                        if (!f.Remove(skill)) f.Add(skill);
                        Refresh();
                    }), "Click to make this one of the settler's jobs.");
                    Ui.FitText(b, 16f);
                    Ui.Size(b, CellWidth, RowHeight, 1f);
                    line[k] = b;
                }
                cells.Add(line);
            }
            // actions
            var actions = Ui.Row(content, ActionsHeight, 8f);
            actionButtons.Add(Button(actions, "OPTIMIZE TEAM", () => EditorUi.Run(true),
                "Re-pick traits and skill levels for every settler, sharing the team's points."));
            selectedOnlyButton = Button(actions, "OPTIMIZE SELECTED", () => EditorUi.Run(false),
                "Only the settler selected in the editor. The others keep their points.");
            actionButtons.Add(selectedOnlyButton);
            actionButtons.Add(Button(actions, "ALL ON AUTO", () => { EditorUi.Manual.Clear(); Refresh(); },
                "Clear every pick; the mod assigns all jobs."));

            status = Ui.Text(content, title, "", 14f);
            Ui.Size(status.gameObject, -1, StatusHeight);
        }

        /// <summary>Size the frame and put it in the middle of the screen, scaled down if the screen is too short.</summary>
        static void Center(RectTransform prt, float width, float height)
        {
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(width, height);
            prt.localScale = Vector3.one;
            var canvas = prt.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) { prt.anchoredPosition = Vector2.zero; return; }
            var crt = (RectTransform)canvas.transform;
            prt.position = crt.TransformPoint(crt.rect.center);
            // available size in the panel's own units
            var parent = prt.parent as RectTransform;
            float ratio = parent != null && parent.lossyScale.y > 0 ? crt.lossyScale.y / parent.lossyScale.y : 1f;
            float availH = crt.rect.height * ratio * 0.94f, availW = crt.rect.width * ratio * 0.94f;
            float scale = Mathf.Min(1f, availH / height, availW / width);
            prt.localScale = new Vector3(scale, scale, 1f);
        }

        static GameObject Button(Transform parent, string text, Action onClick, string tooltip)
        {
            var b = Ui.CloneButton(EditorUi.ButtonTemplate, parent, text, () => EditorUi.Safe(onClick), tooltip);
            Ui.FitText(b, 18f);
            Ui.Size(b, -1, 42f, 1f);
            return b;
        }

        static string Marker(int pref)
        {
            switch (pref)
            {
                case 1: return " --";
                case 2: return " -";
                case 4: return " +";
                case 5: return " ++";
                case 6: return " x";
                default: return "";
            }
        }

        /// <summary>Redraw cells from the editor's live state (levels, interests) and the current picks.</summary>
        internal static void Refresh()
        {
            if (!IsOpen) return;
            var ctl = EditorUi.Ctl;
            var workers = ctl.Workers;
            if (headers.Count != workers.Count) { Build(); }
            for (int k = 0; k < workers.Count && k < headers.Count; k++)
            {
                var h = workers[k];
                var manual = EditorUi.ManualFor(k);
                string first = (h.GetFullName() ?? "").Split(' ').FirstOrDefault() ?? "";
                string mode = manual.Count > 0 ? "<color=#ffc760>MANUAL</color>" : "<color=#8fe69a>AUTO</color>";
                Ui.SetText(headers[k], (k == ctl.Selected ? "> " : "") + first + "\n<size=70%>" + mode + "</size>");
                EditorUi.AutoJobs.TryGetValue(k, out var auto);
                for (int s = 0; s < GameModel.SkillCount; s++)
                {
                    var cell = cells[s][k];
                    var ws = h.Skills.GetSkill((SkillType)(s + 1));
                    int level = ws != null ? ws.Level : 0, pref = ws != null ? ws.GetGoalPreferenceLevel() : 0;
                    bool isManual = manual.Contains(s), isAuto = manual.Count == 0 && auto != null && auto.Contains(s);
                    var col = isManual ? ManualText : isAuto ? AutoText : OffText;
                    string txt = level + Marker(pref);
                    Ui.SetText(cell, isManual || isAuto ? "<b>" + txt + "</b>" : txt);
                    Ui.SetTextColor(cell, col);
                    Ui.Tint(cell, isManual || isAuto ? JobTint : OffTint);
                }
            }
            string who = workers.Count > ctl.Selected ? (workers[ctl.Selected].GetFullName() ?? "").Split(' ').FirstOrDefault() : "";
            if (selectedOnlyButton != null) Ui.SetText(selectedOnlyButton, "OPTIMIZE " + who.ToUpperInvariant() + " ONLY");
            foreach (var b in actionButtons) b.GetComponent<SoundButton>().interactable = !EditorUi.Running;
            if (status != null) status.text = EditorUi.Status;
        }
    }
}
