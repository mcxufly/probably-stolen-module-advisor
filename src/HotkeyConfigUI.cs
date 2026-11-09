// SPDX-FileCopyrightText: 2026 XuFly
// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ModuleAdvisor
{
    // The config entry remains an int for backward-compatible persistence. Only our row gets
    // a key-capture button; the hidden input lets native Apply/Reset keep their usual semantics.
    [HarmonyPatch(typeof(ModConfigWindow), "BuildEntry")]
    internal static class HotkeyConfigRow
    {
        private static bool Prefix(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            if (!ModEntry.Enabled || config.ModId != ModEntry.Id || entry.Key != "hotkey") return true;
            b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(L.Text("hotkey")).AddFlexibleSpace();
            b.AddInput("", ConfigEntry.Format(entry.BoxedValue), id: entry.Key);
            b.Last.SetVisible(false);
            var field = b.Last;
            HotkeyCapture capture = null;
            b.AddButton(KeyBindHelper.KeyName((KeyCode)(int)entry.BoxedValue), () => { if (capture != null) capture.Begin(); });
            b.Last.SetFixedWidth(245);
            var button = b.Last;
            b.End().AddLabel("", "hotkey_capture_status");
            var status = b.Last;
            b.OnClose(HotkeyCapture.CancelActive);
            capture = button.root.AddComponent<HotkeyCapture>();
            capture.Setup(field, button, status);
            return false;
        }
    }

    // The native options controller also polls the shared capture service and would cancel it
    // when its own window is closed. Leave polling to our row only while we own that service.
    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.PollKeyCapture))]
    internal static class HotkeyCaptureOwner
    {
        private static bool Prefix() => KeyBindHelper.CapturingOptionId != HotkeyCapture.CaptureId;
    }

    public sealed class HotkeyCapture : MonoBehaviour
    {
        internal const string CaptureId = ModEntry.Id + ".config.hotkey";
        private static HotkeyCapture active;
        private CustomUIElement field, button, status;
        private CustomUIWindow window;
        private bool oldCloseOnEscape;
        private int startFrame, restoreFrame = -1;

        internal void Setup(CustomUIElement field, CustomUIElement button, CustomUIElement status)
        {
            this.field = field;
            this.button = button;
            this.status = status;
        }

        internal void Begin()
        {
            if (active == this) { End(); return; }
            if (KeyBindHelper.IsCapturing) return;
            window = CustomUIManager.Instance.GetWindow(ModConfigWindow.WINDOW_ID_PREFIX + ModEntry.Id);
            if (window == null || !window.IsVisible) return;
            oldCloseOnEscape = restoreFrame >= 0 ? oldCloseOnEscape : window.closeOnEscape;
            restoreFrame = -1;
            window.closeOnEscape = false;
            startFrame = Time.frameCount;
            active = this;
            KeyBindHelper.StartCapture(CaptureId);
            status.SetText(L.Text("hotkey_waiting"));
            Refresh();
        }

        private void Update()
        {
            if (restoreFrame >= 0 && Time.frameCount >= restoreFrame)
            {
                if (window != null && window.IsAlive) window.closeOnEscape = oldCloseOnEscape;
                restoreFrame = -1;
            }
            if (active != this) { Refresh(); return; }
            if (window == null || !window.IsVisible || KeyBindHelper.CapturingOptionId != CaptureId)
            { End(); return; }
            if (Time.frameCount == startFrame) return;
            var result = KeyBindHelper.PollCapture(out var key);
            if (result == KeyBindHelper.CaptureResult.Cancelled) { End(); return; }
            if (result != KeyBindHelper.CaptureResult.Key) return;
            if (KeyBindHelper.IsReserved(key))
            { status.SetText(L.Text("hotkey_reserved")); return; }
            var ownId = ModHelper.GameOptionId(ModEntry.Id, "open");
            var owner = GameOptionRegistry.Sections.SelectMany(s => s.entries)
                .FirstOrDefault(e => e.kind == GameOptionRegistry.Kind.KeyBind && e.id != ownId
                    && e.getKey != null && e.getKey() == key);
            if (owner != null)
            { status.SetText(L.Text("hotkey_conflict", KeyBindHelper.KeyName(key), owner.label?.Invoke() ?? owner.id)); return; }
            field.SetString(((int)key).ToString());
            End();
        }

        private void Refresh()
        {
            if (button == null || !button.IsAlive) return;
            button.SetText(active == this ? L.Text("hotkey_press") :
                KeyBindHelper.KeyName(int.TryParse(field.GetString(), out int value) ? (KeyCode)value : KeyCode.F6));
        }

        private void End()
        {
            if (active != this) return;
            if (KeyBindHelper.CapturingOptionId == CaptureId) KeyBindHelper.StopCapture();
            active = null;
            restoreFrame = Time.frameCount + 1;
            status?.SetText("");
            Refresh();
        }

        internal static void CancelActive() { if (active != null) active.End(); }
        private void OnDisable()
        {
            End();
            if (restoreFrame >= 0 && window != null && window.IsAlive) window.closeOnEscape = oldCloseOnEscape;
            restoreFrame = -1;
        }
    }
}
