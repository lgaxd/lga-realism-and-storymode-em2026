using System.Text.Json.Nodes;
using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// Valve Regional Standings (see GLOSSARY.md: VRS), modelled on Valve's published method
/// (github.com/ValveSoftware/counter-strike_regional_standings):
///   1. Seeding, from the last 6 months, each result weighted by age (full for 30 days, then fading
///      to zero at 180 days), each factor normalised against the 5th-best team:
///      - Bounty Offered: the team's prize money (its 10 best results);
///      - Bounty Collected: wins over teams with high Bounty Offered (10 best);
///      - Opponent Network: distinct opponents beaten;
///      - LAN Wins: wins at LAN events (10 best).
///      Seed points = 400 + 1600 x the average of the four factors.
///   2. Head-to-head: every match in date order adjusts both teams Elo-style (K scaled by age).
/// Until the mod has 6 months of history, the result is blended with the game's own ERS ranking.
///
/// VRS then decides who is invited, as Valve requires of organisers: TournamentEngine
/// GetAvailableTeams (the list the AI fills tournaments from) is reordered by VRS, interleaving the
/// three Regions by quota (Europe 50%, Americas 25%, Asia 25%); a team's place for invite
/// eligibility (DataTeams.GetFullSquadERSPlace, inside IsTeamInviteEligible and
/// CanParticipateInTournament) becomes its place in that same order.
public static class Vrs
{
    record struct MatchRec(int Day, string T1, string T2, string Winner, bool Lan);
    record struct PrizeRec(int Day, string Team, int Amount);

    static readonly List<MatchRec> _matches = new();
    static readonly List<PrizeRec> _prizes = new();
    static readonly HashSet<string> _prizeTournaments = new();
    static readonly DateTime Epoch = new(2000, 1, 1);
    const int WindowDays = 180;

    static Dictionary<string, float> _points = new();
    static Dictionary<string, int> _globalPlace = new();
    static int _computedDay = -1;
    static ScopedPatch _placePatch = null!;

    public static void Apply(Harmony harmony)
    {
        Sidecar.Apply(harmony);
        Sidecar.Register(new Section());
        _placePatch = new ScopedPatch(harmony,
            AccessTools.Method(typeof(DataTeams), nameof(DataTeams.GetFullSquadERSPlace)),
            AccessTools.Method(typeof(Vrs), nameof(PlacePostfix)));
        harmony.PatchAll(typeof(Vrs));
        Plugin.Log.LogInfo("[VRS] patches applied");
    }

    static int Today() => (int)(new DateTime(GlobalValues.CurrentDate.Ticks) - Epoch).TotalDays;

    static float AgeWeight(int day, int today)
    {
        var age = today - day;
        if (age <= 30) return 1f;
        if (age >= WindowDays) return 0f;
        return (WindowDays - age) / (float)(WindowDays - 30);
    }

    // --- Recording results ---

    static readonly HashSet<string> _recordedToday = new();
    static int _recordedDay = -1;

    /// User matches end in EndMatch; AI matches are resolved in ProccesAIMatch. Record both, once.
    static void Record(DataMatch? match)
    {
        try
        {
            if (match == null || !match.IsMatchOver || string.IsNullOrEmpty(match.Team1Id) || string.IsNullOrEmpty(match.Team2Id)) return;
            var winner = match.WinnerId;
            if (string.IsNullOrEmpty(winner)) return;
            var today = Today();
            if (today != _recordedDay) { _recordedToday.Clear(); _recordedDay = today; }
            if (!_recordedToday.Add(MatchPower.Key(match))) return;
            var lan = false;
            if (!string.IsNullOrEmpty(match.TournamentId))
                lan = DataTournaments.Get(match.TournamentId)?.IsLan ?? false;
            _matches.Add(new MatchRec(today, match.Team1Id, match.Team2Id, winner, lan));
        }
        catch (Exception e) { Plugin.Log.LogError($"[VRS] could not record a match: {e}"); }
    }

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.EndMatch), typeof(DataMatch), typeof(SeriesResult), typeof(bool))]
    [HarmonyPostfix]
    static void EndMatchPostfix(DataMatch match) => Record(match);

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.ProccesAIMatch))]
    [HarmonyPostfix]
    static void ProccesAIMatchPostfix(DataMatch match) => Record(match);

    /// Most AI matches are resolved inside ProcessAIMatches' own loop.
    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.ProcessAIMatches))]
    [HarmonyPostfix]
    static void ProcessAIMatchesPostfix(Il2CppSystem.Collections.Generic.List<DataMatch> matches)
    {
        if (matches == null) return;
        for (var i = 0; i < matches.Count; i++) Record(matches[i]);
    }

    /// The game builds a tournament's history row (final places) when it ends; record prize money.
    [HarmonyPatch(typeof(DataTournaments.HistoryRow), nameof(DataTournaments.HistoryRow.Of))]
    [HarmonyPostfix]
    static void HistoryRowPostfix(DataTournament t, DataTournaments.HistoryRow __result)
    {
        try
        {
            if (t == null || __result?.Places == null || !_prizeTournaments.Add(t.Id)) return;
            var prizes = t.PrizeFundList;
            if (prizes == null) return;
            // The game also builds history rows for past tournaments (history screens), so date the
            // prize by the tournament's end, not by today, and ignore anything outside the window.
            var day = (int)(new DateTime(t.DateEnd.Ticks) - Epoch).TotalDays;
            if (Today() - day > WindowDays) return;
            foreach (var key in __result.Places.Keys)
            {
                var place = __result.Places[key];
                if (place >= 1 && place <= prizes.Count && prizes[place - 1] > 0)
                    _prizes.Add(new PrizeRec(day, key, prizes[place - 1]));
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[VRS] could not record prizes: {e}"); }
    }

    // --- Computing standings ---

    static List<DataTeam> AllTeams()
    {
        var all = DataTeams.List;
        var n = all.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<DataTeam>>().Count;
        var result = new List<DataTeam>(n);
        for (var i = 0; i < n; i++)
        {
            var t = all[i];
            if (t != null && !t.IsDisbanded) result.Add(t);
        }
        return result;
    }

    static Dictionary<string, float> Normalise(Dictionary<string, float> raw)
    {
        var sorted = raw.Values.Where(v => v > 0).OrderByDescending(v => v).ToList();
        if (sorted.Count == 0) return raw.ToDictionary(kv => kv.Key, _ => 0f);
        var reference = sorted[Math.Min(4, sorted.Count - 1)]; // the 5th-best team
        return raw.ToDictionary(kv => kv.Key, kv => reference <= 0 ? 0f : Math.Min(1f, kv.Value / reference));
    }

    static float TopN(IEnumerable<float> values, int n) => values.OrderByDescending(v => v).Take(n).Sum();

    public static void Recompute()
    {
        var today = Today();
        _matches.RemoveAll(m => today - m.Day > WindowDays);
        _prizes.RemoveAll(p => today - p.Day > WindowDays);
        var teams = AllTeams();
        var ids = teams.Select(t => t.Id).ToList();
        _regionCache.Clear(); // rosters change; a team's Region is its starters' majority

        // 1. Seeding factors.
        var bountyOffered = Normalise(ids.ToDictionary(id => id,
            id => TopN(_prizes.Where(p => p.Team == id).Select(p => p.Amount * AgeWeight(p.Day, today)), 10)));
        var wins = _matches.Select(m => (m.Day, Winner: m.Winner, Loser: m.Winner == m.T1 ? m.T2 : m.T1, m.Lan)).ToList();
        var bountyCollected = Normalise(ids.ToDictionary(id => id,
            id => TopN(wins.Where(w => w.Winner == id).Select(w => (bountyOffered.TryGetValue(w.Loser, out var b) ? b : 0f) * AgeWeight(w.Day, today)), 10)));
        var network = Normalise(ids.ToDictionary(id => id,
            id => wins.Where(w => w.Winner == id).GroupBy(w => w.Loser).Sum(g => g.Max(w => AgeWeight(w.Day, today)))));
        var lanWins = Normalise(ids.ToDictionary(id => id,
            id => TopN(wins.Where(w => w.Winner == id && w.Lan).Select(w => AgeWeight(w.Day, today)), 10)));

        var rating = ids.ToDictionary(id => id,
            id => 400f + 1600f * (bountyOffered[id] + bountyCollected[id] + network[id] + lanWins[id]) / 4f);

        // 2. Head-to-head, in date order.
        foreach (var m in _matches.OrderBy(m => m.Day))
        {
            if (!rating.TryGetValue(m.T1, out var r1) || !rating.TryGetValue(m.T2, out var r2)) continue;
            var expected1 = 1f / (1f + MathF.Pow(10f, (r2 - r1) / 400f));
            var actual1 = m.Winner == m.T1 ? 1f : 0f;
            var k = 32f * AgeWeight(m.Day, today);
            rating[m.T1] = r1 + k * (actual1 - expected1);
            rating[m.T2] = r2 - k * (actual1 - expected1);
        }

        // 3. Blend with the game's ERS while the mod's history is shorter than the window.
        var history = _matches.Count == 0 ? 0 : today - _matches.Min(m => m.Day);
        var w = Math.Clamp(history / (float)WindowDays, 0f, 1f);
        var ers = teams.ToDictionary(t => t.Id, t => (float)t.ERS);
        float minE = ers.Values.DefaultIfEmpty(0).Min(), maxE = ers.Values.DefaultIfEmpty(1).Max();
        _points = ids.ToDictionary(id => id, id =>
        {
            var ersPoints = maxE > minE ? 400f + 1600f * (ers[id] - minE) / (maxE - minE) : 400f;
            return w * rating[id] + (1f - w) * ersPoints;
        });

        _globalPlace = new Dictionary<string, int>();
        var order = QuotaOrder(teams);
        for (var i = 0; i < order.Count; i++) _globalPlace[order[i].Id] = i + 1;
        _computedDay = today;

        Plugin.Log.LogInfo($"[VRS] standings computed: {_matches.Count} matches, {_prizes.Count} prize results, history {history} days (VRS weight {w:P0}, ERS {1 - w:P0})");
        foreach (var region in Enum.GetValues<Region>())
        {
            var top = teams.Where(t => RegionOfTeam(t) == region).OrderByDescending(t => _points[t.Id]).Take(5)
                .Select(t => $"{t.Name} {_points[t.Id]:F0}");
            Plugin.Log.LogInfo($"[VRS] {region} top 5: {string.Join(", ", top)}");
        }
        var club = GlobalValues.DataTeam;
        if (club != null && _points.TryGetValue(club.Id, out var mine))
        {
            var region = RegionOfTeam(club);
            var regionalRank = teams.Where(t => RegionOfTeam(t) == region).Count(t => _points[t.Id] > mine) + 1;
            Plugin.Log.LogInfo($"[VRS] {club.Name}: {mine:F0} points, #{regionalRank} in {region}, invite place #{_globalPlace[club.Id]}");
        }
    }

    static readonly Dictionary<string, Region> _regionCache = new(); // refreshed on every recompute

    static Region RegionOfTeam(DataTeam t)
    {
        if (_regionCache.TryGetValue(t.Id, out var cached)) return cached;
        var s = Regions.SubregionOf(t);
        var r = s == null ? Region.Europe : Regions.RegionOf(s.Value);
        _regionCache[t.Id] = r;
        return r;
    }

    static float PointsOf(DataTeam t) => _points.TryGetValue(t.Id, out var p) ? p : 400f;

    /// Teams ordered by VRS, the three Regions interleaved by quota (Europe 2 : Americas 1 : Asia 1).
    static List<DataTeam> QuotaOrder(IEnumerable<DataTeam> teams)
    {
        var queues = Enum.GetValues<Region>().ToDictionary(r => r,
            r => new Queue<DataTeam>(teams.Where(t => RegionOfTeam(t) == r).OrderByDescending(PointsOf)));
        var pattern = new[] { Region.Europe, Region.Americas, Region.Europe, Region.Asia };
        var result = new List<DataTeam>();
        while (queues.Values.Any(q => q.Count > 0))
            foreach (var r in pattern)
            {
                if (queues[r].Count > 0) { result.Add(queues[r].Dequeue()); continue; }
                // A Region that ran out yields its slot to the best remaining team overall.
                var best = queues.Values.Where(q => q.Count > 0).OrderByDescending(q => PointsOf(q.Peek())).FirstOrDefault();
                if (best != null) result.Add(best.Dequeue());
            }
        return result;
    }

    static void EnsureComputed()
    {
        if (_computedDay < 0) Recompute();
    }

    [HarmonyPatch(typeof(CalendarController), nameof(CalendarController.NextDayDate))]
    [HarmonyPostfix]
    static void NextDayDatePostfix()
    {
        if (GlobalValues.CurrentDate.DayOfWeek != Il2CppSystem.DayOfWeek.Monday && _computedDay >= 0) return;
        try { Recompute(); }
        catch (Exception e) { Plugin.Log.LogError($"[VRS] recompute failed: {e}"); }
    }

    // --- Driving invites ---

    /// Runs before Regional Tournament filtering (higher priority), which keeps this order.
    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.GetAvailableTeams))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.High)]
    static void GetAvailableTeamsPostfix(ref Il2CppSystem.Collections.Generic.List<DataTeam> __result)
    {
        if (__result == null || __result.Count == 0) return;
        try
        {
            EnsureComputed();
            var teams = new List<DataTeam>();
            for (var i = 0; i < __result.Count; i++) if (__result[i] != null) teams.Add(__result[i]);
            var ordered = new Il2CppSystem.Collections.Generic.List<DataTeam>();
            foreach (var t in QuotaOrder(teams)) ordered.Add(t);
            __result = ordered;
        }
        catch (Exception e) { Plugin.Log.LogError($"[VRS] could not order available teams: {e}"); }
    }

    static void PlacePostfix(DataTeam teamInput, ref int __result)
    {
        if (teamInput != null && _globalPlace.TryGetValue(teamInput.Id, out var place)) __result = place;
    }

    static bool BeginPlace()
    {
        try { EnsureComputed(); } catch (Exception e) { Plugin.Log.LogError($"[VRS] {e}"); return false; }
        _placePatch.Begin();
        return true;
    }

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.IsTeamInviteEligible))]
    [HarmonyPrefix]
    static void InvitePrefix(out bool __state) => __state = BeginPlace();

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.IsTeamInviteEligible))]
    [HarmonyPostfix]
    static void InvitePostfix(bool __state) { if (__state) _placePatch.End(); }

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.CanParticipateInTournament))]
    [HarmonyPrefix]
    static void CanParticipatePrefix(out bool __state) => __state = BeginPlace();

    [HarmonyPatch(typeof(TournamentEngine), nameof(TournamentEngine.CanParticipateInTournament))]
    [HarmonyPostfix]
    static void CanParticipatePostfix(bool __state) { if (__state) _placePatch.End(); }

    // --- Saved data ---

    sealed class Section : Sidecar.ISection
    {
        public string Name => "vrs";

        const int SectionVersion = 2; // v1 dated past tournaments' prizes by the day they were seen

        public void Load(JsonNode? data)
        {
            _matches.Clear();
            _prizes.Clear();
            _prizeTournaments.Clear();
            _computedDay = -1;
            if (data is not JsonObject obj) return;
            if ((obj["_v"]?.GetValue<int>() ?? 1) < SectionVersion)
            {
                Plugin.Log.LogInfo("[VRS] stored history is from an older version; starting it over");
                return;
            }
            if (obj["matches"] is JsonArray ms)
                foreach (var n in ms)
                    if (n is JsonArray a && a.Count == 5)
                        _matches.Add(new MatchRec(a[0]!.GetValue<int>(), a[1]!.GetValue<string>(), a[2]!.GetValue<string>(),
                            a[3]!.GetValue<string>(), a[4]!.GetValue<bool>()));
            if (obj["prizes"] is JsonArray ps)
                foreach (var n in ps)
                    if (n is JsonArray a && a.Count == 3)
                        _prizes.Add(new PrizeRec(a[0]!.GetValue<int>(), a[1]!.GetValue<string>(), a[2]!.GetValue<int>()));
            if (obj["prizeTournaments"] is JsonArray ts)
                foreach (var n in ts) if (n != null) _prizeTournaments.Add(n.GetValue<string>());
        }

        public JsonNode? Save()
        {
            var ms = new JsonArray();
            foreach (var m in _matches) ms.Add(new JsonArray(m.Day, m.T1, m.T2, m.Winner, m.Lan));
            var ps = new JsonArray();
            foreach (var p in _prizes) ps.Add(new JsonArray(p.Day, p.Team, p.Amount));
            var ts = new JsonArray();
            foreach (var t in _prizeTournaments) ts.Add(t);
            return new JsonObject { ["_v"] = SectionVersion, ["matches"] = ms, ["prizes"] = ps, ["prizeTournaments"] = ts };
        }
    }
}
