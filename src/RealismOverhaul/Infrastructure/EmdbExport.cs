using UnityEngine;

namespace RealismOverhaul.Infrastructure;

/// Diagnostics (one shot): exports the CSV tables inside the game's default database
/// (StreamingAssets/default.emdb) to BepInEx/RealismOverhaul-emdb/, using the game's own reader
/// (EmdbExtractor). Story Mode needs the exact table schema to build historical worlds, and the
/// 2022 database is the reference its ratings are calibrated against. The export is the game's
/// data: it stays on this machine and is never committed.
public static class EmdbExport
{
    public static void Run()
    {
        try
        {
            var source = Path.Combine(Application.streamingAssetsPath, "default.emdb");
            var outDir = Path.Combine(BepInEx.Paths.BepInExRootPath, "RealismOverhaul-emdb");
            Directory.CreateDirectory(outDir);
            var tables = EmdbExtractor.ExtractEmdbToMemory(source);
            var count = 0;
            foreach (var key in tables.Keys)
            {
                var name = string.Concat(key.Split(Path.GetInvalidFileNameChars()));
                File.WriteAllText(Path.Combine(outDir, name), tables[key]);
                count++;
            }
            Plugin.Log.LogInfo($"[EmdbExport] exported {count} tables from {source} to {outDir}");
            Plugin.Flags.ExportDefaultDatabase.Value = false; // one shot
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[EmdbExport] failed: {e}");
        }
    }
}
