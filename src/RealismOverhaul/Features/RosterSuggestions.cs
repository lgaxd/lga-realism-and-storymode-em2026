using System.Text.Json.Nodes;
using HarmonyLib;
using RealismOverhaul.Infrastructure;

namespace RealismOverhaul.Features;

/// Performance Slump and Roster Suggestion (see GLOSSARY.md).
///
/// The game keeps per-map ratings only inside each match's records, so this feature keeps its own
/// history: every map a Main Roster player plays adds (date, impact rating) to the sidecar file.
/// Every Monday, each Main Roster player is checked: a Performance Slump is an average rating at
/// least SlumpThreshold below his own baseline over the last SlumpWindowDays (with at least
/// SlumpMinMaps maps), the baseline being the 6 months before that window (also at least
/// SlumpMinMaps maps). The coach then sends an inbox email, in the game's own scout-recommendation
/// format, with up to 3 Roster Suggestions: players who fit the slumping player's Role (primary or
/// secondary), are stronger than him, and fit the budget (transfer fee within the transfer budget,
/// free agents and own Academy players free; wage demand at most 30% above his market wage).
/// Each player gets at most one suggestion per SlumpCooldownDays.
public static class RosterSuggestions
{
    record struct Entry(int Day, float Rating); // Day = days since 2000-01-01

    static readonly Dictionary<string, List<Entry>> _history = new();
    static readonly Dictionary<string, int> _lastSuggested = new(); // playerId -> Day
    static readonly DateTime Epoch = new(2000, 1, 1);
    const int BaselineDays = 182;

    public static void Apply(Harmony harmony)
    {
        Sidecar.Apply(harmony);
        Sidecar.Register(new Section());
        harmony.PatchAll(typeof(RosterSuggestions));
        Plugin.Log.LogInfo("[RosterSuggestions] patches applied");
    }

    static int Today() => (int)(new DateTime(GlobalValues.CurrentDate.Ticks) - Epoch).TotalDays;

    // --- Recording ratings ---

    [HarmonyPatch(typeof(MatchesEngine), nameof(MatchesEngine.EndMatch), typeof(DataMatch), typeof(SeriesResult), typeof(bool))]
    [HarmonyPostfix]
    static void EndMatchPostfix(DataMatch match)
    {
        try
        {
            var club = GlobalValues.DataTeam;
            if (match == null || club == null || (match.Team1Id != club.Id && match.Team2Id != club.Id)) return;
            var maps = match.MapStats;
            if (maps == null) return;
            var squad = club.MainSquad;
            var day = Today();
            for (var i = 0; i < maps.Count; i++)
            {
                var players = maps[i]?._playerStats;
                if (players == null) continue;
                foreach (var key in players.Keys)
                {
                    if (squad == null || !squad.Contains(key)) continue;
                    var rating = players[key].GetImpactRating();
                    if (float.IsNaN(rating) || rating <= 0f) continue;
                    if (!_history.TryGetValue(key, out var list)) _history[key] = list = new List<Entry>();
                    list.Add(new Entry(day, rating));
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[RosterSuggestions] could not record ratings: {e}");
        }
    }

    // --- Weekly check ---

    [HarmonyPatch(typeof(CalendarController), nameof(CalendarController.NextDayDate))]
    [HarmonyPostfix]
    static void NextDayDatePostfix()
    {
        if (Plugin.Flags.ForceRosterSuggestionTest.Value)
        {
            Plugin.Flags.ForceRosterSuggestionTest.Value = false; // one shot
            try { ForcedTest(); }
            catch (Exception e) { Plugin.Log.LogError($"[RosterSuggestions] forced test failed: {e}"); }
        }
        if (GlobalValues.CurrentDate.DayOfWeek != Il2CppSystem.DayOfWeek.Monday) return;
        try { WeeklyCheck(); }
        catch (Exception e) { Plugin.Log.LogError($"[RosterSuggestions] weekly check failed: {e}"); }
    }

    static void WeeklyCheck()
    {
        var club = GlobalValues.DataTeam;
        var squad = club?.MainSquad;
        if (club == null || squad == null) return;
        var today = Today();
        var window = Plugin.Flags.SlumpWindowDays.Value;
        var minMaps = Plugin.Flags.SlumpMinMaps.Value;

        // Drop history older than anything we compare against.
        foreach (var list in _history.Values) list.RemoveAll(e => e.Day < today - window - BaselineDays);

        for (var i = 0; i < squad.Count; i++)
        {
            var id = squad[i];
            if (!_history.TryGetValue(id, out var list)) continue;
            var recent = list.Where(e => e.Day > today - window).ToList();
            var baseline = list.Where(e => e.Day <= today - window).ToList();
            if (recent.Count < minMaps || baseline.Count < minMaps) continue;

            var recentAvg = recent.Average(e => e.Rating);
            var baseAvg = baseline.Average(e => e.Rating);
            if (recentAvg > baseAvg * (1f - Plugin.Flags.SlumpThreshold.Value)) continue;
            if (_lastSuggested.TryGetValue(id, out var last) && today - last < Plugin.Flags.SlumpCooldownDays.Value) continue;

            var p = DataPlayers.Get(id);
            if (p == null) continue;
            _lastSuggested[id] = today;
            var suggestions = FindSuggestions(p, club);
            Plugin.Log.LogInfo($"[RosterSuggestions] Performance Slump: {id} {recentAvg:F2} over {recent.Count} maps vs baseline {baseAvg:F2} ({baseline.Count} maps); " +
                               $"suggesting {string.Join(", ", suggestions.Select(s => s.Id))}");
            SendEmail(p, club, recentAvg, baseAvg, recent.Count, suggestions);
        }
    }

    /// Diagnostics: send a suggestion now for the Main Roster's weakest player, skipping the history
    /// requirement, so the email and the candidate search can be checked without months of play.
    static void ForcedTest()
    {
        var club = GlobalValues.DataTeam;
        var squad = club?.MainSquad;
        if (club == null || squad == null || squad.Count == 0) return;
        DataPlayer? weakest = null;
        for (var i = 0; i < squad.Count; i++)
        {
            var p = DataPlayers.Get(squad[i]);
            if (p != null && (weakest == null || p.CalculatePower(1f) < weakest.CalculatePower(1f))) weakest = p;
        }
        if (weakest == null) return;
        var suggestions = FindSuggestions(weakest, club);
        Plugin.Log.LogInfo($"[RosterSuggestions] FORCED TEST for {weakest.Id} ({Roles.RolesOf(weakest).Primary}): suggesting {string.Join(", ", suggestions.Select(s => s.Id))}");
        SendEmail(weakest, club, 0.85f, 1.00f, 12, suggestions);
    }

    // --- Suggestions ---

    static List<DataPlayer> FindSuggestions(DataPlayer slumping, DataTeam club)
    {
        var role = Roles.RolesOf(slumping).Primary;
        var power = slumping.CalculatePower(1f);
        var transferBudget = DataFinances.Get(BudgetType.TRANSFER);
        var wageCap = slumping.ComputeMarketWage(false, false) * 1.3f;
        var academyId = club.GetAcademyTeam()?.Id;
        var mainSquad = club.MainSquad;

        var all = DataPlayers.List;
        var count = all.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<DataPlayer>>().Count;
        var candidates = new List<(DataPlayer Player, float Power)>();
        for (var i = 0; i < count; i++)
        {
            var c = all[i];
            if (c == null || c.Retired || c.Id == slumping.Id || mainSquad.Contains(c.Id)) continue;
            var cPower = c.CalculatePower(1f);
            if (cPower <= power) continue; // must be an upgrade on the slumping player
            var (primary, secondary) = Roles.RolesOf(c);
            if (primary != role && secondary != role) continue;

            var isFree = string.IsNullOrEmpty(c.TeamId) || c.TeamId == academyId;
            if (!isFree)
            {
                var seller = DataTeams.Get(c.TeamId);
                if (seller == null || c.GetTransferCost(seller) > transferBudget) continue;
            }
            if (c.ComputeMarketWage(false, false) > wageCap) continue;
            candidates.Add((c, cPower));
        }
        return candidates.OrderByDescending(x => x.Power).Take(3).Select(x => x.Player).ToList();
    }

    static void SendEmail(DataPlayer slumping, DataTeam club, float recentAvg, float baseAvg, int maps, List<DataPlayer> suggestions)
    {
        var coach = DataStaffs.GetByRoleAndTeam(StaffRole.Coach, club.Id);
        if (coach == null)
        {
            Plugin.Log.LogInfo("[RosterSuggestions] no coach to send the suggestion");
            return;
        }
        var pt = (UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en").StartsWith("pt");
        var drop = (1f - recentAvg / baseAvg) * 100f;
        var role = Roles.RolesOf(slumping).Primary;
        var window = Plugin.Flags.SlumpWindowDays.Value / 7;

        var subject = pt ? $"Queda de rendimento: {slumping.Id}" : $"Performance slump: {slumping.Id}";
        string body;
        if (suggestions.Count > 0)
            body = pt
                ? $"{slumping.Id} está rendendo {drop:F0}% abaixo do próprio nível nas últimas {window} semanas (rating {recentAvg:F2} em {maps} mapas, contra {baseAvg:F2} antes). Se quiser reforçar a função de {role}, estas opções cabem no nosso orçamento:"
                : $"{slumping.Id} has been playing {drop:F0}% below his own level for the last {window} weeks (rating {recentAvg:F2} over {maps} maps, against {baseAvg:F2} before). If you want to strengthen the {role} role, these options fit our budget:";
        else
            body = pt
                ? $"{slumping.Id} está rendendo {drop:F0}% abaixo do próprio nível nas últimas {window} semanas (rating {recentAvg:F2} em {maps} mapas, contra {baseAvg:F2} antes). Procurei substitutos para a função de {role}, mas nenhum que seja melhor cabe no orçamento agora."
                : $"{slumping.Id} has been playing {drop:F0}% below his own level for the last {window} weeks (rating {recentAvg:F2} over {maps} maps, against {baseAvg:F2} before). I looked for replacements for the {role} role, but no better one fits the budget right now.";

        var list = new Il2CppSystem.Collections.Generic.List<DataPlayer>();
        foreach (var s in suggestions) list.Add(s);
        var email = new DataEmail(EmailAuthorFactory.Create(coach), true, new ScoutRecomendationContent(list));
        email = DataEmailBuilder.WithSubject(email, subject);
        email = DataEmailBuilder.WithBodyBeforeContent(email, body);
        DataEmails.Add(email);
    }

    // --- Saved data ---

    sealed class Section : Sidecar.ISection
    {
        public string Name => "rosterSuggestions";

        public void Load(JsonNode? data)
        {
            _history.Clear();
            _lastSuggested.Clear();
            if (data is not JsonObject obj) return;
            if (obj["history"] is JsonObject history)
                foreach (var (id, node) in history)
                {
                    if (node is not JsonArray arr) continue;
                    var list = new List<Entry>();
                    foreach (var e in arr)
                        if (e is JsonArray pair && pair.Count == 2)
                            list.Add(new Entry(pair[0]!.GetValue<int>(), pair[1]!.GetValue<float>()));
                    _history[id] = list;
                }
            if (obj["lastSuggested"] is JsonObject last)
                foreach (var (id, node) in last)
                    if (node != null) _lastSuggested[id] = node.GetValue<int>();
        }

        public JsonNode? Save()
        {
            var history = new JsonObject();
            foreach (var (id, list) in _history)
            {
                var arr = new JsonArray();
                foreach (var e in list) arr.Add(new JsonArray(e.Day, MathF.Round(e.Rating, 3)));
                history[id] = arr;
            }
            var last = new JsonObject();
            foreach (var (id, day) in _lastSuggested) last[id] = day;
            return new JsonObject { ["history"] = history, ["lastSuggested"] = last };
        }
    }
}
