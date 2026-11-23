// Small helpers that build UI from the game's own components, so text and buttons look native.
using System;
using System.Collections.Generic;
using NSEipix.View.UI;
using NSMedieval.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BestStarter.Game
{
    internal static class Ui
    {
        internal static GameObject CloneButton(SoundButton src, Transform parent, string text, Action onClick, string tooltip = null)
        {
            var go = Object.Instantiate(src.gameObject, parent);
            go.name = "BestStarter_" + text;
            foreach (var l in go.GetComponentsInChildren<I2.Loc.Localize>(true)) Object.DestroyImmediate(l);
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) { t.richText = true; t.text = text; }
            foreach (var tip in go.GetComponentsInChildren<LocalizedTextTooltipView>(true))
            {
                tip.TextKeys = new List<string>();
                tip.SetNonLocalizedText(tooltip ?? text);
            }
            var sb = go.GetComponent<SoundButton>();
            sb.onClick = new Button.ButtonClickedEvent();
            sb.onClick.AddListener(() => onClick());
            sb.interactable = true;
            go.SetActive(true);
            return go;
        }

        internal static void SetText(GameObject button, string text)
        {
            if (button == null) return;
            foreach (var t in button.GetComponentsInChildren<TMP_Text>(true)) t.text = text;
        }

        internal static void SetTooltip(GameObject button, string tooltip)
        {
            foreach (var tip in button.GetComponentsInChildren<LocalizedTextTooltipView>(true)) tip.SetNonLocalizedText(tooltip);
        }

        internal static void FitText(GameObject button, float max, float min = 10f)
        {
            foreach (var t in button.GetComponentsInChildren<TMP_Text>(true))
            {
                t.enableAutoSizing = true; t.fontSizeMax = max; t.fontSizeMin = min;
                t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;
                t.alignment = TextAlignmentOptions.Center;
            }
        }

        internal static TextMeshProUGUI Text(Transform parent, TMP_Text style, string text, float size,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = new GameObject("BestStarter_Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (style != null) { t.font = style.font; t.fontSharedMaterial = style.fontSharedMaterial; t.color = style.color; }
            t.fontSize = size; t.alignment = align; t.richText = true; t.raycastTarget = false;
            t.enableWordWrapping = true; t.text = text;
            return t;
        }

        internal static RectTransform Row(Transform parent, float height, float spacing = 4f)
        {
            var go = new GameObject("BestStarter_Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            Size(go, -1, height);
            return (RectTransform)go.transform;
        }

        internal static LayoutElement Size(GameObject go, float width, float height, float flexWidth = 0f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; }
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; }
            le.flexibleWidth = flexWidth;
            return le;
        }

        internal static void SetTextColor(GameObject button, Color c)
        {
            foreach (var t in button.GetComponentsInChildren<TMP_Text>(true)) t.color = c;
        }

        internal static void Tint(GameObject button, Color c)
        {
            var img = button.GetComponent<Image>();
            if (img != null) img.color = c;
        }
    }
}
