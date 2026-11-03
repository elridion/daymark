using HarmonyLib;
using UnityEngine;

namespace Daymark
{
    /// <summary>
    /// Watches where the local player looks and, after a short steady gaze at the sun by day or up at
    /// the stars by night, shows a vague time of day in the centre of the screen, never during a
    /// fight; in worlds without a map it also brings up the compass. The sun's direction is rebuilt
    /// from EnvMan's own rotation formula instead of read from its light, because vanilla flips that
    /// light at dusk while the sun is still in the sky. The moon is not used: the skybox draws it
    /// somewhere other than opposite the sun, which is where an earlier version looked for it.
    /// </summary>
    internal class SkyGlance
    {
        // Seconds between gaze checks; looking at the sky needs no per-frame precision.
        private const float CheckInterval = 0.1f;
        // Seconds the gaze must stay away before the same body can tell the time again.
        private const float RearmTime = 1f;
        // Sine of the lowest elevation that counts as in the sky (about 1.5 degrees).
        private const float MinElevation = 0.025f;
        // Sine of how far up the player must look to be gazing at the stars (about 35 degrees).
        private const float StarElevation = 0.57f;
        // Far enough to catch mountains on the horizon, near enough to stay cheap.
        private const float OcclusionRange = 1500f;
        // Seconds without being targeted or attacking before the sky can be read again, so the
        // message never pops up mid-fight or in the lull between two swings.
        private const float CalmTime = 5f;
        // Room for every hit along one sight line through a forest.
        private const int MaxHits = 32;
        // Buffers' worth of forest a sight line may cross before it counts as blocked.
        private const int MaxPasses = 8;

        // Seconds since the player's last attack; protected on Humanoid, reset by StartAttack.
        private static readonly AccessTools.FieldRef<Humanoid, float> s_lastCombatTimer =
            AccessTools.FieldRefAccess<Humanoid, float>("m_lastCombatTimer");

        // EnvMan's sky time: 0.25 is sunrise, 0.5 midday, 0.75 sunset, 0 midnight. Vanilla rescales
        // the real clock so daylight takes 70% of the day, and this is the rescaled value the sky
        // shows, so the phases follow the visible sun rather than the raw world clock.
        private static readonly float[] s_sunPhaseEnds = { 0.30f, 0.40f, 0.47f, 0.53f, 0.60f, 0.70f, 0.75f };
        private static readonly string[] s_sunPhases =
        {
            "$daymark_sun_risen", "$daymark_sun_morning", "$daymark_sun_latemorning",
            "$daymark_sun_midday", "$daymark_sun_earlyafternoon", "$daymark_sun_lateafternoon",
            "$daymark_sun_setting",
        };

        // Night phases measured from sunset, wrapping past midnight (sky time + 1 after midnight).
        private static readonly float[] s_starPhaseEnds = { 0.82f, 0.95f, 1.05f, 1.18f, 1.25f };
        private static readonly string[] s_starPhases =
        {
            "$daymark_stars_rising", "$daymark_stars_night", "$daymark_stars_midnight",
            "$daymark_stars_sinking", "$daymark_stars_setting",
        };

        private readonly CompassStrip m_compass;
        private float m_checkTimer;
        private float m_gazeTimer;
        private float m_awayTimer;
        private float m_calmTimer;
        private bool m_told;
        private int m_occlusionMask;
        private readonly RaycastHit[] m_hits = new RaycastHit[MaxHits];

        public SkyGlance(CompassStrip compass)
        {
            m_compass = compass;
        }

        public void Update()
        {
            m_checkTimer += Time.deltaTime;
            if (m_checkTimer < CheckInterval) return;
            float dt = m_checkTimer;
            m_checkTimer = 0f;

            Player player = Player.m_localPlayer;
            m_calmTimer = player && InCombat(player) ? 0f : m_calmTimer + dt;

            string phase = m_calmTimer >= CalmTime ? GazedPhase(player) : null;
            if (phase == null)
            {
                m_gazeTimer = 0f;
                m_awayTimer += dt;
                if (m_awayTimer >= RearmTime) m_told = false;
                return;
            }

            m_awayTimer = 0f;
            if (m_told) return;
            m_gazeTimer += dt;
            if (m_gazeTimer < DaymarkPlugin.GazeTime.Value) return;

            m_told = true;
            // Worlds without a map get a compass to find the way.
            if (Game.m_noMap) m_compass.Show();
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, phase, log: false);
        }

        // The localization key for the time of day when the player is looking at the visible sun by
        // day or up into open night sky, otherwise null.
        private string GazedPhase(Player player)
        {
            if (!player || player.IsDead()) return null;
            if (!EnvMan.instance || !MessageHud.instance) return null;
            if (InventoryGui.IsVisible() || Minimap.IsOpen() || Menu.IsVisible() || TextInput.IsVisible()
                || Console.IsVisible() || Hud.InRadial()) return null;
            Camera camera = Utils.GetMainCamera();
            if (!camera) return null;

            EnvSetup env = EnvMan.instance.GetCurrentEnvironment();
            // Mistlands-style environments draw no sky at all.
            if (env == null || env.m_alwaysDark) return null;

            float time = EnvMan.instance.GetDayFraction();
            bool day = time >= 0.25f && time < 0.75f;
            Transform view = camera.transform;
            Vector3 toSky;
            if (day)
            {
                Quaternion sky = Quaternion.Euler(-90f + env.m_sunAngle, 0f, 0f) * Quaternion.Euler(0f, -90f, 0f)
                    * Quaternion.Euler(-90f + 360f * time, 0f, 0f);
                // The light shines from the sun, so the sun lies opposite its forward.
                toSky = -(sky * Vector3.forward);
                if (toSky.y < MinElevation) return null;
                if (Vector3.Angle(view.forward, toSky) > DaymarkPlugin.GazeAngle.Value) return null;
            }
            else
            {
                // Stars fill the whole night sky, so any steady look well upwards counts.
                toSky = view.forward;
                if (toSky.y < StarElevation) return null;
            }

            if (Blocked(view.position, toSky)) return null;

            return day ? Phase(time, s_sunPhaseEnds, s_sunPhases) : Phase(time < 0.25f ? time + 1f : time, s_starPhaseEnds, s_starPhases);
        }

        // Terrain, rocks, buildings and ships hide the sky; trees and bushes do not. A crown is one
        // solid collider, so it hid the sky under any canopy, and at a low sun a forest on the
        // horizon hid it too, while in the game the sky shows through the leaves.
        private bool Blocked(Vector3 from, Vector3 direction)
        {
            if (m_occlusionMask == 0)
                m_occlusionMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            float range = OcclusionRange;
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                // Triggers (spawn and effect areas, location bounds) are invisible and never hide the sky.
                int count = Physics.RaycastNonAlloc(from, direction, m_hits, range, m_occlusionMask, QueryTriggerInteraction.Ignore);
                float farthest = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (!Vegetation(m_hits[i].collider)) return true;
                    farthest = Mathf.Max(farthest, m_hits[i].distance);
                }
                if (count < m_hits.Length) return false;
                // A full buffer may have left a solid hit out behind all that forest: the hits come
                // unsorted, so carry on from just past the farthest one.
                float step = farthest + 0.05f;
                from += direction * step;
                range -= step;
                if (range <= 0f) return false;
            }
            return true;
        }

        private static bool Vegetation(Collider collider)
        {
            if (collider.GetComponentInParent<TreeBase>()) return true;
            var destructible = collider.GetComponentInParent<Destructible>();
            return destructible && destructible.m_destructibleType == DestructibleType.Tree;
        }

        // Vanilla's own combat signal: IsTargeted is what starts the combat music (a creature that
        // has noticed the player targets them within the last second). The player's own swings and
        // shots count too, so attacking a creature that has not noticed yet is also a fight.
        private static bool InCombat(Player player)
        {
            return player.IsTargeted() || s_lastCombatTimer(player) < CalmTime;
        }

        private static string Phase(float time, float[] ends, string[] phases)
        {
            for (int i = 0; i < ends.Length; i++)
                if (time < ends[i]) return phases[i];
            return phases[phases.Length - 1];
        }
    }
}
