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
    /// after the player reads the sky in a world without a map. Like dead reckoning it drifts: every
    /// <c>CompassDistance</c> travelled, one point's rune or letter turns into a plain
    /// stroke, the point the player travelled towards least going first, and with all four lost the
    /// strip fades. A portal, a dungeon or death loses them all at once. It hangs under the HUD root,
    /// which vanilla hides by moving it off screen, so it hides with the rest of the HUD.
    /// </summary>
    internal class CompassStrip
    {
        // Strip size in canvas pixels, how many degrees it spans and how far below the top edge it sits.
        private const float Width = 480f;
        private const float Height = 34f;
        private const float Span = 120f;
        private const float TopMargin = 28f;
        // Seconds to fade the strip in and out, and to turn a lost point into a stroke; how much of
        // each half fades towards the edge.
        private const float FadeTime = 1f;
        private const float PointFadeTime = 3f;
        private const float EdgeFade = 0.35f;
        private const float TickSpacing = 15f;
        private const float PointSize = 24f;
        // A lost point becomes the same stroke as the intercardinals.
        private const float StrokeLength = 10f;
        // A jump further than this in one frame is a teleport, which no reckoning survives.
        private const float TeleportJump = 100f;
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
        // Which point a mark belongs to (0-3 north, east, south, west) and whether it is the point's
        // label or the stroke that replaces it; -1 for the ticks between points.
        private int[] m_markPoint;
        private bool[] m_markIsStroke;
        private CompassStyle m_builtStyle;
        private float m_alpha;

        private readonly bool[] m_known = new bool[4];
        // How much of each point's label still shows, fading towards its stroke when lost.
        private readonly float[] m_labelAlpha = new float[4];
        // Metres travelled towards each point since the reading; the least travelled is lost first.
        private readonly float[] m_travelled = new float[4];
        private float m_drift;
        private int m_lost = 4;
        private Vector3 m_lastPosition;
        private bool m_tracking;

        public void Show()
        {
            for (int i = 0; i < 4; i++)
            {
                m_known[i] = true;
                m_travelled[i] = 0f;
                // A hidden strip comes back with its labels; a showing one turns its strokes back.
                if (m_alpha <= 0f) m_labelAlpha[i] = 1f;
            }
            m_drift = 0f;
            m_lost = 0;
            m_tracking = false;
        }

        public void Update()
        {
            Player player = Player.m_localPlayer;
            if (m_lost < 4) Reckon(player);
            for (int i = 0; i < 4; i++)
                m_labelAlpha[i] = Mathf.MoveTowards(m_labelAlpha[i], m_known[i] ? 1f : 0f, Time.deltaTime / PointFadeTime);

            CompassStyle style = DaymarkPlugin.Compass.Value;
            // With all points lost, the last label still turns into its stroke before the strip goes.
            bool wanted = (m_lost < 4 || AnyLabel()) && Game.m_noMap && style != CompassStyle.Off && player;
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
                float weight = 1f;
                int point = m_markPoint[i];
                if (point >= 0) weight = m_markIsStroke[i] ? 1f - m_labelAlpha[point] : m_labelAlpha[point];
                float x = Mathf.DeltaAngle(yaw, m_bearings[i]) * (Width / Span);
                float edge = Mathf.Abs(x) / half;
                bool visible = edge < 1f && weight > 0f;
                if (m_marks[i].gameObject.activeSelf != visible) m_marks[i].gameObject.SetActive(visible);
                if (!visible) continue;
                m_marks[i].anchoredPosition = new Vector2(x, m_marks[i].anchoredPosition.y);
                m_renderers[i].SetAlpha(Mathf.Clamp01((1f - edge) / EdgeFade) * weight);
            }
        }

        // Adds up the distance travelled since the reading and loses a point every CompassDistance;
        // anything that takes the player away at once loses them all.
        private void Reckon(Player player)
        {
            if (!player || player.IsDead() || player.InInterior() || player.IsTeleporting())
            {
                LoseAll();
                return;
            }
            Vector3 position = player.transform.position;
            if (m_tracking)
            {
                Vector3 step = position - m_lastPosition;
                step.y = 0f;
                float distance = step.magnitude;
                if (distance > TeleportJump)
                {
                    LoseAll();
                    return;
                }
                if (distance > 0f)
                {
                    m_drift += distance;
                    // North is +Z and east +X, as on vanilla's map.
                    float bearing = Mathf.Atan2(step.x, step.z) * Mathf.Rad2Deg;
                    m_travelled[((Mathf.RoundToInt(bearing / 90f) % 4) + 4) % 4] += distance;
                }
            }
            m_lastPosition = position;
            m_tracking = true;

            float perPoint = DaymarkPlugin.CompassDistance.Value;
            while (m_lost < 4 && m_drift >= perPoint * (m_lost + 1))
                LoseOne();
        }

        // The point travelled towards least goes; on a tie (no travel that way at all) the one
        // furthest from where the camera faces, so the points in view are kept longest.
        private void LoseOne()
        {
            Camera camera = Utils.GetMainCamera();
            float yaw = camera ? camera.transform.eulerAngles.y : 0f;
            int pick = -1;
            for (int i = 0; i < 4; i++)
            {
                if (!m_known[i]) continue;
                if (pick < 0 || m_travelled[i] < m_travelled[pick]
                    || (m_travelled[i] == m_travelled[pick]
                        && Mathf.Abs(Mathf.DeltaAngle(yaw, i * 90f)) > Mathf.Abs(Mathf.DeltaAngle(yaw, pick * 90f))))
                    pick = i;
            }
            if (pick >= 0) m_known[pick] = false;
            m_lost++;
        }

        private bool AnyLabel()
        {
            for (int i = 0; i < 4; i++)
                if (m_labelAlpha[i] > 0f) return true;
            return false;
        }

        private void LoseAll()
        {
            for (int i = 0; i < 4; i++) m_known[i] = false;
            m_lost = 4;
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

            // One mark per tick, plus a stroke under each of the four points for when it is lost.
            int ticks = Mathf.RoundToInt(360f / TickSpacing);
            int count = ticks + 4;
            m_marks = new RectTransform[count];
            m_renderers = new CanvasRenderer[count];
            m_bearings = new float[count];
            m_markPoint = new int[count];
            m_markIsStroke = new bool[count];
            for (int i = 0; i < ticks; i++)
            {
                float bearing = i * TickSpacing;
                bool point = bearing % 90f == 0f;
                GameObject mark = point
                    ? MakeText(rect, points[(int)(bearing / 90f)], font, PointSize, bearing == 0f ? s_northColor : s_markColor)
                    : MakeTick(rect, bearing % 45f == 0f ? StrokeLength : 5f);
                AddMark(i, mark, bearing, point ? (int)(bearing / 90f) : -1, false);
            }
            for (int p = 0; p < 4; p++)
                AddMark(ticks + p, MakeTick(rect, StrokeLength), p * 90f, p, true);
            // The facing marker: a short line hanging from the strip's top edge at its centre.
            var centre = (RectTransform)MakeTick(rect, 8f).transform;
            centre.anchorMin = centre.anchorMax = centre.pivot = new Vector2(0.5f, 1f);
            centre.anchoredPosition = Vector2.zero;

            m_builtStyle = style;
            m_root.SetActive(false);
            return true;
        }

        private void AddMark(int index, GameObject mark, float bearing, int point, bool stroke)
        {
            m_marks[index] = (RectTransform)mark.transform;
            m_renderers[index] = mark.GetComponent<CanvasRenderer>();
            m_bearings[index] = bearing;
            m_markPoint[index] = point;
            m_markIsStroke[index] = stroke;
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
