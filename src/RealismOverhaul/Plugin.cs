using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace RealismOverhaul;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "lga.esportsmanager2026.realismoverhaul";
    public const string Name = "Realism Overhaul";
    public const string Version = "0.1.0";

    internal static ManualLogSource Log = null!;
    internal static Features.FeatureFlags Flags = null!;

    public override void Load()
    {
        Log = base.Log;
        Flags = new Features.FeatureFlags(Config);
        Log.LogInfo($"{Name} {Version} loading");

        var harmony = new Harmony(Guid);
        if (Flags.OveragePlayer.Value)
            Features.OveragePlayer.Apply(harmony);
        if (Flags.TournamentRegistration.Value)
            Features.TournamentRegistration.Apply(harmony);

        Log.LogInfo($"{Name} loaded");
    }
}
