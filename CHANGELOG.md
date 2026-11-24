# Changelog

All notable changes to **AutoResumeSurvival** will be documented in this file.

## [1.0.1] - 23-NOV-2026

### Added
- On-screen `"Too late, save loading!"` status prompt displayed once `RESUME` is triggered and dismissed immediately before sending the pause `Escape` key press.
- `#nullable enable` annotations context in `Patches.cs` for clean compilation without project-wide nullable reference type warnings.

### Fixed
- Removed duplicate unreachable readiness check block in `Stage.WaitingToPauseGameplay`.