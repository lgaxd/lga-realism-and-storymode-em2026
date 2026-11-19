# Mod data lives in a sidecar file next to each save, never inside the game's .mpack

The Realism Overhaul's own data (Map Familiarity, Role/CT Position, VRS history, Roster Suggestion cooldowns, Overage status) is stored in a separate file beside each save slot instead of being written into the game's MessagePack save. A bad write, an uninstall, or a game update that breaks the plugin then costs only the mod's data, and the save still loads in the vanilla game. Existing careers are supported: when a save has no sidecar file, the mod builds the missing data from the game's current state the first time that save is loaded.

The file is `Save/<slotId>/RealismOverhaul.json`, inside the game's own slot folder, so the game's slot delete and backup carry it along. It is read after `SlotController.LoadSlot` and written on every `SlotController.EnqueueSave` (manual saves and autosaves), through a temporary file and an atomic rename.

## Consequences

- The sidecar file and the save can drift apart (for example, if you load an older copy of a save). The mod must detect that a sidecar file doesn't match its save and rebuild it.
- Every feature can be switched on or off in config. A disabled feature leaves its sidecar data untouched.
