using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Daymark
{
    /// <summary>
    /// Holding Use on a big ore deposit pins it on the map, named after its metal. Deposits have no
    /// vanilla Use action, so the held key is free; vanilla swaps an untouched deposit for its
    /// fractured copy at the same position on the first hit, so both prefabs of a deposit are listed
    /// and a pin made from either is the same pin.
    /// </summary>
    internal class DepositPins
    {
        // Seconds Use must be held on a deposit, long enough not to fire on a stray tap.
        private const float HoldTime = 0.6f;
        // The dot icon of vanilla's map pin choices.
        private const Minimap.PinType PinIcon = Minimap.PinType.Icon3;

        // Big deposits mined node by node that yield metal, stand in the open world and stay put
        // (the rule agreed with the user, 2026-11-03), each with the vanilla name of its metal.
        // Left out: tin and obsidian (single small rocks, emptied in one visit), the flametal vein
        // (rides a Lavaiathan that sinks), the iron vein (console only), muddy scrap piles (crypt
        // ones are in dungeons, buried swamp ones are dug out once and drop nothing for certain).
        private static readonly Dictionary<string, string> s_deposits = new Dictionary<string, string>
        {
            { "rock4_copper", "$item_copper" },
            { "rock4_copper_frac", "$item_copper" },
            { "silvervein", "$item_silver" },
            { "silvervein_frac", "$item_silver" },
            // Ancient armor and swords in the Mistlands drop scrap iron and copper scrap.
            { "giant_helmet1", "$item_iron" },
            { "giant_helmet1_destruction", "$item_iron" },
            { "giant_helmet2", "$item_iron" },
            { "giant_helmet2_destruction", "$item_iron" },
            { "giant_sword1", "$item_iron" },
            { "giant_sword1_destruction", "$item_iron" },
            { "giant_sword2", "$item_iron" },
            { "giant_sword2_destruction", "$item_iron" },
        };

        private static GameObject s_lastHover;
        private static ZNetView s_lastDeposit;
        private static string s_lastLabel;

        private ZNetView m_held;
        private float m_heldTime;
        private bool m_waitRelease;

        public void Update()
        {
            Player player = Player.m_localPlayer;
            bool pressed = ZInput.GetButton("Use") || ZInput.GetButton("JoyUse");
            if (!pressed)
            {
                m_waitRelease = false;
                m_held = null;
                return;
            }
            if (m_waitRelease || !DaymarkPlugin.DepositPins.Value) return;
            if (!player || player.IsDead() || !Minimap.instance || InMenu())
            {
                m_held = null;
                return;
            }

            ZNetView deposit = GetHoveredDeposit(player, out string label);
            if (!deposit || deposit != m_held)
            {
                m_held = deposit;
                m_heldTime = 0f;
                return;
            }
            m_heldTime += Time.deltaTime;
            if (m_heldTime < HoldTime) return;

            m_waitRelease = true;
            string name = Localization.instance.Localize(label);
            // The same call a Vegvisir's answer ends in: it adds a saved pin and shows vanilla's
            // "Pin added" message, or adds nothing when this deposit already has its pin.
            if (!Minimap.instance.DiscoverLocation(deposit.transform.position, PinIcon, name, false))
                player.Message(MessageHud.MessageType.Center, "$msg_pin_exist");
        }

        /// <summary>The deposit under the crosshair within reach, or null.</summary>
        public static ZNetView GetHoveredDeposit(Player player, out string label)
        {
            GameObject hover = player.GetHoverObject();
            // Hover changes rarely while it is looked at; checked every frame by the hint too.
            if (hover != s_lastHover)
            {
                s_lastHover = hover;
                s_lastDeposit = null;
                s_lastLabel = null;
                ZNetView view = hover ? hover.GetComponentInParent<ZNetView>() : null;
                if (view && s_deposits.TryGetValue(Utils.GetPrefabName(view.gameObject), out string found))
                {
                    s_lastDeposit = view;
                    s_lastLabel = found;
                }
            }
            label = s_lastLabel;
            return s_lastDeposit;
        }

        private static bool InMenu() =>
            InventoryGui.IsVisible() || Minimap.IsOpen() || Menu.IsVisible() || TextInput.IsVisible()
            || Console.IsVisible() || Hud.InRadial() || (Chat.instance && Chat.instance.HasFocus());
    }

    /// <summary>Adds the hold hint under a deposit's name on the crosshair.</summary>
    [HarmonyPatch]
    internal static class DepositPinsPatches
    {
        private const string Hint = "[<color=yellow><b>$KEY_Use</b></color>] $daymark_deposit_pin";

        [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
        [HarmonyPostfix]
        private static void AddHint(Hud __instance, Player player)
        {
            if (!DaymarkPlugin.DepositPins.Value || !player) return;
            if (!DepositPins.GetHoveredDeposit(player, out _)) return;
            string text = __instance.m_hoverName.text;
            string hint = Localization.instance.Localize(Hint);
            // As vanilla does for every hover text: a gamepad button sprite goes without brackets.
            if (ZInput.IsGamepadActive())
                hint = hint.Replace("[<color=yellow><b><sprite=", "<sprite=").Replace("\"></b></color>]", "\">");
            __instance.m_hoverName.text = string.IsNullOrEmpty(text) ? hint : text + "\n" + hint;
        }
    }
}
