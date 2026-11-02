using System.IO;
using BepInEx;
using BepInEx.Configuration;
using Jotunn.Managers;
using Jotunn.Utils;

namespace Daymark
{
    // Client-side: nothing is saved, and the only message is an optional, read-only request for the
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
        public static ConfigEntry<float> CompassTime;
        public static ConfigEntry<bool> Wisp;

        private readonly CompassStrip m_compass = new CompassStrip();
        private readonly NightWisp m_wisp = new NightWisp();
        private SkyGlance m_glance;

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
            CompassTime = Config.Bind("Wayfinding", "CompassTime", 60f,
                new ConfigDescription("Seconds the compass stays on screen after reading the sky.",
                    new AcceptableValueRange<float>(5f, 600f)));
            Wisp = Config.Bind("Wayfinding", "Wisp", true,
                "In worlds without a map, a wisp stays with you at night: it floats over your bed at home and leads you there from afar.");

            m_glance = new SkyGlance(m_compass);
            WorldDirectory.Register();
            AddLocalizations();
        }

        private void Update()
        {
            m_glance.Update();
            m_compass.Update();
            m_wisp.Update();
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
