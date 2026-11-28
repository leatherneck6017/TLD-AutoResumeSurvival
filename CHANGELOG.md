# Changelog

All notable changes to **Auto Resume Survival** will be documented in this file.

## [1.0.3] - 27-Nov-26

### Changed

- The on-screen cancel prompt is drawn only after the MAIN MENU panel is visible, so OnGUI does no IMGUI work during boot and pre-menu loading. LEFT SHIFT or ESC still cancel during that earlier period.
- `SaveGameSlotHelper.RefreshSaveSlots` and `Panel_Sandbox.OnClickResume()` now run on separate frames, and OnGUI is skipped on the frame that starts the save load (and the frame that opens the pause menu).
- OnGUI returns immediately when there is nothing to draw.

The one-frame hitch when RESUME is invoked is the game loading the save (`GameManager.LoadSaveGameSlot`) inside `OnClickResume()`. A manual click on RESUME would hitch the same way; StutterProfiler was billing it to Auto Resume Survival because the call ran from `OnUpdate`.

## [1.0.2] - 25-Nov-26

### Added

- If BackgroundLoading is not installed and the game window is unfocused during auto-resume, logs a one-time tip recommending that mod.

### Changed

- Pause now uses `InputManager.PauseGame()` instead of a synthesized Escape key press, and waits until the load is finished and `CanInvokePauseMenu()` is true.
- Resume now calls `Panel_Sandbox.OnClickResume()` once the SURVIVAL menu is enabled, instead of scanning every GameObject for a `MenuItem1` widget or waiting a fixed delay.
- Loading and in-gameplay detection now use `InterfaceManager` and `GameManager` APIs instead of searching for loading-panel objects or matching scene names.
- After RESUME is triggered, LEFT SHIFT or ESC skips only the auto-pause, and the on-screen message says so.
- Auto-resume stops immediately (after a short slot-populate wait) if no Survival save is present.
- The sequence is driven only by `MelonMod.OnUpdate` / `OnGUI`. The injected `AutoResumeRunner` MonoBehaviour and the Win32 `keybd_event` P/Invoke are gone.

### Fixed

- `MelonInfo` version now matches the release tag (it was still `1.0.0` on 1.0.1).

## [1.0.1] - 23-Nov-26

### Added
- On-screen `"Too late, save loading!"` status prompt displayed once `RESUME` is triggered and dismissed immediately before sending the pause `Escape` key press.
- `#nullable enable` annotations context in `Patches.cs` for clean compilation without project-wide nullable reference type warnings.

### Fixed
- Removed duplicate unreachable readiness check block in `Stage.WaitingToPauseGameplay`.