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

    public ConfigEntry<bool> EmergencySub { get; } = config.Bind("Features", "EmergencySub", true,
        "A coach who used to be a player steps in when your team cannot field five (instead of a forfeit).");

    public ConfigEntry<bool> Roles { get; } = config.Bind("Features", "Roles", true,
        "Realistic Roles (Entry, Trader, Support, Lurker, AWPer, Anchor) shown on profiles, with a Role Balance effect on matches.");

    public ConfigEntry<float> RoleBalanceStrength { get; } = config.Bind("Tuning", "RoleBalanceStrength", 1f,
        new ConfigDescription("Scales the Role Balance bonus/penalty (0 = off, 1 = default, max -8%/+2% at 1).",
            new AcceptableValueRange<float>(0f, 2f)));

    public ConfigEntry<bool> MapFamiliarity { get; } = config.Bind("Features", "MapFamiliarity", true,
        "Per-player Map Familiarity and Side Bias affect map results; CT Positions are assigned by Role. Data is saved next to the game's save.");

    public ConfigEntry<float> FamiliarityStrength { get; } = config.Bind("Tuning", "FamiliarityStrength", 0.5f,
        new ConfigDescription("Weight of the lineups' Map Familiarity relative to the game's own team map mastery.",
            new AcceptableValueRange<float>(0f, 2f)));

    public ConfigEntry<float> SideBiasStrength { get; } = config.Bind("Tuning", "SideBiasStrength", 0.3f,
        new ConfigDescription("Weight of Side Bias on CT- or T-sided maps (fraction of the game's maximum map mastery points).",
            new AcceptableValueRange<float>(0f, 1f)));

    public ConfigEntry<bool> RosterSuggestions { get; } = config.Bind("Features", "RosterSuggestions", true,
        "The coach emails up to 3 replacement suggestions (same Role, within budget) when a Main Roster player is in a Performance Slump.");

    public ConfigEntry<float> SlumpThreshold { get; } = config.Bind("Tuning", "SlumpThreshold", 0.10f,
        new ConfigDescription("How far below his own baseline a player's recent rating must be (0.10 = 10%).", new AcceptableValueRange<float>(0.02f, 0.5f)));

    public ConfigEntry<int> SlumpWindowDays { get; } = config.Bind("Tuning", "SlumpWindowDays", 42,
        new ConfigDescription("Length of the recent window compared against the baseline.", new AcceptableValueRange<int>(14, 120)));

    public ConfigEntry<int> SlumpMinMaps { get; } = config.Bind("Tuning", "SlumpMinMaps", 12,
        new ConfigDescription("Minimum maps in both the recent window and the baseline.", new AcceptableValueRange<int>(3, 60)));

    public ConfigEntry<int> SlumpCooldownDays { get; } = config.Bind("Tuning", "SlumpCooldownDays", 60,
        new ConfigDescription("Days before the same player can trigger another suggestion.", new AcceptableValueRange<int>(7, 365)));

    public ConfigEntry<bool> RegionalTournaments { get; } = config.Bind("Features", "RegionalTournaments", true,
        "Tournaments naming a Subregion, and Online T3/T4 tournaments, are restricted to teams from that Subregion.");

    public ConfigEntry<bool> BackgroundThrottle { get; } = config.Bind("Performance", "BackgroundThrottle", true,
        "Lower the frame rate while the game is unfocused or minimized, to cut CPU/GPU load and heat.");

    public ConfigEntry<int> BackgroundFps { get; } = config.Bind("Performance", "BackgroundFps", 10,
        new ConfigDescription("Frame rate while the game is unfocused or minimized.", new AcceptableValueRange<int>(1, 60)));

    public ConfigEntry<int> ForegroundFpsCap { get; } = config.Bind("Performance", "ForegroundFpsCap", 0,
        new ConfigDescription("Frame rate cap while the game is focused (0 = keep the game's own setting). 60 is plenty for a manager game.",
            new AcceptableValueRange<int>(0, 360)));

    public ConfigEntry<bool> ForceRosterSuggestionTest { get; } = config.Bind("Diagnostics", "ForceRosterSuggestionTest", false,
        "Set to true to receive one Roster Suggestion email on the next day change, for your weakest Main Roster player, without waiting for a real Performance Slump. Turns itself off.");

    public ConfigEntry<bool> ClassInjectionSelfTest { get; } = config.Bind("Diagnostics", "ClassInjectionSelfTest", true,
        "At startup, check that class injection and delegate conversion work (needed for custom UI).");
}
