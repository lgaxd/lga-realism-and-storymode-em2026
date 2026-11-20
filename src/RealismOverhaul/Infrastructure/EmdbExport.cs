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
        var sources = new List<(string Path, string Folder)>
        {
            (System.IO.Path.Combine(Application.streamingAssetsPath, "default.emdb"), "RealismOverhaul-emdb"),
        };
        // Extra databases (e.g. a community .emdb with real names, for calibration).
        foreach (var extra in Plugin.Flags.ExportExtraDatabases.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            sources.Add((extra, "RealismOverhaul-emdb-" + System.IO.Path.GetFileNameWithoutExtension(extra)));

        var ok = true;
        foreach (var (source, folder) in sources)
        {
            try
            {
                var outDir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, folder);
                Directory.CreateDirectory(outDir);
                var tables = EmdbExtractor.ExtractEmdbToMemory(source);
                var count = 0;
                foreach (var key in tables.Keys)
                {
                    var name = string.Concat(key.Split(System.IO.Path.GetInvalidFileNameChars()));
                    File.WriteAllText(System.IO.Path.Combine(outDir, name), tables[key]);
                    count++;
                }
                Plugin.Log.LogInfo($"[EmdbExport] exported {count} tables from {source} to {outDir}");
            }
            catch (Exception e)
            {
                ok = false;
                Plugin.Log.LogError($"[EmdbExport] {source} failed: {e.Message}");
            }
        }
        if (ok) Plugin.Flags.ExportDefaultDatabase.Value = false; // one shot
    }
}
