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

        if (Flags.ClassInjectionSelfTest.Value)
            Infrastructure.ClassInjectionSelfTest.Run();

        if (Flags.BackgroundThrottle.Value)
            Infrastructure.BackgroundThrottle.Start();

        var harmony = new Harmony(Guid);
        if (Flags.OveragePlayer.Value)
            Features.OveragePlayer.Apply(harmony);
        if (Flags.TournamentRegistration.Value)
            Features.TournamentRegistration.Apply(harmony);
        if (Flags.AcademyMatchdays.Value)
            Features.AcademyMatchdays.Apply(harmony);
        if (Flags.Comeback.Value)
            Features.Comeback.Apply(harmony);
        if (Flags.RetiredCoach.Value)
            Features.RetiredCoach.Apply(harmony);
        if (Flags.EmergencySub.Value)
            Features.EmergencySub.Apply(harmony);
        if (Flags.OverlappingTournaments.Value)
            Features.DoubleMatchday.Apply(harmony);
        if (Flags.Roles.Value)
            Features.Roles.Apply(harmony);
        if (Flags.MapFamiliarity.Value)
            Features.MapFamiliarity.Apply(harmony);
        if (Flags.RosterSuggestions.Value)
            Features.RosterSuggestions.Apply(harmony);
        if (Flags.RegionalTournaments.Value)
            Features.Regions.Apply(harmony);
        if (Flags.Vrs.Value)
        {
            Features.Vrs.Apply(harmony);
            Features.VrsUi.Apply(harmony);
        }

        if (Flags.ProfileUi.Value)
            Features.ProfileUi.Apply(harmony);
        if (Flags.CtPositionsUi.Value)
            Features.CtPositionsUi.Apply(harmony);

        if (Flags.ExportDefaultDatabase.Value)
            Infrastructure.EmdbExport.Run();

        if (Flags.StoryMode.Value)
            StoryMode.StoryStart.Apply(harmony);

        if (Flags.UiCensus.Value)
            Infrastructure.UiCensus.Start();

        if (Flags.DumpUi.Value)
            Infrastructure.UiDump.Apply(harmony);

        Log.LogInfo($"{Name} loaded");
    }
}
