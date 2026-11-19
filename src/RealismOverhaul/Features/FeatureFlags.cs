using BepInEx.Configuration;

namespace RealismOverhaul.Features;

/// One on/off switch per feature (ADR 0003).
public class FeatureFlags(ConfigFile config)
{
    public ConfigEntry<bool> OveragePlayer { get; } = config.Bind("Features", "OveragePlayer", true,
        "Allow one Academy player older than the Academy age limit (the Overage Player).");

    public ConfigEntry<bool> TournamentRegistration { get; } = config.Bind("Features", "TournamentRegistration", true,
        "Allow registering for a tournament while committed to another one, when their dates do not overlap.");

    public ConfigEntry<bool> AcademyMatchdays { get; } = config.Bind("Features", "AcademyMatchdays", true,
        "Academy matches stop the calendar like Main Roster matches (watch or simulate).");

    public ConfigEntry<bool> AiOverageRetention { get; } = config.Bind("Features", "AiOverageRetention", true,
        "AI Academies sometimes keep one player who aged out (always their IGL) instead of selling them.");

    public ConfigEntry<int> AiOverageRetentionChance { get; } = config.Bind("Tuning", "AiOverageRetentionChance", 15,
        new ConfigDescription("Percent chance an AI Academy keeps a non-IGL player who aged out.",
            new AcceptableValueRange<int>(0, 100)));
}
