using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;

namespace Daymark
{
    // Client-side: nothing is saved in the world (dungeon pins go on the character's own map like
    // any pin the player places), and the only message is an optional, read-only request for the
    // ship and portal list, so peers and servers without the mod can still connect.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    internal class DaymarkPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "elridion.daymark";
        public const string PluginName = "Daymark";
        public const string PluginVersion = "0.2.0";

        private const string TranslationPrefix = "Daymark.translations.";

        public static ConfigEntry<float> GazeAngle;
        public static ConfigEntry<float> GazeTime;
        public static ConfigEntry<CompassStyle> Compass;
        public static ConfigEntry<float> CompassDistance;
        public static ConfigEntry<bool> Wisp;
        public static ConfigEntry<bool> DungeonPins;
        public static ConfigEntry<bool> DepositPins;

        private readonly CompassStrip m_compass = new CompassStrip();
        private readonly NightWisp m_wisp = new NightWisp();
        private readonly DungeonPins m_dungeonPins = new DungeonPins();
        private readonly DepositPins m_depositPins = new DepositPins();
        private Harmony _harmony;
        private SkyGlance m_glance;
        private Feature[] m_features;

        private void Awake()
        {
            GazeAngle = Config.Bind("Glance", "GazeAngle", 6f,
                new ConfigDescription("How close to the sun you must look, in degrees from the centre of the screen. At night any look well up at the stars counts.",
                    new AcceptableValueRange<float>(2f, 20f)));
            GazeTime = Config.Bind("Glance", "GazeTime", 0.75f,
                new ConfigDescription("Seconds you must keep looking at the sun or the stars before the time of day is shown.",
                    new AcceptableValueRange<float>(0f, 5f)));
            Compass = Config.Bind("Wayfinding", "Compass", CompassStyle.Runes,
                "In worlds without a map, the compass strip shown after reading the sky: Runes (Old Norse ᚾ ᚨ ᛊ ᚹ), Letters (compass points in your language) or Off.");
            CompassDistance = Config.Bind("Wayfinding", "CompassDistance", 1000f,
                new ConfigDescription("Metres of travel after reading the sky for each compass point to be lost: every this many metres, one point turns into a plain stroke, and with all four gone the compass fades. A portal, a dungeon or death loses them all.",
                    new AcceptableValueRange<float>(100f, 5000f)));
            Wisp = Config.Bind("Wayfinding", "Wisp", true,
                "In worlds without a map, a wisp stays with you at night: it floats over your bed at home and leads you there from afar.");
            DungeonPins = Config.Bind("Wayfinding", "DungeonPins", false,
                "Put a pin on the map at a dungeon's entrance when you go in, unless a pin already marks it. In worlds without a map it shows once the map is on.");

            DepositPins = Config.Bind("Wayfinding", "DepositPins", false,
                "Hold the use key on a copper deposit, silver vein or ancient armor or sword to pin it on the map, named after its metal.");

            m_glance = new SkyGlance(m_compass);
            m_features = new[]
            {
                new Feature { m_name = "sky glance", m_update = m_glance.Update },
                new Feature { m_name = "compass", m_update = m_compass.Update },
                new Feature { m_name = "night wisp", m_update = m_wisp.Update },
                new Feature { m_name = "dungeon pins", m_update = m_dungeonPins.Update },
                new Feature { m_name = "deposit pins", m_update = m_depositPins.Update },
            };
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(DaymarkPlugin).Assembly);
            WorldDirectory.Register();
            AddLocalizations();
        }

        private void Update()
        {
            // A feature that throws logs once and stops, instead of throwing every frame.
            for (int i = 0; i < m_features.Length; i++)
            {
                if (m_features[i].m_failed) continue;
                try
                {
                    m_features[i].m_update();
                }
                catch (Exception e)
                {
                    m_features[i].m_failed = true;
                    Jotunn.Logger.LogError($"Daymark {m_features[i].m_name} stopped after an error: {e}");
                }
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private struct Feature
        {
            public string m_name;
            public Action m_update;
            public bool m_failed;
        }

        // Each embedded assets/translations/<Language>.json is named after a Valheim language. Jotunn
        // falls back to English for languages and keys without a translation.
        private static void AddLocalizations()
        {
            var localization = LocalizationManager.Instance.GetLocalization();
            var assembly = typeof(DaymarkPlugin).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(TranslationPrefix) || !resource.EndsWith(".json")) continue;
                string language = resource.Substring(TranslationPrefix.Length, resource.Length - TranslationPrefix.Length - ".json".Length);
                using (var reader = new StreamReader(assembly.GetManifestResourceStream(resource)))
                    localization.AddJsonFile(language, reader.ReadToEnd());
            }
        }
    }
}
