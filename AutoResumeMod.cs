
using System;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace AutoResumeSurvival
{
    public class AutoResumeMod : MelonMod
    {
        private enum Stage
        {
            WaitingForMainMenu,
            EnteringSurvival,
            WaitingForSurvivalScreen,
            ClickingResume,
            WaitingToPauseGameplay,
            Done
        }

        private Stage _stage = Stage.WaitingForMainMenu;
        private bool _resumeClicked;
        private bool _skipAutoPause;
        private bool _showCancelPrompt = true;
        private bool _showSaveLoadingMessage;
        private bool _showNoSaveMessage;
        private bool _sandboxSlotsRefreshed;
        private float _cancelConfirmUntilTime;
        private float _stageEnteredTime;
        private float _lastStatusLogTime;
        private float _readySinceTime;
        private int _consecutiveReadyFrames;
        private float _profileReadySinceTime;
        private bool _sawGameplay;
        private float _gameplaySinceTime;
        private bool _loggedBackgroundLoadingTip;
        private GUIStyle _promptStyle;

        private bool ShouldDrawCancelPrompt =>
            _showCancelPrompt && _stage < Stage.WaitingToPauseGameplay && _stage != Stage.Done;

        private bool ShouldDrawSaveLoading =>
            _showSaveLoadingMessage && _stage == Stage.WaitingToPauseGameplay;

        private bool ShouldDrawCancelConfirmation =>
            Time.realtimeSinceStartup < _cancelConfirmUntilTime && _stage == Stage.Done
            && !_showNoSaveMessage && (_skipAutoPause || !_resumeClicked);

        private bool ShouldDrawNoSaveMessage =>
            _showNoSaveMessage && Time.realtimeSinceStartup < _cancelConfirmUntilTime;

        public override void OnInitializeMelon()
        {
            _stageEnteredTime = Time.realtimeSinceStartup;
            LoggerInstance.Msg("AutoResumeSurvival initialized. Hold LeftShift or Esc during menus to cancel.");
        }

        public override void OnUpdate()
        {
            if (_stage == Stage.Done)
                return;

            MaybeLogBackgroundLoadingTip();

            // Cancel is allowed until PauseGame() is sent. Before RESUME that aborts
            // the whole sequence; after RESUME it only skips the auto-pause.
            if (TryReadCancelInput())
            {
                HandleUserCancel();
                return;
            }

            float elapsedInStage = Time.realtimeSinceStartup - _stageEnteredTime;
            float now = Time.realtimeSinceStartup;

            switch (_stage)
            {
                case Stage.WaitingForMainMenu:
                    TickWaitingForMainMenu(now);
                    break;
                case Stage.EnteringSurvival:
                    TickEnteringSurvival(now);
                    break;
                case Stage.WaitingForSurvivalScreen:
                    TickWaitingForSurvivalScreen(now, elapsedInStage);
                    break;
                case Stage.ClickingResume:
                    TickClickingResume(now, elapsedInStage);
                    break;
                case Stage.WaitingToPauseGameplay:
                    TickWaitingToPauseGameplay(now, elapsedInStage);
                    break;
            }
        }

        public override void OnGUI()
        {
            string message = null;

            if (ShouldDrawCancelPrompt)
            {
                message = "Auto-resuming Survival... press Left Shift or Esc to cancel!";
            }
            else if (ShouldDrawCancelConfirmation)
            {
                message = _skipAutoPause
                    ? "Auto-pause skipped.\nHave a nice day!"
                    : "Auto-resume canceled.\nHave a nice day!";
            }
            else if (ShouldDrawNoSaveMessage)
            {
                message = "No Survival save found.\nAuto-resume canceled.";
            }
            else if (ShouldDrawSaveLoading)
            {
                message = "Save loading...\nPress Left Shift or Esc to skip auto-pause.";
            }

            if (string.IsNullOrEmpty(message))
                return;

            try
            {
                if (GUI.skin == null)
                    return;

                float boxWidth = 800f;
                float boxHeight = 200f;
                float x = (Screen.width - boxWidth) * 0.5f;
                float y = (Screen.height - boxHeight) * 0.5f;
                Rect rect = new Rect(x, y, boxWidth, boxHeight);

                Color prevColor = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.Box(rect, GUIContent.none);
                GUI.color = prevColor;

                _promptStyle ??= new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 48,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.95f, 0.94f, 0.91f, 1f) }
                };

                GUI.Label(rect, message, _promptStyle);
            }
            catch
            {
                // IMGUI can be unavailable on the very first frames.
            }
        }

        private void SetStage(Stage nextStage, string note)
        {
            _stage = nextStage;
            _stageEnteredTime = Time.realtimeSinceStartup;
            _lastStatusLogTime = 0f;
            _readySinceTime = 0f;
            _consecutiveReadyFrames = 0;
            LoggerInstance.Msg($"Stage -> {nextStage}: {note}");
        }

        private void StopSequence(string reason, bool warning = false)
        {
            _showCancelPrompt = false;
            _showSaveLoadingMessage = false;
            _stage = Stage.Done;
            if (warning)
                LoggerInstance.Warning(reason);
            else
                LoggerInstance.Msg(reason);
        }

        private static bool TryReadCancelInput()
        {
            try
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.Escape);
            }
            catch
            {
                // Input may not be initialized on the very first frames.
                return false;
            }
        }

        private void HandleUserCancel()
        {
            _showCancelPrompt = false;
            _showSaveLoadingMessage = false;
            _cancelConfirmUntilTime = Time.realtimeSinceStartup + 4f;

            if (_resumeClicked)
            {
                _skipAutoPause = true;
                _stage = Stage.Done;
                LoggerInstance.Msg(MelonLoader.Logging.ColorARGB.Yellow,
                    "Auto-pause skipped by user (LeftShift / Esc). The save will finish loading unpaused.");
            }
            else
            {
                _stage = Stage.Done;
                LoggerInstance.Msg(MelonLoader.Logging.ColorARGB.Yellow,
                    "Auto-resume canceled by user (LeftShift / Esc).");
            }
        }

        private void TickWaitingForMainMenu(float now)
        {
            if (!TryGetEnabledPanel(out Panel_MainMenu menu) || menu == null)
            {
                LogStatus(now, "WaitingForMainMenu: Panel_MainMenu not ready yet.");
                return;
            }

            if (menu.ProfileIsLoading())
            {
                _profileReadySinceTime = 0f;
                LogStatus(now, "WaitingForMainMenu: ProfileIsLoading() == true, waiting...");
                return;
            }

            if (_profileReadySinceTime == 0f)
                _profileReadySinceTime = now;

            // Note: the Survival save list is only reliably populated once the Survival
            // screen opens, so the "no save" check happens in ClickingResume instead.

            // Prefer HasFadedIn so we don't invoke OnSandbox mid-fade; fall back 10s after the profile is ready.
            if (!menu.HasFadedIn() && now - _profileReadySinceTime < 10.0f)
            {
                LogStatus(now, "WaitingForMainMenu: Main menu has not finished fading in...");
                return;
            }

            SetStage(Stage.EnteringSurvival, "Panel_MainMenu ready, profile loaded.");
        }

        private void TickEnteringSurvival(float now)
        {
            if (!TryGetEnabledPanel(out Panel_MainMenu menu) || menu == null)
            {
                LogStatus(now, "EnteringSurvival: Panel_MainMenu not available; retrying...");
                return;
            }

            try
            {
                menu.OnSandbox();
                SetStage(Stage.WaitingForSurvivalScreen, "Invoked Panel_MainMenu.OnSandbox(). Waiting for Survival menu...");
            }
            catch (Exception ex)
            {
                if (now - _lastStatusLogTime >= 2.0f)
                {
                    _lastStatusLogTime = now;
                    LoggerInstance.Warning($"EnteringSurvival: Panel_MainMenu.OnSandbox() failed ({ex.Message}); retrying...");
                }
            }
        }

        private void TickWaitingForSurvivalScreen(float now, float elapsedInStage)
        {
            if (IsPanelReady<Panel_Sandbox>())
            {
                SetStage(Stage.ClickingResume, "Survival menu is enabled.");
                return;
            }

            if (elapsedInStage > 8.0f)
            {
                StopSequence("Timed out waiting for the Survival menu to become enabled; stopping.", warning: true);
                return;
            }

            LogStatus(now, "WaitingForSurvivalScreen: Panel_Sandbox not enabled yet...");
        }

        private void TickClickingResume(float now, float elapsedInStage)
        {
            if (!TryGetEnabledPanel(out Panel_Sandbox sandbox) || sandbox == null)
            {
                if (elapsedInStage > 8.0f)
                {
                    StopSequence("Timed out looking for Panel_Sandbox to call OnClickResume(); stopping.", warning: true);
                    return;
                }

                LogStatus(now, "ClickingResume: Panel_Sandbox not available yet...");
                return;
            }

            // Survival screen is open, so its save list should be current now.
            bool hasSandboxSave = true;
            try
            {
                if (!_sandboxSlotsRefreshed)
                {
                    _sandboxSlotsRefreshed = true;
                    SaveGameSlotHelper.RefreshSaveSlots(SaveSlotType.SANDBOX, false);
                }
                hasSandboxSave = SaveGameSlotHelper.HasSaveSlots(SaveSlotType.SANDBOX)
                    || SaveGameSlotHelper.GetNumSaveSlots(SaveSlotType.SANDBOX) > 0;
            }
            catch (Exception ex)
            {
                // If the check itself fails, don't block: let OnClickResume() decide.
                LogStatus(now, $"ClickingResume: save slot check threw ({ex.Message}); trying Resume anyway.");
            }

            if (!hasSandboxSave)
            {
                if (elapsedInStage < 2.0f)
                {
                    LogStatus(now, "ClickingResume: No Survival save reported yet; giving slots a moment to populate...");
                    return;
                }

                _showCancelPrompt = false;
                _showNoSaveMessage = true;
                _cancelConfirmUntilTime = now + 4f;
                StopSequence("No Survival save found; auto-resume stopped.", warning: true);
                return;
            }

            try
            {
                sandbox.OnClickResume();
                _resumeClicked = true;
                _showCancelPrompt = false;
                _showSaveLoadingMessage = true;
                SetStage(Stage.WaitingToPauseGameplay, "Invoked Panel_Sandbox.OnClickResume(); waiting for the world to load...");
            }
            catch (Exception ex)
            {
                if (elapsedInStage > 8.0f)
                {
                    StopSequence($"OnClickResume() failed ({ex.Message}); stopping.", warning: true);
                    return;
                }

                if (now - _lastStatusLogTime >= 2.0f)
                {
                    _lastStatusLogTime = now;
                    LoggerInstance.Warning($"ClickingResume: OnClickResume() failed ({ex.Message}); retrying...");
                }
            }
        }

        private void TickWaitingToPauseGameplay(float now, float elapsedInStage)
        {
            if (!_sawGameplay)
            {
                if (IsInGameplay())
                {
                    _sawGameplay = true;
                    _gameplaySinceTime = now;
                    LoggerInstance.Msg("Left the main menu; waiting for gameplay to become ready to pause...");
                }
                else if (elapsedInStage > 30.0f)
                {
                    StopSequence("Timed out waiting to leave the main menu after RESUME; stopping.", warning: true);
                    return;
                }
                else
                {
                    LogStatus(now, "WaitingToPauseGameplay: Waiting to leave the main menu...");
                    return;
                }
            }

            if (now - _gameplaySinceTime > 45.0f)
            {
                StopSequence("Timed out waiting for gameplay to become ready to pause; stopping.", warning: true);
                return;
            }

            // If something else already paused the game, treat that as success.
            try
            {
                if (GameManager.m_IsPaused)
                {
                    _showSaveLoadingMessage = false;
                    SetStage(Stage.Done, "Gameplay is already paused; nothing more to do.");
                    return;
                }
            }
            catch
            {
                // GameManager may not be ready on the first gameplay frames.
            }

            if (IsGameplayReadyToPause())
            {
                if (_consecutiveReadyFrames == 0)
                    _readySinceTime = now;

                _consecutiveReadyFrames++;

                // Brief settle so post-load fade-in finishes before opening the pause menu.
                if (now - _readySinceTime >= 1.5f && _consecutiveReadyFrames >= 4)
                {
                    try
                    {
                        _showSaveLoadingMessage = false;
                        InputManager.PauseGame();
                        SetStage(Stage.Done, "Gameplay ready; invoked InputManager.PauseGame().");
                    }
                    catch (Exception ex)
                    {
                        _consecutiveReadyFrames = 0;
                        _readySinceTime = 0f;
                        LoggerInstance.Warning($"PauseGame() failed ({ex.Message}); will retry if still ready...");
                    }
                }
            }
            else
            {
                _consecutiveReadyFrames = 0;
                _readySinceTime = 0f;
                LogStatus(now, $"WaitingToPauseGameplay: Not ready to pause ({DescribeUnreadiness()})...");
            }
        }

        // Left the main menu and either a load is in progress or the player exists.
        private static bool IsInGameplay()
        {
            try
            {
                if (!InterfaceManager.HasInstance())
                    return false;
                if (GameManager.IsMainMenuActive())
                    return false;
                if (InterfaceManager.IsPanelLoadingEnabledOrLoading())
                    return true;
                return GameManager.GetPlayerManagerComponent() != null
                    || GameManager.GetPlayerObject() != null;
            }
            catch
            {
                return false;
            }
        }

        // True when the load is finished and the game will actually accept a pause.
        private static bool IsGameplayReadyToPause()
        {
            try
            {
                if (GameManager.IsMainMenuActive())
                    return false;
                if (InterfaceManager.IsPanelLoadingEnabledOrLoading())
                    return false;
                if (GameManager.GetPlayerManagerComponent() == null)
                    return false;
                if (GameManager.GetPlayerObject() == null)
                    return false;
                if (!InterfaceManager.IsPanelLoaded<Panel_HUD>())
                    return false;
                if (!InputManager.CanInvokePauseMenu())
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string DescribeUnreadiness()
        {
            try
            {
                if (GameManager.IsMainMenuActive())
                    return "main menu still active";
                if (InterfaceManager.IsPanelLoadingEnabledOrLoading())
                    return "loading panel still active";
                if (GameManager.GetPlayerManagerComponent() == null)
                    return "player manager not ready";
                if (GameManager.GetPlayerObject() == null)
                    return "player object not ready";
                if (!InterfaceManager.IsPanelLoaded<Panel_HUD>())
                    return "HUD not loaded";
                if (!InputManager.CanInvokePauseMenu())
                    return "CanInvokePauseMenu() is false";
                return "ready";
            }
            catch (Exception ex)
            {
                return $"check failed: {ex.Message}";
            }
        }

        private static bool IsPanelReady<T>() where T : Panel_Base
        {
            try
            {
                return InterfaceManager.HasInstance() && InterfaceManager.IsPanelEnabled<T>();
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetEnabledPanel<T>(out T panel) where T : Panel_Base
        {
            panel = null;
            try
            {
                if (!IsPanelReady<T>())
                    return false;

                panel = InterfaceManager.GetPanel<T>();
                return panel != null && panel.IsEnabled();
            }
            catch
            {
                panel = null;
                return false;
            }
        }

        private void MaybeLogBackgroundLoadingTip()
        {
            if (_loggedBackgroundLoadingTip)
                return;

            try
            {
                if (Application.isFocused)
                    return;
            }
            catch
            {
                // Application focus may not be readable on the very first frames.
                return;
            }

            if (IsBackgroundLoadingInstalled())
                return;

            _loggedBackgroundLoadingTip = true;
            LoggerInstance.Msg("Tip: the game window is unfocused during loading; Unity may pause until you refocus. Install BackgroundLoading to keep loading in the background.");
        }

        private static bool IsBackgroundLoadingInstalled()
        {
            try
            {
                foreach (MelonBase melon in MelonBase.RegisteredMelons)
                {
                    if (melon != null && melon.Info != null && melon.Info.Name == "BackgroundLoading")
                        return true;
                }
            }
            catch
            {
                // The registered-mod list should already be available in OnUpdate.
            }

            return false;
        }

        private void LogStatus(float now, string message)
        {
            if (now - _lastStatusLogTime < 2.0f)
                return;
            _lastStatusLogTime = now;
            LoggerInstance.Msg(message);
        }
    }
}

