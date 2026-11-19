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
}
