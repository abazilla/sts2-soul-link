using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using SoulLinkMod.UI;

using SoulLinkMod;

namespace SoulLinkMod.Patches;

/// <summary>
/// Injects a "Soul Link Settings" section into the mod-info panel that appears on the
/// right side of the Modding screen (General → Modding → Installed Mods → Soul Link).
///
/// Two-patch approach:
///   _Ready  — create the settings VBox as a hidden child node.
///   Fill    — show/hide the VBox based on which mod is selected; refresh values.
/// </summary>

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer._Ready))]
public static class ModInfoReadyPatch
{
    private const string VBoxName = "_SoulLinkSettingsVBox";

    [HarmonyPostfix]
    static void Postfix(NModInfoContainer __instance)
    {
        try
        {
            if (__instance.FindChild(VBoxName, owned: false) != null)
                return; // already injected (scene re-use)

            var vbox = BuildSettingsVBox(__instance);
            vbox.Name = VBoxName;
            vbox.Visible = false;
            __instance.AddChild(vbox);
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"ModInfoReadyPatch crashed: {ex}");
        }
    }

    private static VBoxContainer BuildSettingsVBox(NModInfoContainer parent)
    {
        // Position the vbox below the existing description text.
        // Use bottom-anchored offset so it doesn't overlap the description.
        var description = parent.FindChild("ModDescription", owned: false) as Control;
        float topOffset = description != null
            ? description.OffsetBottom + description.OffsetTop + 10f
            : 200f;

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        vbox.OffsetTop  = topOffset;
        vbox.OffsetLeft = 0f;
        vbox.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        vbox.AddThemeConstantOverride("separation", 4);

        // Separator
        var sep = new HSeparator();
        sep.AddThemeColorOverride("separation_color", new Color("333333"));
        vbox.AddChild(sep);

        // Section label
        var sectionLabel = new Label { Text = "Soul Link Settings" };
        sectionLabel.AddThemeFontSizeOverride("font_size", 13);
        sectionLabel.Modulate = new Color("888888");
        SoulLinkFont.Apply(sectionLabel);
        vbox.AddChild(sectionLabel);

        // Run settings note
        var noteLabel = new Label { Text = "Run settings are configured in the lobby." };
        noteLabel.AddThemeFontSizeOverride("font_size", 11);
        noteLabel.Modulate = new Color("666666");
        noteLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        SoulLinkFont.Apply(noteLabel);
        vbox.AddChild(noteLabel);

        // Panel visibility checkboxes
        vbox.AddChild(MakeCheckBox("Show Soul Feed (top right)",    SoulLinkSettings.Instance.ShowCombatLog,
            on => { SoulLinkSettings.Instance.ShowCombatLog    = on; SoulLinkSettings.Save(); }));
        vbox.AddChild(MakeCheckBox("Show Debug Overlay",            SoulLinkSettings.Instance.ShowDebugOverlay,
            on => { SoulLinkSettings.Instance.ShowDebugOverlay = on; SoulLinkSettings.Save(); }));

        return vbox;
    }

    private static Control MakeCheckBox(string text, bool initialValue, Action<bool> onToggle)
    {
        var cb = new CheckBox
        {
            Text      = text,
            FocusMode = Control.FocusModeEnum.None,
        };
        cb.SetPressedNoSignal(initialValue);
        cb.AddThemeFontSizeOverride("font_size", 13);
        cb.Toggled += (bool on) => onToggle(on);
        SoulLinkFont.Apply(cb);
        return cb;
    }
}

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
public static class ModInfoFillPatch
{
    private const string VBoxName = "_SoulLinkSettingsVBox";

    [HarmonyPostfix]
    static void Postfix(NModInfoContainer __instance, Mod mod)
    {
        try
        {
            var vbox = __instance.FindChild(VBoxName, owned: false) as Control;
            if (vbox == null) return;

            bool isSoulLink = mod.manifest?.id == "SoulLink";
            vbox.Visible = isSoulLink;

            if (!isSoulLink) return;

            // Refresh checkbox states in case settings changed since _Ready ran.
            var s = SoulLinkSettings.Instance;
            int childIdx = 0;
            foreach (var child in vbox.GetChildren())
            {
                if (child is CheckBox cb)
                {
                    string text = cb.Text;
                    bool value = text.Contains("Soul Feed") ? s.ShowCombatLog
                                                            : s.ShowDebugOverlay;
                    cb.SetPressedNoSignal(value);
                }
                childIdx++;
            }
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"ModInfoFillPatch crashed: {ex}");
        }
    }
}
