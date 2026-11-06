using UnityEngine;

namespace ModuleAdvisor
{
    internal static class L
    {
        internal static string Text(string key, params object[] args) => ModHelper.GetLocalized(ModEntry.Id, key, args);
    }

    public sealed class ModEntry : IMod
    {
        public const string Id = "xufly_module_advisor";
        private GameObject driver;
        private ModLog log;
        internal static ConfigEntry<int> Hotkey;
        internal static bool Enabled;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            Hotkey = ModHelper.GetConfig(Id).Define("hotkey", (int)KeyCode.F6, "Controls",
                () => L.Text("hotkey"));
        }

        public void OnEnable()
        {
            Enabled = true;
            ModHook.OnGameLoadedNormal += GameLoaded;
            ModHelper.AddGameOptionKeyBind(Id, ModHelper.OPTION_SECTION_KEYBIND, "open",
                () => L.Text("hotkey"), () => (KeyCode)Hotkey.Value, key => Hotkey.Set((int)key));
        }

        public void OnDisable()
        {
            Enabled = false;
            ModHook.OnGameLoadedNormal -= GameLoaded;
            ModHelper.RemoveGameOptions(Id);
            ModHelper.RemoveDebugFunctions(Id);
            AdvisorUI.Reset();
            if (driver != null) Object.Destroy(driver);
            driver = null;
        }

        private void GameLoaded()
        {
            AdvisorUI.Reset();
            if (driver != null) Object.Destroy(driver);
            driver = new GameObject("ModuleAdvisor.Input");
            driver.AddComponent<AdvisorInput>();
            ModHelper.RemoveDebugFunctions(Id);
            ModHelper.AddDebugFunction(Id, ModHelper.DEBUG_CATEGORY_SPECIAL, L.Text("title"), AdvisorUI.Open);
            log.Log(L.Text("loaded", KeyBindHelper.KeyName((KeyCode)Hotkey.Value)));
        }
    }

    public sealed class AdvisorInput : MonoBehaviour
    {
        private object locale;
        private void Update()
        {
            if (!ModEntry.Enabled || PlayerStore.Instance == null || CustomUIManager.Instance == null) return;
            var current = ModHelper.GetCurrentLocale();
            if (!ReferenceEquals(locale, current))
            {
                locale = current;
                if (AdvisorUI.IsOpen) AdvisorUI.Open();
            }
            if (!KeyBindHelper.SuppressHotkeys && !StoreUIManager.IsAnyTyping()
                && Input.GetKeyDown((KeyCode)ModEntry.Hotkey.Value))
            {
                if (AdvisorUI.IsOpen) AdvisorUI.Close(); else AdvisorUI.Open();
            }
        }
    }
}
