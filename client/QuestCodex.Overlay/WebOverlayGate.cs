// Adapted from examples/WebOverlayGate.cs in Anvil-WebOverlay, MIT License, Copyright (c) 2026 maschine.
// https://github.com/maschine34675/WebOverlay - rules in its docs/SOFT-DEPENDENCY.md.

using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using UnityEngine;

namespace QuestCodex.Overlay;

/// <summary>
/// The only class allowed to touch Anvil-WebOverlay types. They may appear in method bodies only:
/// a signature, field or captured local of a library type breaks Assembly.GetTypes() for every
/// other mod when the library is missing. tools/Audit-SoftDependency.ps1 enforces this.
/// </summary>
internal static class WebOverlayGate
{
    public const string LibraryGuid = "com.anvil.weboverlay";

    private static readonly Version MinimumVersion = new Version(1, 11, 0);

    private static bool? _loaded;
    private static Version? _foundVersion;
    private static object? _overlay;
    private static bool _failed;
    private static bool _reportedTooOld;

    private static bool IsLoaded
    {
        get
        {
            if (_loaded == null)
            {
                _loaded = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(LibraryGuid, out BepInEx.PluginInfo info);
                if (_loaded.Value && info?.Metadata != null)
                    _foundVersion = info.Metadata.Version;
            }
            return _loaded.Value;
        }
    }

    private static bool IsUsable => IsLoaded && _foundVersion != null && _foundVersion >= MinimumVersion;

    /// <summary>Opens or closes the window. False means the caller should use the browser.</summary>
    public static bool Toggle(string url, KeyboardShortcut hotkey, Action<string> logWarning)
    {
        if (!IsUsable)
        {
            if (IsLoaded && !_reportedTooOld)
            {
                _reportedTooOld = true;
                logWarning("Anvil-WebOverlay " + _foundVersion + " is older than " + MinimumVersion + "; using the browser.");
            }
            return false;
        }
        return !_failed && ToggleCore(url, hotkey, logWarning);
    }

    /// <summary>Drops the window, if there is one. The next Toggle creates a new one.</summary>
    public static void Shutdown()
    {
        if (IsUsable)
            ShutdownCore();
    }

    // Library types may appear from here down, in bodies only.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ToggleCore(string url, KeyboardShortcut hotkey, Action<string> logWarning)
    {
        // Exclusive fullscreen: a window over the game would minimise it, so the library refuses to show one.
        if (!WebOverlay.WebOverlayPlugin.IsDisplayModeSupported)
            return false;

        var handle = _overlay as WebOverlay.IWebOverlay;
        if (handle != null)
        {
            handle.Toggle();
            return true;
        }

        int closeKey = WebOverlay.WebOverlayPlugin.VirtualKey(hotkey.MainKey);
        handle = WebOverlay.WebOverlays.Create("QuestCodex", new WebOverlay.OverlayOptions
        {
            Width = Math.Min(1280, Screen.width * 9 / 10),
            Height = Math.Min(800, Screen.height * 9 / 10),
            // Not CloseKeysFor: it adds Escape, which the page's own dialogs use to close.
            CloseKeys = closeKey != 0 ? new[] { closeKey } : new int[0],
            FreeCursorWhileShown = true,
            ClickThroughWhenUnfocused = true,
            DispatchOnMainThread = true,
        });
        if (handle == null)
        {
            logWarning("Overlays are unavailable (is the WebView2 runtime installed?); using the browser.");
            _failed = true;
            return false;
        }

        _overlay = handle;

        // object, not the interface: a captured local becomes a field of a closure class.
        object created = handle;
        handle.Failed += () =>
        {
            var failedHandle = (WebOverlay.IWebOverlay)created;
            logWarning("Overlay failed (" + failedHandle.Failure + "): " + failedHandle.FailureMessage
                + ". Using the browser from now on.");
            failedHandle.Dispose();
            _failed = true;
            if (ReferenceEquals(_overlay, created))
                _overlay = null;
        };

        handle.Navigate(url);
        handle.Show();
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShutdownCore()
    {
        (_overlay as WebOverlay.IWebOverlay)?.Dispose();
        _overlay = null;
    }
}
