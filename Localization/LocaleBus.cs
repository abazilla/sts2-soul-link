using System;
using MegaCrit.Sts2.Core.Localization;

namespace SoulLinkMod.Localization;

/// <summary>
/// Thin wrapper around <see cref="LocString.SubscribeToLocaleChange"/> / Unsubscribe so
/// callers don't have to import the MegaCrit namespace, and so subscribe/unsubscribe
/// failures degrade gracefully (single try/catch instead of every call site).
///
/// Callback signature is <see cref="LocManager.LocaleChangeCallback"/> — parameterless.
/// Fires after the game has reloaded its tables for the new language.
/// </summary>
public static class LocaleBus
{
    public static void Subscribe(LocManager.LocaleChangeCallback cb)
    {
        try { LocString.SubscribeToLocaleChange(cb); }
        catch (Exception ex) { SoulLinkLog.Error($"LocaleBus.Subscribe failed: {ex.Message}"); }
    }

    public static void Unsubscribe(LocManager.LocaleChangeCallback cb)
    {
        try { LocString.UnsubscribeToLocaleChange(cb); }
        catch (Exception ex) { SoulLinkLog.Error($"LocaleBus.Unsubscribe failed: {ex.Message}"); }
    }
}
