# Game logic via a BepInEx plugin; historical data via EMDB where possible

Esports Manager 2026 is a Unity IL2CPP build with no official code-modding API; its Steam Workshop only accepts `.emdb` databases, which hold data and no rules. Almost every requested feature changes game rules, so the mods are BepInEx 6 (IL2CPP) plugins that patch game methods with HarmonyX, installed from the official BleedingEdge build server (builds.bepinex.dev, build #785, the build the community pins for the game's IL2CPP metadata v39). We do not use the community BepInExPack/ModKit from Nexus or the EM Framework: Nexus quarantined the ModKit download for a trojan (Nov 2026), so we install nothing from third-party repacks. Story Mode's historical snapshots ship as generated `.emdb` files if we can produce the format; otherwise the plugin injects that data itself.

## Considered Options

- **EMDB only**: stable and shareable on the Workshop, but it cannot change any rules, so most features are impossible.
- **Plugin only**: works, but it skips the game's own database pipeline for data the game already knows how to load.

## Patched Cpp2IL for metadata v107

The game shipped on Unity 6000.5.10f1, which uses IL2CPP metadata v107. The Cpp2IL bundled in every official BepInEx build so far (#785–#793, Cpp2IL `development.1452` / `558ddd9`) reads only up to v106. Upstream Cpp2IL added v107 in `01c1748`, but current Cpp2IL needs AsmResolver 6.0.1, which BepInEx's Il2CppInterop 1.5.3 (built on AsmResolver 6.0.0-beta.5) cannot load. So we cherry-pick `70164e0`, `01c1748` and `b20ca0d` onto `558ddd9`, apply small fixups (`patches/cpp2il-v107-backport/`), build `Cpp2IL.Core` for netstandard2.0, and swap `Cpp2IL.Core.dll`, `LibCpp2IL.dll`, `StableNameDotNet.dll` and `WasmDisassembler.dll` into `BepInEx/core`. The originals are kept in `vendor/bepinex-core-original/`. Once an official BepInEx build ships v107 support, drop this patch.

## Patched Il2CppInterop for Unity 6000.5 class injection

Il2CppInterop 1.5.3, bundled with BepInEx #785, has no native struct layouts newer than Unity 2023.1. On 6000.5 that breaks class injection (our own MonoBehaviours) and delegate conversion (callbacks into the game), and with them any custom UI. Only `Il2CppClass` and `Il2CppAssembly` changed in 6000.x. We port their 6000.5 layouts from ds5678/Il2CppInterop's v2-rewrite (PR #36) onto 1.5.3 (`patches/il2cppinterop-6000.5/`) and swap `Il2CppInterop.Runtime.dll` in `BepInEx/core`. The plugin's startup self-test verifies it. We considered working around the missing injection (cloning game buttons, polling input from a game `Update`, routing everything through emails), but rejected it: fixing injection once unlocks UI for every later feature.

## Consequences

- Game updates can break the patches. Each mod release targets a specific game version.
- Players must install BepInEx BE #785 (official build) before the mods.
- We write our own helper layer instead of reusing EM Framework.
