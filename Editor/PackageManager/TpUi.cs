using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwiceSDK.PackageManager
{
    /// <summary>Small UI Toolkit builders so the views read like markup.</summary>
    public static class TpUi
    {
        public static VisualElement El(string classes, params VisualElement[] children)
        {
            var e = new VisualElement();
            AddClasses(e, classes);
            foreach (var c in children) if (c != null) e.Add(c);
            return e;
        }

        public static VisualElement Row(string classes = "", params VisualElement[] children)
        {
            var e = El("tp-row " + classes, children);
            return e;
        }

        public static Label L(string text, string classes = "")
        {
            var l = new Label(text ?? "");
            AddClasses(l, classes);
            return l;
        }

        public static Button Btn(string text, Action onClick, string classes = "tp-btn")
        {
            var b = new Button(() => { if (onClick != null) onClick(); }) { text = text };
            AddClasses(b, classes);
            return b;
        }

        public static Label Chip(string text, string variant = "")
        {
            return L(text, "tp-chip" + (string.IsNullOrEmpty(variant) ? "" : " tp-chip--" + variant));
        }

        public static VisualElement Space(float h)
        {
            var e = new VisualElement();
            e.style.height = h;
            e.style.flexShrink = 0;
            return e;
        }

        public static VisualElement Flex()
        {
            var e = new VisualElement();
            e.style.flexGrow = 1;
            return e;
        }

        public static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
        }

        public static VisualElement Section(string title, params VisualElement[] children)
        {
            var s = El("tp-section");
            s.Add(L(title.ToUpperInvariant(), "tp-section-title"));
            foreach (var c in children) if (c != null) s.Add(c);
            return s;
        }

        public static VisualElement KV(string key, string value)
        {
            return El("tp-kv", L(key, "tp-kv-key"), L(string.IsNullOrEmpty(value) ? "—" : value, "tp-kv-val"));
        }

        public static TextField Field(string label, string value, Action<string> onChange, bool multiline = false)
        {
            var t = new TextField(label) { value = value ?? "", multiline = multiline };
            t.AddToClassList("tp-field");
            if (multiline) { t.style.minHeight = 60; t.style.whiteSpace = WhiteSpace.Normal; }
            t.RegisterValueChangedCallback(e => { if (onChange != null) onChange(e.newValue); });
            return t;
        }

        public static DropdownField Dropdown(string label, List<string> choices, string value, Action<string> onChange)
        {
            int idx = Math.Max(0, choices.IndexOf(value ?? ""));
            var d = new DropdownField(label, choices, choices.Count > 0 ? idx : -1);
            d.AddToClassList("tp-field");
            d.RegisterValueChangedCallback(e => { if (onChange != null) onChange(e.newValue); });
            return d;
        }

        /// <summary>Checkbox with its text on the right (no wide label column).</summary>
        public static Toggle Check(string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle { value = value, text = label };
            t.AddToClassList("tp-check");
            t.RegisterValueChangedCallback(e => { if (onChange != null) onChange(e.newValue); });
            return t;
        }

        /// <summary>Button showing one of the editor's built-in icons (skin-aware).</summary>
        public static Button IconBtn(string icon, string tooltip, Action onClick)
        {
            var b = Btn("", onClick, "tp-icon-btn");
            var c = EditorGUIUtility.IconContent((EditorGUIUtility.isProSkin ? "d_" : "") + icon);
            if (c == null || c.image == null) c = EditorGUIUtility.IconContent(icon);
            if (c != null && c.image != null)
            {
                var img = new Image { image = c.image, scaleMode = ScaleMode.ScaleToFit };
                img.AddToClassList("tp-icon");
                b.Add(img);
            }
            else b.text = icon.Substring(0, 1);
            b.tooltip = tooltip;
            return b;
        }

        static readonly Color[] Tiles =
        {
            new Color32(0x3b, 0x5b, 0xdb, 255), new Color32(0x0c, 0x85, 0x99, 255), new Color32(0x2b, 0x8a, 0x3e, 255),
            new Color32(0xc2, 0x25, 0x5c, 0xff), new Color32(0x86, 0x2e, 0x9c, 255), new Color32(0xe6, 0x77, 0x00, 255),
            new Color32(0x5f, 0x3d, 0xc4, 255), new Color32(0x09, 0x92, 0x68, 255), new Color32(0xd6, 0x33, 0x6c, 255),
        };

        /// <summary>Stable color per package so cards without art still tell apart at a glance.</summary>
        public static Color TileColor(string key)
        {
            int h = 17;
            foreach (char ch in key ?? "") h = h * 31 + ch;
            return Tiles[(h & 0x7fffffff) % Tiles.Length];
        }

        static string Initials(string name)
        {
            var words = new List<string>();
            foreach (var w in (name ?? "").Split(' ', '-', '_', ':', '|', '(', ')'))
                if (w.Length > 0 && char.IsLetterOrDigit(w[0])) words.Add(w);
            if (words.Count == 0) return "?";
            string a = words[0].Substring(0, 1);
            string b = words.Count > 1 ? words[1].Substring(0, 1) : (words[0].Length > 1 ? words[0].Substring(1, 1) : "");
            return (a + b).ToUpperInvariant();
        }

        /// <summary>Package image as background, or a letter placeholder until/unless it loads.</summary>
        public static void SetImage(VisualElement target, TpPackage p)
        {
            target.Clear();
            var tex = TpCatalog.Image(p);
            if (tex != null)
            {
                target.style.backgroundImage = new StyleBackground(tex);
                return;
            }
            target.style.backgroundImage = StyleKeyword.None;
            string n = p != null ? p.DisplayName : "?";
            var c = TileColor(p != null ? p.slug : n);
            target.style.backgroundColor = new Color(c.r, c.g, c.b, 0.85f);
            target.Add(L(Initials(n), "tp-placeholder"));
        }

        public static string Plural(int n, string word) { return n + " " + word; }
    }
}
