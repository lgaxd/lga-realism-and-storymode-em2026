using System.Text.Json;
using HarmonyLib;

namespace RealismOverhaul.StoryMode;

/// The real tournament calendar in a Story Mode career (ADR 0002, 0006).
///
/// The game keeps one list of tournament templates (Tournaments.csv) and, each new year, copies it
/// and spreads the copies over the year by tier (TournamentEngine.CreateSchedule ->
/// TournamentYearScheduler.ScheduleYear). In a Story Mode career, a year that history has data
/// for gets that year's real events instead (story/calendar.json, built by
/// tools/storydata/tour_calendar.py): their names, tiers, end dates, venues and prize pools.
/// Formats stay the game's for each tier, and who plays comes from the career's own rankings.
/// Years after the data, and normal careers, keep the game's own calendar.
[HarmonyPatch]
internal static class StoryCalendar
{
    sealed record Event(string name, string tier, string start, string end, int prize, bool lan,
                        string country, string city, string page);

    sealed record Calendar(int last_year, List<Event> events);

    static Calendar? _calendar;
    static bool _loaded;

    static Calendar? Data
    {
        get
        {
            if (_loaded) return _calendar;
            _loaded = true;
            var path = Path.Combine(BepInEx.Paths.PluginPath, "RealismOverhaul", "story", "calendar.json");
            try
            {
                if (File.Exists(path)) _calendar = JsonSerializer.Deserialize<Calendar>(File.ReadAllText(path));
                Plugin.Log.LogInfo($"[StoryCalendar] {_calendar?.events.Count ?? 0} historical events up to {_calendar?.last_year}");
            }
            catch (Exception e) { Plugin.Log.LogError($"[StoryCalendar] could not read {path}: {e.Message}"); }
            return _calendar;
        }
    }

    /// Real prize pools of the events just scheduled, for Setup (which otherwise prices by tier).
    static readonly Dictionary<IntPtr, int> RealPrize = new();

    [HarmonyPatch(typeof(TournamentYearScheduler), nameof(TournamentYearScheduler.ScheduleYear)), HarmonyPrefix]
    static bool RealYear(Il2CppSystem.Collections.Generic.IReadOnlyList<DataTournament> tournaments, int year)
    {
        if (StoryStart.CareerStart is not { } start || Data is not { } cal || year > cal.last_year) return true;
        try
        {
            var list = tournaments.TryCast<Il2CppSystem.Collections.Generic.List<DataTournament>>();
            if (list == null) return true;
            // History before the Start Date is not replayed (ADR 0002).
            var events = cal.events.Where(e => e.start[..4] == year.ToString() && string.CompareOrdinal(e.start, start) >= 0).ToList();
            if (events.Count == 0) return true;

            var pool = new Dictionary<TournamentType, Queue<DataTournament>>();
            void Refill(List<DataTournament>? first = null)
            {
                foreach (var t in (IEnumerable<DataTournament>?)first ?? Enumerate(DataTournamnetTemplates.GetNewTournaments()))
                {
                    if (!pool.TryGetValue(t.Tier, out var q)) pool[t.Tier] = q = new Queue<DataTournament>();
                    q.Enqueue(t);
                }
            }
            Refill(Enumerate(list).ToList());
            list.Clear();
            RealPrize.Clear();

            foreach (var e in events)
            {
                var tier = TierOf(e.tier);
                if (!pool.TryGetValue(tier, out var q) || q.Count == 0) Refill();
                if (!pool.TryGetValue(tier, out q) || q.Count == 0) continue; // the game has no template of that tier
                var t = q.Dequeue();
                t.Name = e.name;
                t.Description = "";
                t.CupId = Cup(e.name);
                t.IsLan = e.lan;
                t.Country = e.country;
                t.City = e.lan ? e.city : "";
                var end = DateTime.Parse(e.end);
                t.DateEnd = new Il2CppSystem.DateTime(end.Year, end.Month, end.Day);
                t.HasSchedule = true;
                if (e.prize > 0) RealPrize[t.Pointer] = e.prize;
                list.Add(t);
            }
            Plugin.Log.LogInfo($"[StoryCalendar] {year}: {list.Count} real events ({string.Join(", ", Enumerate(list).GroupBy(t => t.Tier).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"))})");
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[StoryCalendar] {year}: {e}");
            return true;
        }
    }

    [HarmonyPatch(typeof(DataTournament), nameof(DataTournament.Setup)), HarmonyPrefix]
    static void RealMoney(DataTournament __instance, ref int prizeFund)
    {
        if (RealPrize.Remove(__instance.Pointer, out var prize)) prizeFund = prize;
    }

    static TournamentType TierOf(string tier) => tier switch
    {
        "Major" => TournamentType.Major,
        "T1" => TournamentType.T1,
        "T2" => TournamentType.T2,
        "T3" => TournamentType.T3,
        _ => TournamentType.T4,
    };

    /// One cup across the years: the name without its year or season ("ESL One: Cologne 2016").
    static string Cup(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"\s*\b(19|20)\d\d\b|\s*-?\s*Season\s+\S+", "").Trim();

    static IEnumerable<DataTournament> Enumerate(Il2CppSystem.Collections.Generic.List<DataTournament> list)
    {
        for (var i = 0; i < list.Count; i++) yield return list[i];
    }
}
