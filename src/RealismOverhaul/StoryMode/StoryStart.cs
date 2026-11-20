using System.Text.Json.Nodes;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using RealismOverhaul.Infrastructure;
using TMPro;
using UnityEngine;

namespace RealismOverhaul.StoryMode;

/// Starting a Story Mode career (ADR 0002, 0004, 0005).
///
/// A start pack is a folder of game tables for one Start Date (plugins/RealismOverhaul/story/
/// yyyy-MM-dd, built by tools/storydata/snapshot.py --install). The Database screen gets a Story
/// Mode panel: picking a Start Date packs its tables into a database with the game's own packer
/// (EmdbExtractor) and loads it with the game's own loader (EmdbApplier), exactly as an uploaded
/// .emdb. The player then starts a new career as usual, choosing a team of that world; the
/// contract's start year is locked to the Start Date, and the career is marked as Story Mode in
/// its sidecar.
public static class StoryStart
{
    /// The Start Date of the world loaded on the Database screen, until a career is started from it.
    internal static string? PendingStart;

    /// The Start Date of the career being played, or null for a normal career.
    internal static string? CareerStart;

    static string PacksDir => Path.Combine(BepInEx.Paths.PluginPath, "RealismOverhaul", "story");

    public static void Apply(Harmony harmony)
    {
        Sidecar.Register(new Section());
        Sidecar.Apply(harmony);
        harmony.PatchAll(typeof(StoryStart));
        ClassInjector.RegisterTypeInIl2Cpp<StoryLoaderBehaviour>();
        EraIndicator.Start();
    }

    internal static List<string> Packs() =>
        Directory.Exists(PacksDir)
            ? Directory.GetDirectories(PacksDir).Select(Path.GetFileName).Where(n => n != null && File.Exists(Path.Combine(PacksDir, n, "Players.csv"))).Select(n => n!).OrderBy(n => n).ToList()
            : new List<string>();

    static int Year(string start) => int.Parse(start[..4]);

    // ---------------------------------------------------------------- Database screen panel

    [HarmonyPatch(typeof(DatabaseMainMenu), nameof(DatabaseMainMenu.OnEnable)), HarmonyPostfix]
    static void AddPanel(DatabaseMainMenu __instance)
    {
        try
        {
            if (__instance.transform.Find("RealismOverhaul.StoryMode") != null) return;
            var packs = Packs();
            var style = __instance.GetComponentInChildren<TMP_Text>(true);
            var template = UiKit.FindDropdownTemplate();
            if (style == null || template == null)
            {
                Plugin.Log.LogWarning("[StoryStart] Database screen: no text style or dropdown template to build the panel");
                return;
            }
            var pt = UiKit.IsPortuguese();
            var panel = UiKit.Image(__instance.transform, "RealismOverhaul.StoryMode", new Color(0f, 0f, 0f, 0.55f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(760f, 132f));
            UiKit.Text(panel.transform, "Title", style, pt ? "MODO HISTÓRIA" : "STORY MODE", 26f, new Color(1f, 0.78f, 0.2f),
                TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(400f, 34f));
            var status = UiKit.Text(panel.transform, "Status", style,
                packs.Count == 0 ? (pt ? "Nenhuma data de início instalada (tools/storydata/snapshot.py --install)." : "No start dates installed (tools/storydata/snapshot.py --install).")
                    : (pt ? "Escolha a data de início: o mundo daquele dia é carregado; depois crie a carreira." : "Pick a start date: that day's world is loaded; then start a new career."),
                17f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 12f), new Vector2(720f, 30f));
            if (packs.Count == 0) return;
            var choice = 0;
            UiKit.Dropdown(template, panel.transform, "StartDate", packs.Select(Label).ToList(), 0,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -52f), new Vector2(380f, 40f), i => choice = i);
            UiKit.TextButton(panel.transform, "Load", style, pt ? "CARREGAR MUNDO" : "LOAD WORLD", 20f,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(420f, -52f), new Vector2(300f, 40f),
                () => Load(__instance, packs[choice], status));
        }
        catch (Exception e) { Plugin.Log.LogError($"[StoryStart] panel failed: {e}"); }
    }

    static string Label(string start)
    {
        var era = Eras.At(DateTime.ParseExact(start, "yyyy-MM-dd", null));
        return era != null ? $"{start}  ·  {era.Name}" : start;
    }

    // ---------------------------------------------------------------- loading a start pack

    static void Load(DatabaseMainMenu menu, string start, TMP_Text status)
    {
        if (StoryLoaderBehaviour.Busy) return;
        try
        {
            var dir = Path.Combine(PacksDir, start);
            var tables = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
            foreach (var file in Directory.GetFiles(dir))
                if (file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    tables[Path.GetFileName(file)] = File.ReadAllText(file);
            // Same format version as the game's own database.
            var defaultRaw = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "default.emdb"));
            var version = defaultRaw[EmdbExtractor.MagicLen];
            var zip = EmdbExtractor.BuildMinimalZip(tables);
            var emdb = EmdbExtractor.BuildEmdb(version, zip);
            var path = Path.Combine(PacksDir, $"{start}.emdb");
            File.WriteAllBytes(path, emdb.ToArray());
            Plugin.Log.LogInfo($"[StoryStart] built {path} ({tables.Count} tables)");
            status.text = UiKit.IsPortuguese() ? $"Carregando o mundo de {start}…" : $"Loading the world of {start}…";
            var task = EmdbApplier.ApplyAsync(path, menu._importQueue, menu._orgList, null);
            StoryLoaderBehaviour.Watch(task, start, menu, status);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[StoryStart] could not load {start}: {e}");
            status.text = (UiKit.IsPortuguese() ? "Falhou: " : "Failed: ") + e.Message;
        }
    }

    internal static void Loaded(string start, EmdbApplyResult? result, DatabaseMainMenu menu, TMP_Text status)
    {
        var pt = UiKit.IsPortuguese();
        if (result == null || !result.Success)
        {
            Plugin.Log.LogError($"[StoryStart] {start}: the game refused the world: {result?.ErrorMessage}");
            status.text = (pt ? "O jogo recusou o mundo: " : "The game refused the world: ") + result?.ErrorMessage;
            return;
        }
        PendingStart = start;
        Plugin.Log.LogInfo($"[StoryStart] world of {start} loaded: {result.Teams} teams, {result.Players} players, {result.Staff} staff");
        status.text = pt ? $"Mundo de {start} carregado ({result.Teams} times). Agora crie um novo jogo."
                         : $"World of {start} loaded ({result.Teams} teams). Now start a new game.";
        try { menu.UpdateInformationRows(); } catch { /* cosmetic */ }
    }

    // ---------------------------------------------------------------- the contract's start year

    [HarmonyPatch(typeof(ContractView), nameof(ContractView.ShowStartYears)), HarmonyPrefix]
    static void AllowStoryYear()
    {
        if (PendingStart != null && CareerStartYear.Earliest > Year(PendingStart))
            CareerStartYear.Earliest = Year(PendingStart);
    }

    [HarmonyPatch(typeof(ContractView), nameof(ContractView.ShowStartYears)), HarmonyPostfix]
    static void LockStoryYear(ContractView __instance)
    {
        if (PendingStart == null) return;
        try
        {
            var year = Year(PendingStart);
            __instance._startYearValue = year;
            var dd = __instance._startYear;
            if (dd != null)
            {
                UiKit.SetOptions(dd, new[] { $"{year} · {(UiKit.IsPortuguese() ? "Modo História" : "Story Mode")}" }, 0);
                dd.interactable = false;
            }
            Plugin.Log.LogInfo($"[StoryStart] contract start year locked to {year} (contract date {__instance._contractDate})");
        }
        catch (Exception e) { Plugin.Log.LogError($"[StoryStart] could not lock the start year: {e}"); }
    }

    [HarmonyPatch(typeof(MainMenuInitializer), nameof(MainMenuInitializer.ConfirmContract)), HarmonyPrefix]
    static void StartStoryCareer(ref int startYear)
    {
        if (PendingStart == null)
        {
            CareerStart = null;
            return;
        }
        startYear = Year(PendingStart);
        CareerStart = PendingStart;
        PendingStart = null;
        Plugin.Log.LogInfo($"[StoryStart] Story Mode career started on {CareerStart}");
    }

    // ---------------------------------------------------------------- the career's sidecar

    sealed class Section : Sidecar.ISection
    {
        public string Name => "story";

        public void Load(JsonNode? data)
        {
            CareerStart = data is JsonObject o ? o["start"]?.GetValue<string>() : null;
            if (CareerStart != null)
                Plugin.Log.LogInfo($"[StoryStart] Story Mode career (started {CareerStart})");
        }

        public JsonNode? Save() => CareerStart == null ? null : new JsonObject { ["start"] = CareerStart };
    }
}

/// Polls the game's asynchronous database load (Unity Awaitable) from the main thread.
public class StoryLoaderBehaviour : MonoBehaviour
{
    public StoryLoaderBehaviour(IntPtr ptr) : base(ptr) { }

    static Awaitable<EmdbApplyResult>? _task;
    static string _start = "";
    static DatabaseMainMenu? _menu;
    static TMP_Text? _status;
    static GameObject? _host;

    internal static bool Busy => _task != null;

    internal static void Watch(Awaitable<EmdbApplyResult> task, string start, DatabaseMainMenu menu, TMP_Text status)
    {
        _task = task;
        _start = start;
        _menu = menu;
        _status = status;
        if (_host == null)
        {
            _host = new GameObject("RealismOverhaul.StoryLoader");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<StoryLoaderBehaviour>();
        }
    }

    void Update()
    {
        if (_task == null) return;
        var awaiter = _task.GetAwaiter();
        if (!awaiter.IsCompleted) return;
        EmdbApplyResult? result = null;
        try { result = awaiter.GetResult(); }
        catch (Exception e) { Plugin.Log.LogError($"[StoryStart] load failed: {e.Message}"); }
        _task = null;
        if (_menu != null && _status != null) StoryStart.Loaded(_start, result, _menu, _status);
    }
}
