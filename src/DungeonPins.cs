using UnityEngine;

namespace Daymark
{
    /// <summary>
    /// Puts a pin on the map where the local player went into a dungeon, named after the place
    /// when the game names it. Dungeons sit 5 km up, so the entrance is the last position the
    /// player had outside before the jump, which is at its door.
    /// </summary>
    internal class DungeonPins
    {
        // Any pin this close to the entrance already marks it, the player's own included.
        private const float SameEntrance = 10f;
        // The hammer (Mjolnir) icon of vanilla's map pin choices.
        private const Minimap.PinType PinIcon = Minimap.PinType.Icon2;

        private Vector3 m_outside;
        private bool m_haveOutside;
        private bool m_wasInside;

        public void Update()
        {
            Player player = Player.m_localPlayer;
            if (!player || player.IsDead())
            {
                m_haveOutside = false;
                return;
            }

            bool inside = player.InInterior();
            if (!inside)
            {
                m_outside = player.transform.position;
                m_haveOutside = true;
            }
            else if (!m_wasInside && m_haveOutside)
            {
                Mark(m_outside);
            }
            m_wasInside = inside;
        }

        private static void Mark(Vector3 entrance)
        {
            // Worlds without a map get the pin too: the character keeps it, and it shows once the
            // map is turned on.
            if (!DaymarkPlugin.DungeonPins.Value || !Minimap.instance) return;
            if (Minimap.instance.HavePinInRange(entrance, SameEntrance)) return;
            Location location = Location.GetLocation(entrance, false);
            string label = location && !string.IsNullOrEmpty(location.m_discoverLabel)
                ? Localization.instance.Localize(location.m_discoverLabel)
                : "";
            // The same call a Vegvisir's answer ends in for a boss: it adds the saved pin and shows
            // vanilla's "Pin added" message, without opening the map at it (showMap false).
            Minimap.instance.DiscoverLocation(entrance, PinIcon, label, false);
        }
    }
}
