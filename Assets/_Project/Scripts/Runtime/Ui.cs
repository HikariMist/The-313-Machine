using System.Collections.Generic;
using UnityEngine;

namespace HikariForge.M313
{
    /// <summary>
    /// Immediate-mode drawing helpers on a virtual 1920x1080 canvas, letterboxed to any resolution.
    /// Text is laid out at the real pixel size (not matrix-scaled) so it stays sharp.
    /// </summary>
    public static class Ui
    {
        public const float VW = 1920f, VH = 1080f;
        public static float Scale { get; private set; } = 1f;
        public static Vector2 Offset { get; private set; }

        public static readonly Color Void = Hex("07080b"), Panel = Hex("10131a"), Steel = Hex("1b1f28"), Edge = Hex("2a303c");
        public static readonly Color Fg = Hex("d8dce3"), Dim = Hex("8b919c"), Sodium = Hex("e9b04c"), Emerald = Hex("34d391");
        public static readonly Color Violet = Hex("9b6dff"), Blood = Hex("d4484c"), Paper = Hex("e8e0cc"), Ink = Hex("23201b");
        public static readonly Color LcdRed = Hex("ff3434"), Bone = Hex("ddd3c4");

        public enum F { Body, Italic, Semi, Label, LabelMed, Lcd, Type, Hand }

        private static readonly Dictionary<F, Font> Fonts = new Dictionary<F, Font>();
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<long, GUIStyle> Styles = new Dictionary<long, GUIStyle>();
        private static Texture2D _white;
        private static float _styleScale = -1;

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }

        public static void Begin()
        {
            Scale = Mathf.Min(Screen.width / VW, Screen.height / VH);
            Offset = new Vector2((Screen.width - VW * Scale) / 2f, (Screen.height - VH * Scale) / 2f);
            if (!Mathf.Approximately(Scale, _styleScale)) { Styles.Clear(); _styleScale = Scale; }
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                LoadFonts();
            }
            // Letterbox bars.
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _white);
            GUI.color = Color.white;
        }

        private static void LoadFonts()
        {
            Fonts[F.Body] = Resources.Load<Font>("Fonts/Barlow-Regular");
            Fonts[F.Italic] = Resources.Load<Font>("Fonts/Barlow-Italic");
            Fonts[F.Semi] = Resources.Load<Font>("Fonts/Barlow-SemiBold");
            Fonts[F.Label] = Resources.Load<Font>("Fonts/BarlowCondensed-Bold");
            Fonts[F.LabelMed] = Resources.Load<Font>("Fonts/BarlowCondensed-Medium");
            Fonts[F.Lcd] = Resources.Load<Font>("Fonts/VT323-Regular");
            Fonts[F.Type] = Resources.Load<Font>("Fonts/SpecialElite-Regular");
            Fonts[F.Hand] = Resources.Load<Font>("Fonts/ReenieBeanie-Regular");
        }

        public static Rect R(float x, float y, float w, float h) =>
            new Rect(Offset.x + x * Scale, Offset.y + y * Scale, w * Scale, h * Scale);

        public static Vector2 Mouse => (Event.current.mousePosition - Offset) / Scale;

        public static bool Hover(float x, float y, float w, float h)
        {
            var m = Mouse;
            return m.x >= x && m.x <= x + w && m.y >= y && m.y <= y + h;
        }

        public static Texture2D Tex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Textures.TryGetValue(path, out var t)) return t;
            t = Resources.Load<Texture2D>(path);
            Textures[path] = t;
            return t;
        }

        public static void Fill(float x, float y, float w, float h, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(R(x, y, w, h), _white);
            GUI.color = old;
        }

        public static void Frame(float x, float y, float w, float h, Color c, float t = 1.5f)
        {
            Fill(x, y, w, t, c); Fill(x, y + h - t, w, t, c);
            Fill(x, y, t, h, c); Fill(x + w - t, y, t, h, c);
        }

        public static void Box(float x, float y, float w, float h, Color bg, Color edge)
        {
            Fill(x, y, w, h, bg);
            Frame(x, y, w, h, edge);
        }

        public static void Image(Texture tex, float x, float y, float w, float h, ScaleMode mode = ScaleMode.ScaleToFit, float alpha = 1f, Color? tint = null)
        {
            if (tex == null) return;
            var old = GUI.color;
            var c = tint ?? Color.white;
            c.a *= alpha;
            GUI.color = c;
            GUI.DrawTexture(R(x, y, w, h), tex, mode, true);
            GUI.color = old;
        }

        public static GUIStyle Style(F font, int size, Color color, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = true)
        {
            long key = ((long)font << 40) | ((long)size << 28) | ((long)anchor << 24) | ((wrap ? 1L : 0L) << 23) | (uint)(color.GetHashCode() & 0x7FFFFF);
            if (Styles.TryGetValue(key, out var s)) return s;
            s = new GUIStyle(GUI.skin.label)
            {
                font = Fonts.TryGetValue(font, out var f) ? f : null,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(size * Scale)),
                alignment = anchor,
                wordWrap = wrap,
                richText = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            s.normal.textColor = color;
            s.hover.textColor = color;
            Styles[key] = s;
            return s;
        }

        public static void Text(string text, float x, float y, float w, float h, F font = F.Body, int size = 22, Color? color = null, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUI.Label(R(x, y, w, h), text, Style(font, size, color ?? Fg, anchor, wrap));
        }

        /// <summary>Height (virtual px) that wrapped text needs at width w.</summary>
        public static float TextHeight(string text, float w, F font = F.Body, int size = 22)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return Style(font, size, Fg).CalcHeight(new GUIContent(text), w * Scale) / Scale;
        }

        public enum B { Normal, Primary, Danger, Ghost, Decision, On }

        public static bool Button(string label, float x, float y, float w, float h, B kind = B.Normal, bool enabled = true, int size = 20)
        {
            bool hover = enabled && Hover(x, y, w, h);
            Color bg = kind == B.Primary ? Hex("123527") : kind == B.Danger ? Hex("3a1517") : kind == B.Ghost ? new Color(0, 0, 0, 0.25f) : Steel;
            Color edge = kind == B.Primary ? Emerald : kind == B.Danger ? Blood : kind == B.Decision ? Violet : kind == B.On ? Sodium : Edge;
            if (hover) { bg = Color.Lerp(bg, Color.white, 0.07f); edge = Emerald; }
            if (!enabled) { bg.a *= 0.4f; edge.a *= 0.4f; }
            Box(x, y, w, h, bg, edge);
            Color fg = kind == B.Primary ? Hex("c9ffe6") : kind == B.Danger ? Hex("ffd6d6") : kind == B.Decision ? Hex("e2d4ff") : kind == B.On ? Sodium : Fg;
            if (!enabled) fg.a = 0.4f;
            Text(label.ToUpperInvariant(), x + 10, y, w - 20, h, F.Label, size, fg, TextAnchor.MiddleCenter, false);
            bool clicked = enabled && GUI.Button(R(x, y, w, h), GUIContent.none, GUIStyle.none);
            if (clicked) Sound?.Invoke("ui_click");
            return clicked;
        }

        /// <summary>Wrapped button that grows to fit its text; returns clicked and the height used.</summary>
        public static bool WrapButton(string label, float x, float y, float w, out float h, B kind = B.Normal, int size = 19)
        {
            h = Mathf.Max(40, TextHeight(label, w - 24, F.Semi, size) + 16);
            bool hover = Hover(x, y, w, h);
            Color edge = kind == B.Decision ? Violet : hover ? Emerald : Edge;
            Box(x, y, w, h, hover ? Hex("202631") : Steel, edge);
            Text(label, x + 12, y + 8, w - 24, h - 8, F.Semi, size, kind == B.Decision ? Hex("e2d4ff") : Fg);
            bool clicked = GUI.Button(R(x, y, w, h), GUIContent.none, GUIStyle.none);
            if (clicked) Sound?.Invoke("ui_click");
            return clicked;
        }

        public static System.Action<string> Sound;

        public static void Meter(float x, float y, float w, float h, float t, Color a, Color b)
        {
            Fill(x, y, w, h, Steel);
            Fill(x, y, w * Mathf.Clamp01(t), h, Color.Lerp(a, b, t));
        }

        public static void Vignette(float strength)
        {
            var tex = Tex("Art/UI/vignette");
            if (tex != null) Image(tex, 0, 0, VW, VH, ScaleMode.StretchToFill, strength);
        }

        public static string Money(int cents) => $"${cents / 100}.{Mathf.Abs(cents % 100):00}";
    }
}
