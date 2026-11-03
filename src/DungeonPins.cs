using System;
using HarmonyLib;
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
        // How far from the last outside position the door just used can be: the player steps into
        // its trigger, so a few metres at most.
        private const float EntranceReach = 15f;
        // The hammer (Mjolnir) icon of vanilla's map pin choices.
        private const Minimap.PinType PinIcon = Minimap.PinType.Icon2;

        // Private in vanilla: the publicized build compiles a direct call, but the game throws
        // MethodAccessException on it.
        private static readonly Func<Minimap, Vector3, float, bool> s_havePinInRange =
            AccessTools.MethodDelegate<Func<Minimap, Vector3, float, bool>>(
                AccessTools.Method(typeof(Minimap), "HavePinInRange"));

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
            if (s_havePinInRange(Minimap.instance, entrance, SameEntrance)) return;
            string label = EntranceName(entrance);
            if (label.Length == 0)
            {
                Location location = Location.GetLocation(entrance, false);
                if (location && !string.IsNullOrEmpty(location.m_discoverLabel)) label = location.m_discoverLabel;
            }
            label = Localization.instance.Localize(label);
            // The same call a Vegvisir's answer ends in for a boss: it adds the saved pin and shows
            // vanilla's "Pin added" message, without opening the map at it (showMap false).
            Minimap.instance.DiscoverLocation(entrance, PinIcon, label, false);
        }

        // The dungeon's name is the banner its door shows on the way in (Teleport.m_enterText,
        // "$location_forestcrypt" for Burial Chambers); the location's own m_discoverLabel is empty
        // for dungeons. The nearest named door that leads into an interior within EntranceReach of
        // the last outside position is the one just used. Runs once per entry, so a scene search is fine.
        private static string EntranceName(Vector3 entrance)
        {
            string name = "";
            float best = EntranceReach * EntranceReach;
            foreach (Teleport door in UnityEngine.Object.FindObjectsByType<Teleport>(FindObjectsSortMode.None))
            {
                if (!door || string.IsNullOrEmpty(door.m_enterText) || !door.m_targetPoint) continue;
                if (!Character.InInterior(door.m_targetPoint.transform.position)) continue;
                float distance = (door.transform.position - entrance).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                name = door.m_enterText;
            }
            return name;
        }
    }
}
