using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Daymark
{
    internal enum CompassStyle
    {
        Runes,
        Letters,
        Off,
    }

    /// <summary>
    /// A strip at the top of the screen whose compass points slide past as the camera turns, shown
    /// for a while after the player reads the sky in a world without a map. It hangs under the HUD
    /// root, which vanilla hides by moving it off screen, so it hides with the rest of the HUD.
    /// </summary>
    internal class CompassStrip
    {
        // Strip size in canvas pixels, how many degrees it spans and how far below the top edge it sits.
        private const float Width = 480f;
        private const float Height = 34f;
        private const float Span = 120f;
        private const float TopMargin = 28f;
        // Seconds to fade in and out, and how much of each half fades towards the edge.
        private const float FadeTime = 1f;
        private const float EdgeFade = 0.35f;
        private const float TickSpacing = 15f;
        private const float PointSize = 24f;
        private const string RuneFontName = "Valheim-Rune";

        // Weathered bone for the marks, a little darker than the HUD's cream so the strip does not
        // glare against the sky; north in ember red, as on a compass rose, so it is found at a glance.
        private static readonly Color s_markColor = new Color(0.82f, 0.75f, 0.62f);
        private static readonly Color s_northColor = new Color(0.86f, 0.36f, 0.24f);

        // The vanilla rune font draws Latin letters as Elder Futhark: n, a, s and w give ᚾ ᚨ ᛊ ᚹ,
        // the first runes of the Old Norse norðr, austr, suðr and vestr.
        private static readonly string[] s_runes = { "n", "a", "s", "w" };
        private static readonly string[] s_letterKeys =
        {
            "$daymark_compass_n", "$daymark_compass_e", "$daymark_compass_s", "$daymark_compass_w",
        };

        private GameObject m_root;
        private CanvasGroup m_group;
        private RectTransform[] m_marks;
        private CanvasRenderer[] m_renderers;
        private float[] m_bearings;
        private CompassStyle m_builtStyle;
        private float m_showTimer;
        private float m_alpha;

        public void Show()
        {
            m_showTimer = DaymarkPlugin.CompassTime.Value;
        }

        public void Update()
        {
            if (m_showTimer > 0f) m_showTimer -= Time.deltaTime;
            CompassStyle style = DaymarkPlugin.Compass.Value;
            bool wanted = m_showTimer > 0f && Game.m_noMap && style != CompassStyle.Off && Player.m_localPlayer;
            m_alpha = Mathf.MoveTowards(m_alpha, wanted ? 1f : 0f, Time.deltaTime / FadeTime);

            if (m_alpha <= 0f)
            {
                if (m_root) m_root.SetActive(false);
                return;
            }

            if (!m_root || m_builtStyle != style)
            {
                if (style == CompassStyle.Off || !Build(style)) return;
            }
            Camera camera = Utils.GetMainCamera();
            if (!camera) return;

            m_root.SetActive(true);
            m_group.alpha = m_alpha;
            // The camera never rolls, so its yaw is the facing even when looking straight up at the sun.
            float yaw = camera.transform.eulerAngles.y;
            float half = Width * 0.5f;
            for (int i = 0; i < m_marks.Length; i++)
            {
                float x = Mathf.DeltaAngle(yaw, m_bearings[i]) * (Width / Span);
                float edge = Mathf.Abs(x) / half;
                bool visible = edge < 1f;
                if (m_marks[i].gameObject.activeSelf != visible) m_marks[i].gameObject.SetActive(visible);
                if (!visible) continue;
                m_marks[i].anchoredPosition = new Vector2(x, m_marks[i].anchoredPosition.y);
                m_renderers[i].SetAlpha(Mathf.Clamp01((1f - edge) / EdgeFade));
            }
        }

        private bool Build(CompassStyle style)
        {
            if (m_root) Object.Destroy(m_root);
            m_root = null;
            if (!Hud.instance || !Hud.instance.m_rootObject || !Hud.instance.m_hoverName) return false;

            TMP_FontAsset font = Hud.instance.m_hoverName.font;
            string[] points = new string[4];
            for (int i = 0; i < 4; i++) points[i] = Localization.instance.Localize(s_letterKeys[i]);
            if (style == CompassStyle.Runes)
            {
                TMP_FontAsset runes = FindRuneFont();
                if (runes)
                {
                    font = runes;
                    points = s_runes;
                }
                else
                {
                    Jotunn.Logger.LogWarning($"Font '{RuneFontName}' not found; the compass shows letters instead.");
                }
            }

            m_root = new GameObject("DaymarkCompass", typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)m_root.transform;
            rect.SetParent(Hud.instance.m_rootObject.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(Width, Height);
            rect.anchoredPosition = new Vector2(0f, -TopMargin);
            m_group = m_root.GetComponent<CanvasGroup>();
            m_group.blocksRaycasts = false;
            m_group.interactable = false;

            int ticks = Mathf.RoundToInt(360f / TickSpacing);
            m_marks = new RectTransform[ticks];
            m_renderers = new CanvasRenderer[ticks];
            m_bearings = new float[ticks];
            for (int i = 0; i < ticks; i++)
            {
                float bearing = i * TickSpacing;
                m_bearings[i] = bearing;
                GameObject mark = bearing % 90f == 0f
                    ? MakeText(rect, points[(int)(bearing / 90f)], font, PointSize, bearing == 0f ? s_northColor : s_markColor)
                    : MakeTick(rect, bearing % 45f == 0f ? 10f : 5f);
                m_marks[i] = (RectTransform)mark.transform;
                m_renderers[i] = mark.GetComponent<CanvasRenderer>();
            }
            // The facing marker: a short line hanging from the strip's top edge at its centre.
            var centre = (RectTransform)MakeTick(rect, 8f).transform;
            centre.anchorMin = centre.anchorMax = centre.pivot = new Vector2(0.5f, 1f);
            centre.anchoredPosition = Vector2.zero;

            m_builtStyle = style;
            m_root.SetActive(false);
            return true;
        }

        private static GameObject MakeText(RectTransform parent, string value, TMP_FontAsset font, float size, Color color)
        {
            var go = new GameObject("Point", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(60f, Height);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = value;
            return go;
        }

        private static GameObject MakeTick(RectTransform parent, float length)
        {
            var go = new GameObject("Tick", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(2f, length);
            // No sprite draws a plain quad in the image colour.
            var image = go.AddComponent<Image>();
            image.color = s_markColor;
            image.raycastTarget = false;
            return go;
        }

        // The font runestones show their text in; it may not be loaded until a runestone is read.
        private static TMP_FontAsset FindRuneFont()
        {
            TMP_FontAsset font = FindLoadedRuneFont();
            if (font) return font;
            Resources.LoadAll<TMP_FontAsset>("");
            return FindLoadedRuneFont();
        }

        private static TMP_FontAsset FindLoadedRuneFont()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (font && font.name == RuneFontName) return font;
            return null;
        }
    }
}
