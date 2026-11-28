
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
        // Don't IMGUI-draw the cancel prompt until the main menu panel is actually up.
        private bool _mainMenuSeen;
        // Skip drawing on the same frame as OnClickResume() / PauseGame().
        private bool _skipOnGuiThisFrame;
        private float _lastBootAdvanceTime;
        private float _nextBootSearchTime;
        private Panel_Boot _cachedBootPanel;
        private GUIStyle _promptStyle;

        private bool ShouldDrawCancelPrompt =>
            _showCancelPrompt && _mainMenuSeen
            && _stage < Stage.WaitingToPauseGameplay && _stage != Stage.Done;

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
            if (_skipOnGuiThisFrame)
            {
                _skipOnGuiThisFrame = false;
                return;
            }

            // Nothing on screen: don't touch IMGUI (GUI.skin / Box / Label) at all.
            if (!ShouldDrawCancelPrompt && !ShouldDrawCancelConfirmation
                && !ShouldDrawNoSaveMessage && !ShouldDrawSaveLoading)
                return;

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

        // Boot intro/disclaimer screens wait for a key and never time out.
        // Secondary screens use OnNextDisclaimerScreen(); the main disclaimer must not,
        // because that method indexes m_DisclaimerScreenList and throws once the list is done.
        private bool TryAdvanceBootIntros(float now)
        {
            Panel_Boot boot = GetEnabledBootPanel(now);
            if (boot == null || IsActive(boot.m_ErrorFlow))
                return false;

            bool waiting = BootIntroIsWaitingForInput(boot);
            bool listFinished = !CanCallOnNextDisclaimerScreen(boot) && !IsActive(boot.m_LoadingFlow)
                && !boot.m_HoldLoadingUntilInputGiven && !IsActive(boot.m_MainDisclaimer);
            if (!waiting && !listFinished)
                return false;

            if (now - _lastBootAdvanceTime < 0.8f)
                return true;

            try
            {
                if (IsActive(boot.m_MainDisclaimer))
                {
                    boot.ShowBootLoading();
                    _lastBootAdvanceTime = now;
                    LoggerInstance.Msg("WaitingForMainMenu: dismissed the main boot disclaimer (Panel_Boot.ShowBootLoading).");
                    return true;
                }

                if (SecondaryDisclaimerIsActive(boot))
                {
                    if (CanCallOnNextDisclaimerScreen(boot))
                    {
                        boot.OnNextDisclaimerScreen();
                        _lastBootAdvanceTime = now;
                        LoggerInstance.Msg("WaitingForMainMenu: advanced a boot intro screen (Panel_Boot.OnNextDisclaimerScreen).");
                    }
                    else
                    {
                        // Index is already past the list, so OnNextDisclaimerScreen would throw.
                        boot.ShowMainDisclaimer();
                        _lastBootAdvanceTime = now;
                        LoggerInstance.Msg("WaitingForMainMenu: showed the main boot disclaimer (Panel_Boot.ShowMainDisclaimer).");
                    }
                    return true;
                }

                // Secondary list is finished but the main disclaimer isn't up yet
                // (OnNextDisclaimerScreen already used up the last index).
                if (!IsActive(boot.m_LoadingFlow) && !CanCallOnNextDisclaimerScreen(boot)
                    && !IsActive(boot.m_MainDisclaimer) && !boot.m_HoldLoadingUntilInputGiven)
                {
                    boot.ShowMainDisclaimer();
                    _lastBootAdvanceTime = now;
                    LoggerInstance.Msg("WaitingForMainMenu: showed the main boot disclaimer (Panel_Boot.ShowMainDisclaimer).");
                    return true;
                }

                if (boot.m_HoldLoadingUntilInputGiven)
                {
                    boot.m_HoldLoadingUntilInputGiven = false;
                    if (boot.m_SceneLoaded)
                        boot.ReadyToActivateScene = true;
                    _lastBootAdvanceTime = now;
                    LoggerInstance.Msg("WaitingForMainMenu: released the boot loading key-hold.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _lastBootAdvanceTime = now;
                LoggerInstance.Warning($"WaitingForMainMenu: boot intro advance failed ({ex.Message}).");
                return true;
            }

            return waiting;
        }

        private Panel_Boot GetEnabledBootPanel(float now)
        {
            Panel_Boot boot;
            if (TryGetEnabledPanel(out boot) && boot != null)
            {
                _cachedBootPanel = boot;
                return boot;
            }

            if (_cachedBootPanel != null)
            {
                try
                {
                    if (_cachedBootPanel && _cachedBootPanel.IsEnabled())
                        return _cachedBootPanel;
                }
                catch
                {
                }
                _cachedBootPanel = null;
            }

            // InterfaceManager may not list Panel_Boot (EmptyScene holds it). Search rarely.
            if (now < _nextBootSearchTime)
                return null;
            _nextBootSearchTime = now + 1.0f;

            try
            {
                if (GameManager.IsEmptySceneActive())
                {
                    _cachedBootPanel = UnityEngine.Object.FindObjectOfType<Panel_Boot>();
                    if (_cachedBootPanel != null && _cachedBootPanel.IsEnabled())
                        return _cachedBootPanel;
                    _cachedBootPanel = null;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool BootIntroIsWaitingForInput(Panel_Boot boot)
        {
            try
            {
                if (boot.m_HoldLoadingUntilInputGiven)
                    return true;
                if (IsActive(boot.m_MainDisclaimer))
                    return true;
                return SecondaryDisclaimerIsActive(boot);
            }
            catch
            {
            }

            return false;
        }

        private static bool SecondaryDisclaimerIsActive(Panel_Boot boot)
        {
            var list = boot.m_DisclaimerScreenList;
            if (list == null)
                return false;

            for (int i = 0; i < list.Length; i++)
            {
                if (IsActive(list[i]))
                    return true;
            }

            return false;
        }

        private static bool CanCallOnNextDisclaimerScreen(Panel_Boot boot)
        {
            var list = boot.m_DisclaimerScreenList;
            if (list == null)
                return false;
            return boot.m_DisclaimerScreenIndex < list.Length;
        }

        private static bool IsActive(GameObject go)
        {
            try
            {
                return go != null && go.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private void TickWaitingForMainMenu(float now)
        {
            if (!TryGetEnabledPanel(out Panel_MainMenu menu) || menu == null)
            {
                if (!TryAdvanceBootIntros(now))
                    LogStatus(now, "WaitingForMainMenu: Panel_MainMenu not ready yet.");
                return;
            }

            _mainMenuSeen = true;

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
            // Refresh on its own frame so it isn't stacked on top of the save load.
            if (!_sandboxSlotsRefreshed)
            {
                try
                {
                    _sandboxSlotsRefreshed = true;
                    SaveGameSlotHelper.RefreshSaveSlots(SaveSlotType.SANDBOX, false);
                }
                catch (Exception ex)
                {
                    LogStatus(now, $"ClickingResume: RefreshSaveSlots threw ({ex.Message}); trying Resume next frame anyway.");
                }
                return;
            }

            bool hasSandboxSave = true;
            try
            {
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
                // OnClickResume() starts the save load (GameManager.LoadSaveGameSlot) on this
                // frame, so don't also spend time in IMGUI here.
                _skipOnGuiThisFrame = true;
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
                        _skipOnGuiThisFrame = true;
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

