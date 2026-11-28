# Auto Resume Survival

<img width="960" height="540" alt="Auto Resume Survival cover" src="https://raw.githubusercontent.com/leatherneck6017/TLD-AutoResumeSurvival/refs/heads/master/AutoResumeSurvival-Cover.png" />

Does your game take forever to load into your SURVIVAL save? Do you wish you could start in a paused state so that you can take a moment to breathe before your character starts to freeze and starve again? Well, Auto Resume Survival is here to help!

Auto Resume Survival is a lightweight mod for The Long Dark that automatically navigates from game startup to the SURVIVAL mode menu, selects RESUME, and opens the pause menu once your save finishes loading so that you start in a paused state.

## Features

- Automatic MAIN MENU to SURVIVAL mode navigation.
- Automatic selection of RESUME.
- Dynamic readiness detection that waits for loading panels and screen fade-ins to finish before opening the pause menu.
- On-screen prompt displaying how to cancel auto-resume while active.

## Requirements

- The Long Dark (tested on v2.60)
- MelonLoader v0.7.2

## Recommended

- **BackgroundLoading** keeps the game loading while its window is unfocused. Auto Resume Survival does not require it, but if that mod is missing and the window is unfocused during startup, a one-time tip is written to the MelonLoader log.

## Installation

1. Install MelonLoader v0.7.2 for The Long Dark if you have not already.
2. Place `AutoResumeSurvival.dll` into your game's `Mods` folder (for example, `E:\SteamLibrary\steamapps\common\TheLongDark\Mods`).
3. Launch the game.

## Usage

When the MAIN MENU appears, Auto Resume Survival displays an on-screen prompt indicating that auto-resume is active and how to cancel. LEFT SHIFT or ESC also cancel during the earlier load, before that prompt is shown.

- Let it run to jump straight into your SURVIVAL save and pause automatically once the world finishes loading.
- Press LEFT SHIFT or ESC while the first prompt is displayed to cancel auto-resume and take manual control.
- After RESUME has been triggered, LEFT SHIFT or ESC skips only the auto-pause; the save still finishes loading.

## Building from Source

1. Clone or open the project in Visual Studio / .NET SDK targeting `net6.0`.
2. Update `TLDPath` in `AutoResumeSurvival.csproj` to point to your The Long Dark installation directory (currently set to `E:\SteamLibrary\steamapps\common\TheLongDark`).
3. Build the project so references from `$(TLDPath)\MelonLoader\net6\*.dll` and `$(TLDPath)\MelonLoader\Il2CppAssemblies\*.dll` resolve.
4. On a successful build, `BuildActions.targets` copies `AutoResumeSurvival.dll` into your game's `Mods` folder automatically.

## Issues

If you encounter any, please report them on the [GitHub Issues](https://github.com/leatherneck6017/TLD-AutoResumeSurvival/issues) page or contact me directly on the [TLD Modding Discord](https://discord.gg/tldmodding) (@leatherneck6017).

## AI Disclaimer

AI tools were consulted during the development of this mod as part of my learning process for exploring The Long Dark's IL2CPP APIs, Harmony patching, and troubleshooting. All code was tested and verified in-game.

## License

This project is open source and available under the [MIT License](LICENSE.txt).
