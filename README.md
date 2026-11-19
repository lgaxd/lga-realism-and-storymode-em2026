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
| AI Academies sometimes keep one player who aged out (always their IGL) | ✅ Works |
| Register for a tournament while playing another, when their dates do not overlap | ✅ Works |
| Overlapping tournaments (one LAN + Online), playing both on the same day | Planned |
| Academy matches stop the calendar like Main Roster matches (watch or simulate) | ✅ Works |
| Retired players: hire as coach, Comeback within 5 years, coach as Emergency Sub | Planned |
| Realistic Roles (Entry, Trader, Support, Lurker, AWPer, Anchor) + IGL + CT Positions per map | Planned |
| Map Familiarity and CT/T Side Bias | Planned |
| Coach Roster Suggestions when a player is in a Performance Slump | Planned |
| Subregions and Regional Tournaments | Planned |
| VRS invites based on Valve's published Regional Standings formula | Planned |

### Story Mode (planned)

- Start at curated historical dates from 2000, with rosters and rankings accurate to that date.
- Real tournament calendar up to the present, with participants decided by your save.
- Game Updates (weapon changes, 1.6 → CS:GO → CS2) on their real dates, affecting players.

Terms are defined in [GLOSSARY.md](GLOSSARY.md); design decisions are in [docs/adr/](docs/adr/).

## Requirements

- Esports Manager 2026 on Windows (tested on the build using Unity 6000.5.10f1).
- [BepInEx 6 Bleeding Edge build #785](https://builds.bepinex.dev/projects/bepinex_be), Unity IL2CPP x64.
- A patched Cpp2IL, because no official BepInEx build reads this game's IL2CPP metadata (v107) yet. See [Patched Cpp2IL](#patched-cpp2il).

> Install BepInEx only from the official build server. Do not use third-party repacks: one repack for this game was quarantined by Nexus Mods for containing a trojan (November 2026).

## Installation

1. **Back up your saves:** `%USERPROFILE%\AppData\LocalLow\NeuronaGames\EsportsManager\Save`.
2. Download `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785+6abdba4.zip` from the [official build server](https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip) and extract it into the game folder (the one with `EsportsManager.exe`).
3. Build the patched Cpp2IL and copy its four DLLs into `BepInEx\core` (see below).
4. Open `BepInEx\config\BepInEx.cfg` (created after the first launch) and set `UnityLogListening = false`. Unity log hooking needs class injection, which does not work on this Unity version yet.
5. Launch the game once and wait for the main menu. The first launch generates interop assemblies and takes a few minutes.
6. Build the mod (see [Building](#building)) and copy `RealismOverhaul.dll` to `BepInEx\plugins\RealismOverhaul\`.

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

## Building

Requires the .NET SDK (6.0 or later) and a game folder where BepInEx has already generated its interop assemblies.

```sh
cd src/RealismOverhaul
dotnet build -c Release -p:Deploy=true
```

`Deploy=true` copies the DLL into the game's `BepInEx\plugins\RealismOverhaul\` folder. If the game is not in the default Steam location, add `-p:GameDir="<path to game folder>"`.

## Configuration

After the first launch with the mod, edit `BepInEx\config\lga.esportsmanager2026.realismoverhaul.cfg`. Every feature has its own on/off switch, plus tuning values such as `AiOverageRetentionChance`.

## Developer tools

- [tools/CodeMap](tools/CodeMap/): dumps type and member signatures from the game's assemblies.
- [tools/Disasm](tools/Disasm/): statically disassembles an IL2CPP method from `GameAssembly.dll` and names its call targets. Useful when a rule is compiled into native code and has no constant to change.

Both only read files. Their output is derived from the game and is not committed.

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx), [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) and [HarmonyX](https://github.com/BepInEx/HarmonyX).
- [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) by Samboy063 and contributors (MIT). The backport patches are their work, applied to an older version.
- [Iced](https://github.com/icedland/iced), the x86 disassembler used by `tools/Disasm`.

## License

[MIT](LICENSE).
