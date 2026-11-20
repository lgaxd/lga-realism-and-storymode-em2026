using System.Text.Json.Nodes;
using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// The user's manual choices, which override what the mod derives: a player's Roles, and a team's
/// CT Positions per map. Saved in the sidecar file; absent choices fall back to the derived ones.
public static class ManualChoices
{
    static readonly Dictionary<string, (Role Primary, Role? Secondary)> _roles = new();
    // teamId -> map -> position -> playerId
    static readonly Dictionary<string, Dictionary<MapType, Dictionary<string, string>>> _ct = new();
    static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        _applied = true;
        Sidecar.Apply(harmony);
        Sidecar.Register(new Section());
    }

    // --- Roles ---

    public static bool TryGetRoles(string playerId, out (Role Primary, Role? Secondary) roles) => _roles.TryGetValue(playerId, out roles);

    public static void SetRoles(string playerId, Role primary, Role? secondary)
    {
        _roles[playerId] = (primary, secondary == primary ? null : secondary);
        Plugin.Log.LogInfo($"[Choices] {playerId} Role set to {primary}{(secondary != null ? $" / {secondary}" : "")}");
    }

    public static void ClearRoles(string playerId)
    {
        if (_roles.Remove(playerId)) Plugin.Log.LogInfo($"[Choices] {playerId} Role back to automatic");
    }

    // --- CT Positions ---

    public static IReadOnlyDictionary<string, string>? CtPositions(string teamId, MapType map) =>
        _ct.TryGetValue(teamId, out var maps) && maps.TryGetValue(map, out var pos) ? pos : null;

    public static void SetCtPosition(string teamId, MapType map, string position, string playerId)
    {
        if (!_ct.TryGetValue(teamId, out var maps)) _ct[teamId] = maps = new();
        if (!maps.TryGetValue(map, out var pos)) maps[map] = pos = new();
        // A player holds one position per map.
        foreach (var key in pos.Where(kv => kv.Value == playerId).Select(kv => kv.Key).ToList()) pos.Remove(key);
        pos[position] = playerId;
        Plugin.Log.LogInfo($"[Choices] {teamId} {map}: {position} = {playerId}");
    }

    public static void ClearCtPositions(string teamId, MapType map)
    {
        if (_ct.TryGetValue(teamId, out var maps) && maps.Remove(map))
            Plugin.Log.LogInfo($"[Choices] {teamId} {map}: CT Positions back to automatic");
    }

    // --- Saved data ---

    sealed class Section : Sidecar.ISection
    {
        public string Name => "choices";

        public void Load(JsonNode? data)
        {
            _roles.Clear();
            _ct.Clear();
            if (data is not JsonObject obj) return;
            if (obj["roles"] is JsonObject roles)
                foreach (var (id, node) in roles)
                    if (node is JsonArray a && a.Count >= 1 && Enum.TryParse<Role>(a[0]?.GetValue<string>(), out var p))
                    {
                        Role? s = a.Count > 1 && Enum.TryParse<Role>(a[1]?.GetValue<string>(), out var sv) ? sv : null;
                        _roles[id] = (p, s);
                    }
            if (obj["ct"] is JsonObject ct)
                foreach (var (teamId, mapsNode) in ct)
                {
                    if (mapsNode is not JsonObject maps) continue;
                    var dict = new Dictionary<MapType, Dictionary<string, string>>();
                    foreach (var (mapName, posNode) in maps)
                        if (Enum.TryParse<MapType>(mapName, out var map) && posNode is JsonObject pos)
                            dict[map] = pos.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>());
                    _ct[teamId] = dict;
                }
        }

        public JsonNode? Save()
        {
            var roles = new JsonObject();
            foreach (var (id, (p, s)) in _roles)
                roles[id] = s == null ? new JsonArray(p.ToString()) : new JsonArray(p.ToString(), s.Value.ToString());
            var ct = new JsonObject();
            foreach (var (teamId, maps) in _ct)
            {
                var m = new JsonObject();
                foreach (var (map, pos) in maps)
                {
                    var o = new JsonObject();
                    foreach (var (position, playerId) in pos) o[position] = playerId;
                    m[map.ToString()] = o;
                }
                ct[teamId] = m;
            }
            return new JsonObject { ["roles"] = roles, ["ct"] = ct };
        }
    }
}
