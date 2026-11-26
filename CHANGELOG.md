# Changelog

All notable changes to **AutoResumeSurvival** will be documented in this file.

## [1.0.2] - 25-Nov-26

### Added

- If BackgroundLoading is not installed and the game window is unfocused during auto-resume, logs a one-time tip recommending that mod.

### Changed

- Pause now uses `InputManager.PauseGame()` instead of a synthesized Escape key press, and waits until the load is finished and `CanInvokePauseMenu()` is true.
- Resume now calls `Panel_Sandbox.OnClickResume()` once the Survival menu is enabled, instead of scanning every GameObject for a `MenuItem1` widget or waiting a fixed delay.
- Loading and in-gameplay detection now use `InterfaceManager` and `GameManager` APIs instead of searching for loading-panel objects or matching scene names.
- After RESUME is triggered, Left Shift or Esc skips only the auto-pause, and the on-screen message says so.
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