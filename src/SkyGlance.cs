using HarmonyLib;
using UnityEngine;

namespace Daymark
{
    /// <summary>
    /// Watches where the local player looks and, after a short steady gaze at the sun or the moon,
    /// shows a vague time of day in the centre of the screen, never during a fight. The sun's
    /// direction is rebuilt from EnvMan's own rotation formula instead of read from its light,
    /// because vanilla flips that light to the moon's side at dusk while the sun is still in the sky.
    /// </summary>
    internal class SkyGlance
    {
        // Seconds between gaze checks; looking at the sky needs no per-frame precision.
        private const float CheckInterval = 0.1f;
        // Seconds the gaze must stay away before the same body can tell the time again.
        private const float RearmTime = 1f;
        // Sine of the lowest elevation that counts as in the sky (about 1.5 degrees).
        private const float MinElevation = 0.025f;
        // Far enough to catch mountains on the horizon, near enough to stay cheap.
        private const float OcclusionRange = 1500f;
        // Seconds without being targeted or attacking before the sky can be read again, so the
        // message never pops up mid-fight or in the lull between two swings.
        private const float CalmTime = 5f;

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
        private static readonly float[] s_moonPhaseEnds = { 0.82f, 0.95f, 1.05f, 1.18f, 1.25f };
        private static readonly string[] s_moonPhases =
        {
            "$daymark_moon_rising", "$daymark_moon_night", "$daymark_moon_midnight",
            "$daymark_moon_sinking", "$daymark_moon_setting",
        };

        private float m_checkTimer;
        private float m_gazeTimer;
        private float m_awayTimer;
        private float m_calmTimer;
        private bool m_told;
        private int m_occlusionMask;

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
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, phase, log: false);
        }

        // The localization key for the time of day when the player is looking at a visible sun or
        // moon, otherwise null.
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
            Quaternion sky = Quaternion.Euler(-90f + env.m_sunAngle, 0f, 0f) * Quaternion.Euler(0f, -90f, 0f)
                * Quaternion.Euler(-90f + 360f * time, 0f, 0f);
            // The light shines from the sun, so the sun lies opposite its forward; the moon is the
            // flipped light vanilla uses at night, straight across the sky from the sun.
            Vector3 toBody = day ? -(sky * Vector3.forward) : sky * Vector3.forward;
            if (toBody.y < MinElevation) return null;

            Transform view = camera.transform;
            if (Vector3.Angle(view.forward, toBody) > DaymarkPlugin.GazeAngle.Value) return null;

            if (m_occlusionMask == 0)
                m_occlusionMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            if (Physics.Raycast(view.position, toBody, OcclusionRange, m_occlusionMask)) return null;

            return day ? Phase(time, s_sunPhaseEnds, s_sunPhases) : Phase(time < 0.25f ? time + 1f : time, s_moonPhaseEnds, s_moonPhases);
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
