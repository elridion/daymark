using System.IO;
using BepInEx;
using BepInEx.Configuration;
using Jotunn.Managers;
using Jotunn.Utils;

namespace Daymark
{
    // Purely client-side: nothing is saved or sent, so peers without the mod can still connect.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    internal class DaymarkPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "elridion.daymark";
        public const string PluginName = "Daymark";
        public const string PluginVersion = "0.1.0";

        private const string TranslationPrefix = "Daymark.translations.";

        public static ConfigEntry<float> GazeAngle;
        public static ConfigEntry<float> GazeTime;

        private readonly SkyGlance m_glance = new SkyGlance();

        private void Awake()
        {
            GazeAngle = Config.Bind("Glance", "GazeAngle", 6f,
                new ConfigDescription("How close to the sun or moon you must look, in degrees from the centre of the screen.",
                    new AcceptableValueRange<float>(2f, 20f)));
            GazeTime = Config.Bind("Glance", "GazeTime", 0.75f,
                new ConfigDescription("Seconds you must keep looking at the sun or moon before the time of day is shown.",
                    new AcceptableValueRange<float>(0f, 5f)));

            AddLocalizations();
        }

        private void Update()
        {
            m_glance.Update();
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
