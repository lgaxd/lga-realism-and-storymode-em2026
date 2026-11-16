using BepInEx.Configuration;

namespace RealismOverhaul.Features;

/// One on/off switch per feature (ADR 0003).
public class FeatureFlags(ConfigFile config)
{
    public ConfigEntry<bool> OveragePlayer { get; } = config.Bind("Features", "OveragePlayer", true,
        "Allow one Academy player older than the Academy age limit (the Overage Player).");
}
