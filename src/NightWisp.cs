using UnityEngine;

namespace Daymark
{
    /// <summary>
    /// Keeps the wisp (<see cref="WispGuide"/>) with the player all night, rested or not: it
    /// appears as night falls or on entering a world at night, and comes back after a death.
    /// </summary>
    internal class NightWisp
    {
        // Seconds between attempts to bring the wisp back and checks for a new bed, so the bed and
        // light ball are not looked up every frame.
        private const float RetryInterval = 5f;

        private readonly WorldDirectory m_directory = new WorldDirectory();
        private readonly WispGuide m_guide;
        private float m_retryTimer = RetryInterval;

        public NightWisp()
        {
            m_guide = new WispGuide(m_directory);
        }

        public void Update()
        {
            // Runs on a dedicated server too, which has no player and answers the clients' requests.
            m_directory.Update();
            m_retryTimer += Time.deltaTime;
            Player player = Player.m_localPlayer;
            bool wanted = player && !player.IsDead() && EnvMan.instance && EnvMan.IsNight() && Game.m_noMap
                && DaymarkPlugin.Wisp.Value;
            if (!DaymarkPlugin.Wisp.Value) m_guide.Dismiss();
            if (wanted && m_retryTimer >= RetryInterval)
            {
                m_retryTimer = 0f;
                if (!m_guide.Active) Start(player);
                else FollowBed(player);
            }
            m_guide.Update();
        }

        private void Start(Player player)
        {
            if (TryGetBed(out Vector3 bed)) m_guide.Start(player, bed);
        }

        // Sleeping in a new bed during the night moves the goal there.
        private void FollowBed(Player player)
        {
            if (TryGetBed(out Vector3 bed) && (bed - m_guide.Goal).sqrMagnitude > 1f) m_guide.SetGoal(player, bed);
        }

        private static bool TryGetBed(out Vector3 bed)
        {
            bed = Vector3.zero;
            PlayerProfile profile = Game.instance ? Game.instance.GetPlayerProfile() : null;
            if (profile == null || !profile.HaveCustomSpawnPoint()) return false;
            bed = profile.GetCustomSpawnPoint();
            return true;
        }
    }
}
