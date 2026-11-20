# Realism Overhaul & Story Mode for Esports Manager 2026

Two mods for Esports Manager 2026:

- **Realism Overhaul** makes the base game play more like professional CS2.
- **Story Mode** (planned) starts a career at a historical date, from the first CS tournaments in 2000 onward.

> **Status: early development.** One feature works in-game so far. Expect breaking changes, and back up your saves.

Not affiliated with or endorsed by the game's developer.

## Features

### Realism Overhaul

| Feature | Status |
|---|---|
| **Overage Player:** an Academy may hold one player older than 23 (sign, refuse, move down) | ✅ Works |
| AI Academies sometimes keep one player who aged out (preferably their IGL) | ✅ Works |
| Register for a tournament while playing another, when their dates do not overlap | ✅ Works |
| Overlapping tournaments (at most 2, never 2 LANs), Double Match Days with fatigue | ✅ Works |
| Academy matches stop the calendar like Main Roster matches (watch or simulate) | ✅ Works |
| Retired players: 'Hire as coach' button, Comeback within 5 years (skills decay with time away) | ✅ Works |
| Former-player coach as Emergency Sub (instead of a forfeit) | 🧪 Built, not yet verified |
| Realistic Roles (Entry, Trader, Support, Lurker, AWPer, Anchor) with Role Balance in matches | ✅ Works |
| CT Positions per map (assigned by Role; manual setup screen planned) | ✅ Works |
| Map Familiarity and CT/T Side Bias (saved in a sidecar file next to the save) | ✅ Works |
| Coach Roster Suggestions when a player is in a Performance Slump | ✅ Works |
| Subregions and Regional Tournaments | 🧪 Built, not yet verified |
| VRS invites based on Valve's published Regional Standings formula (Region quotas) | ✅ Works (history builds over 6 months) |

### Story Mode (planned)

- Start at curated historical dates from 2000, with rosters and rankings accurate to that date.
- Real tournament calendar up to the present, with participants decided by your save.
- Game Updates (weapon changes, 1.6 → CS:GO → CS2) on their real dates, affecting players.

Terms are defined in [GLOSSARY.md](GLOSSARY.md); design decisions are in [docs/adr/](docs/adr/).

## Requirements

- Esports Manager 2026 on Windows (tested on the build using Unity 6000.5.10f1).
- [BepInEx 6 Bleeding Edge build #785](https://builds.bepinex.dev/projects/bepinex_be), Unity IL2CPP x64.
- A patched Cpp2IL and a patched Il2CppInterop, because no official BepInEx build supports this game's Unity version (6000.5, IL2CPP metadata v107) yet. See [Patched Cpp2IL](#patched-cpp2il) and [Patched Il2CppInterop](#patched-il2cppinterop).

> Install BepInEx only from the official build server. Do not use third-party repacks: one repack for this game was quarantined by Nexus Mods for containing a trojan (November 2026).

## Installation

1. **Back up your saves:** `%USERPROFILE%\AppData\LocalLow\NeuronaGames\EsportsManager\Save`.
2. Download `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785+6abdba4.zip` from the [official build server](https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip) and extract it into the game folder (the one with `EsportsManager.exe`).
3. Build the patched Cpp2IL and the patched Il2CppInterop, and copy their DLLs into `BepInEx\core` (see below).
4. Launch the game once and wait for the main menu. The first launch generates interop assemblies and takes a few minutes.
5. Build the mod (see [Building](#building)) and copy `RealismOverhaul.dll` to `BepInEx\plugins\RealismOverhaul\`.

To uninstall, delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, and the `BepInEx` and `dotnet` folders from the game folder.

### Patched Cpp2IL

The patches in [patches/cpp2il-v107-backport/](patches/cpp2il-v107-backport/) add metadata v107 support to the exact Cpp2IL version that BepInEx #785 ships. The reasons are in [ADR 0001](docs/adr/0001-bepinex-plugin-plus-emdb-data.md).

Run from the folder that contains this repository (`<this-repo>` below):

```sh
git clone https://github.com/SamboyCoding/Cpp2IL.git
cd Cpp2IL
git checkout -b backport-v107 558ddd9
git am ../<this-repo>/patches/cpp2il-v107-backport/*.patch
dotnet build Cpp2IL.Core/Cpp2IL.Core.csproj -c Release -f netstandard2.0 -o out
```

Copy `Cpp2IL.Core.dll`, `LibCpp2IL.dll`, `StableNameDotNet.dll` and `WasmDisassembler.dll` from `out` into `BepInEx\core`, replacing the originals. Patch `0004` points `global.json` at .NET SDK 10.0.400; edit it if you use a different SDK.

### Patched Il2CppInterop

Il2CppInterop 1.5.3 (bundled with BepInEx #785) lacks Unity 6000.5's native struct layouts, which breaks class injection. The patch in [patches/il2cppinterop-6000.5/](patches/il2cppinterop-6000.5/) adds them, ported from [ds5678/Il2CppInterop](https://github.com/ds5678/Il2CppInterop).

```sh
git clone https://github.com/BepInEx/Il2CppInterop.git
cd Il2CppInterop
git checkout -b em2026 dbda1cb
git am ../<this-repo>/patches/il2cppinterop-6000.5/*.patch
dotnet build Il2CppInterop.Runtime/Il2CppInterop.Runtime.csproj -c Release -o out
```

Copy `Il2CppInterop.Runtime.dll` from `out` into `BepInEx\core`, replacing the original. On startup the mod logs `[SelfTest] class injection OK` when this works.

## Building

Requires the .NET SDK (6.0 or later) and a game folder where BepInEx has already generated its interop assemblies.

```sh
cd src/RealismOverhaul
dotnet build -c Release -p:Deploy=true
```

`Deploy=true` copies the DLL into the game's `BepInEx\plugins\RealismOverhaul\` folder. If the game is not in the default Steam location, add `-p:GameDir="<path to game folder>"`.

## Performance

The game ships with no frame-rate cap and vSync off, and keeps running in the background. The mod caps it at 60 FPS while focused (`ForegroundFpsCap`) and 10 FPS while unfocused or minimized (`BackgroundFps`), in `[Performance]`.

## Configuration

After the first launch with the mod, edit `BepInEx\config\lga.esportsmanager2026.realismoverhaul.cfg`. Every feature has its own on/off switch, plus tuning values such as `AiOverageRetentionChance`.

## Developer tools

- [tools/CodeMap](tools/CodeMap/): dumps type and member signatures from the game's assemblies.
- [tools/Disasm](tools/Disasm/): statically disassembles an IL2CPP method from `GameAssembly.dll` and names its call targets. Useful when a rule is compiled into native code and has no constant to change.

Both only read files. Their output is derived from the game and is not committed.

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx), [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) and [HarmonyX](https://github.com/BepInEx/HarmonyX). The Unity 6000.5 struct layouts come from [ds5678/Il2CppInterop](https://github.com/ds5678/Il2CppInterop).
- [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) by Samboy063 and contributors (MIT). The backport patches are their work, applied to an older version.
- [Iced](https://github.com/icedland/iced), the x86 disassembler used by `tools/Disasm`.

## License

[MIT](LICENSE).
