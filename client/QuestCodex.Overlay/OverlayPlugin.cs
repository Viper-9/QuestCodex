using BepInEx;
using BepInEx.Configuration;
using SPT.Common.Http;
using UnityEngine;

namespace QuestCodex.Overlay;

/// <summary>
/// Opens QuestCodex from inside the game: in an Anvil-WebOverlay window when that library is
/// usable, otherwise in the default browser. The server mod and web app know nothing about this.
/// </summary>
[BepInPlugin("com.viper.questcodex","QuestCodex Overlay", BuildInfo.Version)]
[BepInDependency(WebOverlayGate.LibraryGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class OverlayPlugin : BaseUnityPlugin
{
    private ConfigEntry<KeyboardShortcut> _hotkey = null!;

    private void Awake()
    {
        _hotkey = Config.Bind("General", "Open QuestCodex", new KeyboardShortcut(KeyCode.F4),
            "Opens or closes QuestCodex. Uses a window over the game when Anvil-WebOverlay is installed, "
            + "otherwise the default browser.");
        // Close keys are fixed when the window is created, so a new hotkey needs a new window.
        _hotkey.SettingChanged += (_, _) => WebOverlayGate.Shutdown();
    }

    private void Update()
    {
        // Polled by hand: KeyboardShortcut.IsDown blocks while any other key is held, walking included.
        KeyboardShortcut hotkey = _hotkey.Value;
        if (hotkey.MainKey == KeyCode.None || !Input.GetKeyDown(hotkey.MainKey))
            return;
        foreach (KeyCode modifier in hotkey.Modifiers)
        {
            if (!Input.GetKey(modifier))
                return;
        }

        string url = RequestHandler.Host.TrimEnd('/') + "/questcodex/";
        if (!WebOverlayGate.Toggle(url, hotkey, Logger.LogWarning))
            Application.OpenURL(url);
    }

    private void OnDestroy()
    {
        WebOverlayGate.Shutdown();
    }
}
