namespace GearScout;

// Only the log dependency is needed when checking MeldData against local game sheets.
internal static class Plugin
{
    internal static Dalamud.Plugin.Services.IPluginLog Log => throw new InvalidOperationException("Unexpected game-data error in native checks.");
}
