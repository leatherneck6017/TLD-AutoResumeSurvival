using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

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
            WaitingForGameplayLoad,
            WaitingToPauseGameplay,
            Done
        }

        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const byte VK_ESCAPE = 0x1B;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private static AutoResumeMod? _instance;
        private static MethodInfo? _il2CppTypeOfGenericMethod;
        private static MethodInfo? _il2CppTryCastGenericMethod;
        private static Il2CppSystem.Type? _gameObjectIl2CppType;
        private static Il2CppSystem.Type? _componentIl2CppType;
        private Stage _stage = Stage.WaitingForMainMenu;
        private bool _bypassed;
        private bool _enteredGameplayScene;
        private float _stageEnteredTime;
        private int _stageFrames;
        private float _lastStatusLogTime;
        private bool _sceneHooked;
        private float _readySinceTime;
        private int _consecutiveReadyFrames;
        private bool _showCancelPrompt;
        private float _cancelConfirmUntilTime;
        private bool ShouldDrawCancelConfirmation => _bypassed && Time.realtimeSinceStartup < _cancelConfirmUntilTime;
        private bool ShouldDrawCancelPrompt => !_bypassed && _showCancelPrompt && _stage < Stage.WaitingForGameplayLoad;
        private float _lastReadinessCheckTime;

        private Type? _panelMainMenuType;
        private object? _panelMainMenuInstance;

        public override void OnInitializeMelon()
        {
            _instance = this;
            ResetSequence("OnInitializeMelon");
            LoggerInstance.Msg("AutoResumeSurvival initialized. Hold LeftShift or Esc during menus to bypass.");

            TryHookSceneLoaded();
            TryCreateRunnerObject();
        }

        private void ResetSequence(string reason)
        {
            if (_bypassed)
                return;

            _stage = Stage.WaitingForMainMenu;
            _enteredGameplayScene = false;
            _stageEnteredTime = Time.realtimeSinceStartup;
            _stageFrames = 0;
            _lastStatusLogTime = 0f;
            _readySinceTime = 0f;
            _consecutiveReadyFrames = 0;
            _showCancelPrompt = !_bypassed;
            _lastReadinessCheckTime = 0f;
        }

        private void SetStage(Stage nextStage, string note)
        {
            _stage = nextStage;
            _stageEnteredTime = Time.realtimeSinceStartup;
            _stageFrames = 0;
            _lastStatusLogTime = 0f;
            _readySinceTime = 0f;
            _consecutiveReadyFrames = 0;
            _lastReadinessCheckTime = 0f;
            LoggerInstance.Msg($"Stage -> {nextStage}: {note}");
        }

        private void TryHookSceneLoaded()
        {
            if (_sceneHooked)
                return;

            try
            {
                Action<Scene, LoadSceneMode> handler = OnUnitySceneLoaded;
                var unityAction = DelegateSupport.ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(handler);
                SceneManager.add_sceneLoaded(unityAction);
                _sceneHooked = true;
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"SceneManager.add_sceneLoaded hook failed: {ex.Message}");
            }
        }

        private static void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _instance?.HandleSceneLoaded(scene.name);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            HandleSceneLoaded(sceneName);
        }

        private void HandleSceneLoaded(string sceneName)
        {
            if (_bypassed || _enteredGameplayScene)
                return;

            string lower = (sceneName ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("region") || lower.Contains("zone") || lower.Contains("interior") ||
                lower.Contains("lake") || lower.Contains("coastal") || lower.Contains("pleasant"))
            {
                _enteredGameplayScene = true;
                SetStage(Stage.WaitingToPauseGameplay, $"Entered gameplay scene '{sceneName}'; waiting for fade-in before pausing...");
            }
        }

        public override void OnUpdate()
        {
            PumpTick("MelonMod.OnUpdate");
        }

        public void PumpTick(string source)
        {
            if (_bypassed || _stage == Stage.Done)
                return;

            // Allow manual bypass until we reach the point where we are waiting to pause gameplay,
            if (_stage < Stage.WaitingToPauseGameplay)
            {
                try
                {
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.Escape))
                    {
                        _bypassed = true;
                        _showCancelPrompt = false;
                        _cancelConfirmUntilTime = Time.realtimeSinceStartup + 4f;
                        _stage = Stage.Done;
                        LoggerInstance.Msg(MelonLoader.Logging.ColorARGB.Yellow, "Auto-resume bypassed by user (LeftShift / Esc).");
                        return;
                    }
                }
                catch
                {
                    // Input may not be initialized on the very first frames.
                }
            }

            _stageFrames++;
            float elapsedInStage = Time.realtimeSinceStartup - _stageEnteredTime;
            float now = Time.realtimeSinceStartup;

            switch (_stage)
            {
                case Stage.WaitingForMainMenu:
                    if (elapsedInStage < 1.0f || _stageFrames < 15)
                        return;

                    if (!TryResolveMainMenu(out Type? menuType, out object? menuInstance))
                    {
                        if (now - _lastStatusLogTime >= 2.0f)
                        {
                            _lastStatusLogTime = now;
                            LoggerInstance.Msg("WaitingForMainMenu: Panel_MainMenu or instance not ready yet.");
                        }
                        return;
                    }

                    if (IsProfileLoading(menuType!, menuInstance!))
                    {
                        if (now - _lastStatusLogTime >= 2.0f)
                        {
                            _lastStatusLogTime = now;
                            LoggerInstance.Msg("WaitingForMainMenu: ProfileIsLoading() == true, waiting...");
                        }
                        return;
                    }

                    _panelMainMenuType = menuType;
                    _panelMainMenuInstance = menuInstance;
                    SetStage(Stage.EnteringSurvival, "Panel_MainMenu ready and profile loaded.");
                    break;

                case Stage.EnteringSurvival:
                    if (TryInvokeParameterless(_panelMainMenuType!, _panelMainMenuInstance!, "OnSandbox"))
                    {
                        SetStage(Stage.WaitingForSurvivalScreen, "Invoked Panel_MainMenu.OnSandbox(). Waiting for Survival menu...");
                    }
                    else if (now - _lastStatusLogTime >= 2.0f)
                    {
                        _lastStatusLogTime = now;
                        LoggerInstance.Warning("EnteringSurvival: Retrying Panel_MainMenu.OnSandbox()...");
                    }
                    break;

                case Stage.WaitingForSurvivalScreen:
                    if (elapsedInStage < 1.0f || _stageFrames < 20)
                        return;

                    SetStage(Stage.ClickingResume, "Survival menu settle window complete.");
                    break;

                case Stage.ClickingResume:
                    if (TryClickResumeOnSurvivalMenu())
                    {
                        _showCancelPrompt = false;
                        SetStage(Stage.WaitingForGameplayLoad, "Activated RESUME on Survival menu; waiting for world load...");
                    }
                    else if (elapsedInStage > 8.0f)
                    {
                        _showCancelPrompt = false;
                        LoggerInstance.Warning("Timed out looking for RESUME on Survival menu; stopping.");
                        _stage = Stage.Done;
                    }
                    else if (now - _lastStatusLogTime >= 2.0f)
                    {
                        _lastStatusLogTime = now;
                        LoggerInstance.Msg("ClickingResume: Waiting for RESUME option on Survival menu...");
                    }
                    break;

                case Stage.WaitingForGameplayLoad:
                    if (elapsedInStage > 30.0f)
                    {
                        _showCancelPrompt = false;
                        LoggerInstance.Warning("Timed out waiting for gameplay scene load; stopping.");
                        _stage = Stage.Done;
                    }
                    break;

                case Stage.WaitingToPauseGameplay:
                    if (elapsedInStage > 45.0f)
                    {
                        _showCancelPrompt = false;
                        LoggerInstance.Warning("Timed out waiting for gameplay to become ready to pause; stopping.");
                        _stage = Stage.Done;
                        return;
                    }

                    // Only poll scene objects every 0.25s instead of every frame during heavy world/subscene loading.
                    if (now - _lastReadinessCheckTime < 0.25f)
                        return;

                    _lastReadinessCheckTime = now;

                    if (IsGameplayReadyToPause())
                    {
                        if (_consecutiveReadyFrames == 0)
                            _readySinceTime = now;

                        _consecutiveReadyFrames++;

                        // Wait 1.5s of continuous readiness (and at least 4 checks at 0.25s intervals)
                        // so post-load settle frames finish before pressing Escape.
                        if (now - _readySinceTime >= 1.5f && _consecutiveReadyFrames >= 4)
                        {
                            SendEscapeKeyPress();
                            SetStage(Stage.Done, "Gameplay active; sent Escape key to pause.");
                        }
                    }
                    else
                    {
                        _consecutiveReadyFrames = 0;
                        _readySinceTime = 0f;

                        if (now - _lastStatusLogTime >= 2.0f)
                        {
                            _lastStatusLogTime = now;
                            LoggerInstance.Msg("WaitingToPauseGameplay: Save still loading or fading in...");
                        }
                    }
                    break;

                    if (IsGameplayReadyToPause())
                    {
                        if (_consecutiveReadyFrames == 0)
                            _readySinceTime = now;

                        _consecutiveReadyFrames++;

                        if (now - _readySinceTime >= 0.5f && _consecutiveReadyFrames >= 15)
                        {
                            SendEscapeKeyPress();
                            SetStage(Stage.Done, "Gameplay active; sent Escape key to pause.");
                        }
                    }
                    else
                    {
                        _consecutiveReadyFrames = 0;
                        _readySinceTime = 0f;

                        if (now - _lastStatusLogTime >= 2.0f)
                        {
                            _lastStatusLogTime = now;
                            LoggerInstance.Msg("WaitingToPauseGameplay: Save still loading or fading in...");
                        }
                    }
                    break;
            }
        }

        private bool IsGameplayReadyToPause()
        {
            try
            {
                _gameObjectIl2CppType ??= ResolveIl2CppType(typeof(GameObject));
                if (_gameObjectIl2CppType == null)
                    return false;

                Il2CppArrayBase<UnityEngine.Object>? objs = UnityEngine.Object.FindObjectsOfType(_gameObjectIl2CppType);
                if (objs != null)
                {
                    for (int i = 0; i < objs.Length; i++)
                    {
                        GameObject? go = objs[i]?.TryCast<GameObject>();
                        if (go == null || !go.activeInHierarchy)
                            continue;

                        if (go.name.Contains("Loading", StringComparison.OrdinalIgnoreCase) &&
                            go.name.Contains("Panel", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }

                if (Time.timeScale <= 0.001f)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SendEscapeKeyPress()
        {
            try
            {
                keybd_event(VK_ESCAPE, 0x01, 0, UIntPtr.Zero);
                keybd_event(VK_ESCAPE, 0x01, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Failed to send Escape key press: {ex.Message}");
            }
        }

        private bool TryResolveMainMenu(out Type? menuType, out object? menuInstance)
        {
            menuType = _panelMainMenuType;
            if (menuType == null)
            {
                menuType = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a =>
                    {
                        string? n = a.GetName().Name;
                        return n != null && (n.Contains("Assembly-CSharp") || n.Contains("Il2Cpp"));
                    })
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
                    })
                    .FirstOrDefault(t => t != null && t.Name == "Panel_MainMenu");
            }

            if (menuType == null)
            {
                menuInstance = null;
                return false;
            }

            menuInstance = FindInstance(menuType);
            return menuInstance != null;
        }

        private static bool IsProfileLoading(Type menuType, object menuInstance)
        {
            MethodInfo? isLoadingMethod = menuType.GetMethod("ProfileIsLoading", AnyInstance);
            if (isLoadingMethod != null && isLoadingMethod.ReturnType == typeof(bool))
            {
                object? adapted = AdaptTargetForMethod(menuInstance, isLoadingMethod);
                if (adapted == null && !isLoadingMethod.IsStatic)
                    return false;

                try
                {
                    object? res = isLoadingMethod.Invoke(adapted, null);
                    if (res is bool b)
                        return b;
                }
                catch { }
            }
            return false;
        }

        private bool TryInvokeParameterless(Type menuType, object menuInstance, string methodName)
        {
            MethodInfo? method = menuType.GetMethod(methodName, AnyInstance, null, Type.EmptyTypes, null)
                ?? menuType.GetMethods(AnyInstance).FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);

            if (method == null)
            {
                LoggerInstance.Warning($"Method '{menuType.Name}.{methodName}()' not found.");
                return false;
            }

            object? adapted = AdaptTargetForMethod(menuInstance, method);
            if (adapted == null && !method.IsStatic)
            {
                LoggerInstance.Warning($"Could not adapt target for {menuType.Name}.{methodName}().");
                return false;
            }

            try
            {
                method.Invoke(adapted, null);
                return true;
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Failed invoking {menuType.Name}.{methodName}(): {ex.Message}");
                return false;
            }
        }

        private bool TryClickResumeOnSurvivalMenu()
        {
            try
            {
                _gameObjectIl2CppType ??= ResolveIl2CppType(typeof(GameObject));
                if (_gameObjectIl2CppType == null)
                    return false;

                Il2CppArrayBase<UnityEngine.Object>? objs = UnityEngine.Object.FindObjectsOfType(_gameObjectIl2CppType);
                if (objs == null || objs.Length == 0)
                    return false;

                for (int i = 0; i < objs.Length; i++)
                {
                    GameObject? go = objs[i]?.TryCast<GameObject>();
                    if (go == null || !go.activeInHierarchy)
                        continue;

                    if (go.name == "MenuItem1" && go.transform.parent != null)
                    {
                        string parentPath = GetParentPath(go.transform);
                        if (parentPath.Contains("Panel_Sandbox", StringComparison.OrdinalIgnoreCase) &&
                            TryActivateUiObject(go))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error in TryClickResumeOnSurvivalMenu: {ex.Message}");
            }

            return false;
        }

        private static string GetParentPath(Transform t)
        {
            Transform? curr = t.parent;
            string path = string.Empty;
            int guard = 0;
            while (curr != null && guard++ < 25)
            {
                path = string.IsNullOrEmpty(path) ? curr.name : $"{curr.name}/{path}";
                curr = curr.parent;
            }
            return path;
        }

        private bool TryActivateUiObject(GameObject targetGo)
        {
            _componentIl2CppType ??= ResolveIl2CppType(typeof(Component));
            if (_componentIl2CppType != null)
            {
                Il2CppReferenceArray<Component>? comps = null;
                try { comps = targetGo.GetComponents(_componentIl2CppType); }
                catch { }

                if (comps != null)
                {
                    for (int i = 0; i < comps.Length; i++)
                    {
                        Component? comp = comps[i];
                        if (comp == null) continue;

                        PropertyInfo? onClickProp = comp.GetType().GetProperty("onClick", AnyInstance);
                        if (onClickProp != null)
                        {
                            object? clickEvent = onClickProp.GetValue(comp);
                            MethodInfo? invokeMethod = clickEvent?.GetType().GetMethod("Invoke", AnyInstance);
                            if (invokeMethod != null && invokeMethod.GetParameters().Length == 0)
                            {
                                invokeMethod.Invoke(clickEvent, null);
                                return true;
                            }
                        }

                        foreach (string mName in new[] { "OnClick", "OnPress", "Click", "Submit", "OnSubmit" })
                        {
                            MethodInfo? m = comp.GetType().GetMethods(AnyInstance)
                                .FirstOrDefault(x => x.Name == mName && x.GetParameters().Length == 0);
                            if (m != null)
                            {
                                m.Invoke(comp, null);
                                return true;
                            }
                        }
                    }
                }
            }

            try
            {
                targetGo.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
                return true;
            }
            catch { }

            return false;
        }

        private static object? AdaptTargetForMethod(object? target, MethodInfo method)
        {
            if (method.IsStatic || target == null)
                return null;

            Type? targetType = method.DeclaringType;
            if (targetType == null || targetType.IsInstanceOfType(target))
                return target;

            if (target is Il2CppObjectBase il2CppObj && typeof(Il2CppObjectBase).IsAssignableFrom(targetType))
            {
                try
                {
                    _il2CppTryCastGenericMethod ??= typeof(Il2CppObjectBase)
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "TryCast" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

                    if (_il2CppTryCastGenericMethod != null)
                    {
                        MethodInfo closedCast = _il2CppTryCastGenericMethod.MakeGenericMethod(targetType);
                        object? result = closedCast.Invoke(il2CppObj, null);
                        if (result != null)
                            return result;
                    }
                }
                catch { }
            }

            return null;
        }

        private static object? FindInstance(Type declaringType)
        {
            foreach (string name in new[] { "Instance", "s_Instance", "Current", "Active" })
            {
                try
                {
                    PropertyInfo? prop = declaringType.GetProperty(name, AnyStatic);
                    if (prop != null)
                    {
                        object? v = prop.GetValue(null);
                        if (v != null) return v;
                    }

                    FieldInfo? field = declaringType.GetField(name, AnyStatic);
                    if (field != null)
                    {
                        object? v = field.GetValue(null);
                        if (v != null) return v;
                    }
                }
                catch { }
            }

            try
            {
                Il2CppSystem.Type? il2CppType = ResolveIl2CppType(declaringType);
                if (il2CppType != null)
                {
                    Il2CppArrayBase<UnityEngine.Object>? found = UnityEngine.Object.FindObjectsOfType(il2CppType);
                    if (found != null && found.Count > 0)
                        return found[0];
                }
            }
            catch { }

            return null;
        }

        private static Il2CppSystem.Type? ResolveIl2CppType(Type systemType)
        {
            try
            {
                _il2CppTypeOfGenericMethod ??= typeof(Il2CppType)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Of" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

                if (_il2CppTypeOfGenericMethod != null)
                {
                    MethodInfo closedOf = _il2CppTypeOfGenericMethod.MakeGenericMethod(systemType);
                    object? result = closedOf.Invoke(null, null);
                    if (result is Il2CppSystem.Type il2CppType)
                        return il2CppType;
                }
            }
            catch { }

            return null;
        }

        private void TryCreateRunnerObject()
        {
            try
            {
                Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<AutoResumeRunner>();
                GameObject go = new GameObject("AutoResumeSurvival_Runner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<AutoResumeRunner>();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Failed to create runner: {ex.Message}");
            }
        }

        public class AutoResumeRunner : MonoBehaviour
        {
            private static GUIStyle? _promptStyle;

            public AutoResumeRunner(IntPtr ptr) : base(ptr) { }

            private void Update() => _instance?.PumpTick("AutoResumeRunner.Update");

            private void OnGUI()
            {
                if (_instance == null)
                    return;

                string? message = null;

                if (_instance.ShouldDrawCancelPrompt)
                {
                    message = "Auto-resuming Survival... press Left Shift or Esc to cancel!";
                }
                else if (_instance.ShouldDrawCancelConfirmation)
                {
                    message = "Auto-resume canceled.\nHave a nice day!";
                }

                if (string.IsNullOrEmpty(message))
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
        }
    }
}