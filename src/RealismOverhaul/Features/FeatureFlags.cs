using BepInEx.Configuration;

namespace RealismOverhaul.Features;

/// One on/off switch per feature (ADR 0003).
public class FeatureFlags(ConfigFile config)
{
    public ConfigEntry<bool> OveragePlayer { get; } = config.Bind("Features", "OveragePlayer", true,
        "Allow one Academy player older than the Academy age limit (the Overage Player).");

    public ConfigEntry<bool> TournamentRegistration { get; } = config.Bind("Features", "TournamentRegistration", true,
        "Allow registering for a tournament while committed to another one when their dates do not overlap.");

    public ConfigEntry<bool> OverlappingTournaments { get; } = config.Bind("Features", "OverlappingTournaments", true,
        "Also allow overlapping tournaments: at most 2 at once and never 2 LANs. Same-day matches are played in sequence.");

    public ConfigEntry<float> DoubleMatchdayFatigue { get; } = config.Bind("Tuning", "DoubleMatchdayFatigue", 0.05f,
        new ConfigDescription("Power penalty for a team's second match on the same day (0.05 = 5%).",
            new AcceptableValueRange<float>(0f, 0.5f)));

    public ConfigEntry<bool> AcademyMatchdays { get; } = config.Bind("Features", "AcademyMatchdays", true,
        "Academy matches stop the calendar like Main Roster matches (watch or simulate).");

    public ConfigEntry<bool> AiOverageRetention { get; } = config.Bind("Features", "AiOverageRetention", true,
        "AI Academies sometimes keep one player who aged out (preferably their IGL) instead of selling them.");

    public ConfigEntry<int> AiOverageRetentionChance { get; } = config.Bind("Tuning", "AiOverageRetentionChance", 15,
        new ConfigDescription("Percent chance per season that an AI Academy keeps one player who aged out.",
            new AcceptableValueRange<int>(0, 100)));

    public ConfigEntry<bool> Comeback { get; } = config.Bind("Features", "Comeback", true,
        "Retired players can be signed again within ComebackMaxYears of retiring.");

    public ConfigEntry<bool> RetiredCoach { get; } = config.Bind("Features", "RetiredCoach", true,
        "Retired players' transfer tab gets a 'Hire as coach' button.");

    public ConfigEntry<int> ComebackMaxYears { get; } = config.Bind("Tuning", "ComebackMaxYears", 5,
        new ConfigDescription("Years after retiring during which a player can come back.", new AcceptableValueRange<int>(0, 20)));

    public ConfigEntry<float> ComebackDecayPerYear { get; } = config.Bind("Tuning", "ComebackDecayPerYear", 0.05f,
        new ConfigDescription("Mechanical skill loss per year retired (0.05 = 5%).", new AcceptableValueRange<float>(0f, 0.5f)));

    public ConfigEntry<float> ComebackMaxDecay { get; } = config.Bind("Tuning", "ComebackMaxDecay", 0.20f,
        new ConfigDescription("Maximum total mechanical skill loss on a Comeback.", new AcceptableValueRange<float>(0f, 0.9f)));

    public ConfigEntry<bool> ClassInjectionSelfTest { get; } = config.Bind("Diagnostics", "ClassInjectionSelfTest", true,
        "At startup, check that class injection and delegate conversion work (needed for custom UI).");
}
