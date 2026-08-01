using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

#if ENABLE_INPUT_SYSTEM
using InputSystemApi = UnityEngine.InputSystem.InputSystem;
using InputSystemButtonControl = UnityEngine.InputSystem.Controls.ButtonControl;
using InputSystemXRController = UnityEngine.InputSystem.XR.XRController;
#endif

/// <summary>
/// Meta XR Interaction SDK の RayInteractable と BookController を接続します。
///
/// ・Ray が本に Hover している間だけ、A/X・B/Y入力を受け付けます。
/// ・Unity Input System の XRController と Meta の OVRInput の両方を確認します。
/// ・Ray の Select（通常は Trigger）から本を開く／ページを送ることもできます。
/// ・Meta XR SDK の型はReflectionで取得するため、SDKがない環境でもコンパイルできます。
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class BookRayInteractionBridge : MonoBehaviour
{
    public enum RaySelectAction
    {
        None,
        NextOrOpen,
        PreviousOrClose
    }

    private sealed class MetaBinding
    {
        public Component Wrapper;
        public UnityEvent Hover;
        public UnityEvent Unhover;
        public UnityEvent Select;
        public UnityAction HoverAction;
        public UnityAction UnhoverAction;
        public UnityAction SelectAction;
    }

    /// <summary>
    /// OVRInputを直接参照せずにButton.One / Button.Twoを読み取ります。
    /// Meta XR SDKが存在しない場合は利用不可として扱います。
    /// </summary>
    private sealed class OvrInputReflectionReader
    {
        private bool initialized;
        private bool available;
        private MethodInfo getMethod;
        private object buttonOne;
        private object buttonTwo;
        private object touchController;

        public bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return available;
            }
        }

        public bool TryRead(
            out bool primaryPressed,
            out bool secondaryPressed
        )
        {
            primaryPressed = false;
            secondaryPressed = false;

            EnsureInitialized();

            if (!available)
            {
                return false;
            }

            try
            {
                primaryPressed =
                    (bool)getMethod.Invoke(
                        null,
                        new[]
                        {
                            buttonOne,
                            touchController
                        }
                    );

                secondaryPressed =
                    (bool)getMethod.Invoke(
                        null,
                        new[]
                        {
                            buttonTwo,
                            touchController
                        }
                    );

                return true;
            }
            catch
            {
                available = false;
                return false;
            }
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            Type ovrInputType = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                ovrInputType = assembly.GetType(
                    "OVRInput",
                    throwOnError: false
                );

                if (ovrInputType != null)
                {
                    break;
                }
            }

            if (ovrInputType == null)
            {
                return;
            }

            Type buttonType =
                ovrInputType.GetNestedType(
                    "Button",
                    BindingFlags.Public
                );

            Type controllerType =
                ovrInputType.GetNestedType(
                    "Controller",
                    BindingFlags.Public
                );

            if (buttonType == null || controllerType == null)
            {
                return;
            }

            foreach (
                MethodInfo method
                in ovrInputType.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Static
                )
            )
            {
                if (
                    method.Name != "Get" ||
                    method.ReturnType != typeof(bool)
                )
                {
                    continue;
                }

                ParameterInfo[] parameters =
                    method.GetParameters();

                if (
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == buttonType &&
                    parameters[1].ParameterType == controllerType
                )
                {
                    getMethod = method;
                    break;
                }
            }

            if (getMethod == null)
            {
                return;
            }

            try
            {
                buttonOne = Enum.Parse(
                    buttonType,
                    "One"
                );

                buttonTwo = Enum.Parse(
                    buttonType,
                    "Two"
                );

                touchController = Enum.Parse(
                    controllerType,
                    "Touch"
                );

                available = true;
            }
            catch
            {
                available = false;
            }
        }
    }

    [Header("Book")]

    [Tooltip(
        "操作対象のBookControllerです。別オブジェクトにある場合は必ず指定してください。"
    )]
    [SerializeField]
    private BookController bookController;

    [Header("Meta XR Interaction SDK")]

    [Tooltip(
        "このオブジェクトと子オブジェクトのInteractableUnityEventWrapperを自動検出します。"
    )]
    [SerializeField]
    private bool autoBindMetaEventWrappers = true;

    [Tooltip("RayのSelect（通常はTrigger）で実行する操作です。")]
    [SerializeField]
    private RaySelectAction selectAction =
        RaySelectAction.NextOrOpen;

    [Tooltip(
        "実行中に追加されたMeta XRコンポーネントを再検索する間隔です。"
    )]
    [SerializeField, Min(0.1f)]
    private float rescanInterval = 1.0f;

    [Header("Controller Buttons")]

    [Tooltip(
        "Unity Input SystemのXRControllerからA/X・B/Yを読み取ります。"
    )]
    [SerializeField]
    private bool enableInputSystemControllerButtons = true;

    [Tooltip(
        "Meta OVRInputのButton.One / Button.Twoもフォールバックとして読み取ります。"
    )]
    [SerializeField]
    private bool enableOvrInputFallback = true;

    [Header("Input Gate")]

    [Tooltip(
        "Rayが本に当たっていない間、BookControllerの従来XR入力を抑止します。"
    )]
    [SerializeField]
    private bool suppressControllerButtonsOutsideRay = true;

    [SerializeField]
    private bool logStateChanges;

    [SerializeField]
    private bool logButtonPresses;

    private static readonly string[] PrimaryButtonControlNames =
    {
        "primaryButton",
        "buttonSouth",
        "aButton"
    };

    private static readonly string[] SecondaryButtonControlNames =
    {
        "secondaryButton",
        "buttonNorth",
        "bButton"
    };

    private readonly List<InputDevice> legacyInputDevices =
        new List<InputDevice>();

    private readonly List<MetaBinding> bindings =
        new List<MetaBinding>();

    private readonly HashSet<Component> boundWrappers =
        new HashSet<Component>();

    private readonly HashSet<Component> hoveredWrappers =
        new HashSet<Component>();

    private readonly OvrInputReflectionReader ovrInputReader =
        new OvrInputReflectionReader();

    private FieldInfo previousPrimaryButtonField;
    private FieldInfo previousSecondaryButtonField;
    private MethodInfo handleRightInputMethod;
    private MethodInfo handleLeftInputMethod;

    private Coroutine bindingCoroutine;
    private bool manualHoverActive;
    private bool reflectionReady;
    private bool reflectionErrorLogged;
    private bool wasModernPrimaryButtonPressed;
    private bool wasModernSecondaryButtonPressed;

    public bool IsRayFocused =>
        manualHoverActive ||
        hoveredWrappers.Count > 0;

    private void Awake()
    {
        ResolveBookController();
        CacheBookControllerMembers();
    }

    private void OnEnable()
    {
        ResolveBookController();
        CacheBookControllerMembers();

        wasModernPrimaryButtonPressed = false;
        wasModernSecondaryButtonPressed = false;

        if (autoBindMetaEventWrappers)
        {
            bindingCoroutine =
                StartCoroutine(
                    BindMetaWrappersRoutine()
                );
        }
    }

    private void OnDisable()
    {
        if (bindingCoroutine != null)
        {
            StopCoroutine(bindingCoroutine);
            bindingCoroutine = null;
        }

        UnbindMetaEvents();

        manualHoverActive = false;
        hoveredWrappers.Clear();

        wasModernPrimaryButtonPressed = false;
        wasModernSecondaryButtonPressed = false;
    }

    private void Update()
    {
        /*
         * 本にRayが当たっていない場合は、既存BookControllerが読む
         * UnityEngine.XR入力のエッジも発生させません。
         */
        if (
            suppressControllerButtonsOutsideRay &&
            !IsRayFocused
        )
        {
            SuppressBookControllerButtonEdges();
        }

        ReadModernControllerButtonEdges(
            out bool primaryPressedThisFrame,
            out bool secondaryPressedThisFrame
        );

        if (!IsRayFocused)
        {
            return;
        }

        if (primaryPressedThisFrame)
        {
            if (logButtonPresses)
            {
                Debug.Log(
                    "Meta controller A/X button pressed while Ray is focused.",
                    this
                );
            }

            InvokeBookInput(
                handleRightInputMethod
            );
        }
        else if (secondaryPressedThisFrame)
        {
            if (logButtonPresses)
            {
                Debug.Log(
                    "Meta controller B/Y button pressed while Ray is focused.",
                    this
                );
            }

            InvokeBookInput(
                handleLeftInputMethod
            );
        }
    }

    public void OnRayHoverEntered()
    {
        manualHoverActive = true;
        LogFocusChange();
    }

    public void OnRayHoverExited()
    {
        manualHoverActive = false;
        LogFocusChange();
    }

    public void OnRaySelected()
    {
        ExecuteSelectAction();
    }

    public void OnRaySelectNext()
    {
        InvokeBookInput(
            handleRightInputMethod
        );
    }

    public void OnRaySelectPrevious()
    {
        InvokeBookInput(
            handleLeftInputMethod
        );
    }

    [ContextMenu("Log Controller Input Diagnostics")]
    public void LogControllerInputDiagnostics()
    {
#if ENABLE_INPUT_SYSTEM
        int xrControllerCount = 0;

        foreach (
            UnityEngine.InputSystem.InputDevice inputDevice
            in InputSystemApi.devices
        )
        {
            if (!(inputDevice is InputSystemXRController xrController))
            {
                continue;
            }

            xrControllerCount++;

            InputSystemButtonControl primaryControl =
                FindInputSystemButton(
                    xrController,
                    PrimaryButtonControlNames
                );

            InputSystemButtonControl secondaryControl =
                FindInputSystemButton(
                    xrController,
                    SecondaryButtonControlNames
                );

            Debug.Log(
                "Input System XR controller: " +
                xrController.displayName +
                " / layout=" +
                xrController.layout +
                " / primary=" +
                (primaryControl != null
                    ? primaryControl.name
                    : "not found") +
                " / secondary=" +
                (secondaryControl != null
                    ? secondaryControl.name
                    : "not found"),
                this
            );
        }

        if (xrControllerCount == 0)
        {
            Debug.LogWarning(
                "Input System上にXRControllerが見つかりません。",
                this
            );
        }
#else
        Debug.LogWarning(
            "ENABLE_INPUT_SYSTEMが無効です。",
            this
        );
#endif

        Debug.Log(
            "OVRInput reflection available: " +
            ovrInputReader.IsAvailable,
            this
        );
    }

    private void ResolveBookController()
    {
        if (bookController != null)
        {
            return;
        }

        bookController =
            GetComponent<BookController>() ??
            GetComponentInParent<BookController>();

        if (
            bookController == null &&
            !reflectionErrorLogged
        )
        {
            reflectionErrorLogged = true;

            Debug.LogError(
                "BookRayInteractionBridgeからBookControllerを取得できません。" +
                "別オブジェクトにある場合はInspectorで指定してください。",
                this
            );
        }
    }

    private void CacheBookControllerMembers()
    {
        if (bookController == null)
        {
            return;
        }

        Type type = bookController.GetType();

        const BindingFlags Flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        previousPrimaryButtonField =
            type.GetField(
                "wasPrimaryButtonPressed",
                Flags
            );

        previousSecondaryButtonField =
            type.GetField(
                "wasSecondaryButtonPressed",
                Flags
            );

        handleRightInputMethod =
            type.GetMethod(
                "HandleRightInput",
                Flags
            );

        handleLeftInputMethod =
            type.GetMethod(
                "HandleLeftInput",
                Flags
            );

        reflectionReady =
            previousPrimaryButtonField != null &&
            previousSecondaryButtonField != null &&
            handleRightInputMethod != null &&
            handleLeftInputMethod != null;

        if (
            !reflectionReady &&
            !reflectionErrorLogged
        )
        {
            reflectionErrorLogged = true;

            Debug.LogError(
                "BookControllerの入力メンバーを取得できません。" +
                "BookController.csのメソッド名またはフィールド名を確認してください。",
                this
            );
        }
    }

    private void SuppressBookControllerButtonEdges()
    {
        if (
            !reflectionReady ||
            bookController == null
        )
        {
            return;
        }

        legacyInputDevices.Clear();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller,
            legacyInputDevices
        );

        bool primaryPressed = false;
        bool secondaryPressed = false;

        foreach (InputDevice device in legacyInputDevices)
        {
            if (
                device.TryGetFeatureValue(
                    CommonUsages.primaryButton,
                    out bool primary
                ) &&
                primary
            )
            {
                primaryPressed = true;
            }

            if (
                device.TryGetFeatureValue(
                    CommonUsages.secondaryButton,
                    out bool secondary
                ) &&
                secondary
            )
            {
                secondaryPressed = true;
            }
        }

        previousPrimaryButtonField.SetValue(
            bookController,
            primaryPressed
        );

        previousSecondaryButtonField.SetValue(
            bookController,
            secondaryPressed
        );
    }

    private void ReadModernControllerButtonEdges(
        out bool primaryPressedThisFrame,
        out bool secondaryPressedThisFrame
    )
    {
        ReadModernControllerButtonStates(
            out bool primaryPressed,
            out bool secondaryPressed
        );

        primaryPressedThisFrame =
            primaryPressed &&
            !wasModernPrimaryButtonPressed;

        secondaryPressedThisFrame =
            secondaryPressed &&
            !wasModernSecondaryButtonPressed;

        wasModernPrimaryButtonPressed =
            primaryPressed;

        wasModernSecondaryButtonPressed =
            secondaryPressed;
    }

    private void ReadModernControllerButtonStates(
        out bool primaryPressed,
        out bool secondaryPressed
    )
    {
        primaryPressed = false;
        secondaryPressed = false;

#if ENABLE_INPUT_SYSTEM
        if (enableInputSystemControllerButtons)
        {
            foreach (
                UnityEngine.InputSystem.InputDevice inputDevice
                in InputSystemApi.devices
            )
            {
                if (!(inputDevice is InputSystemXRController xrController))
                {
                    continue;
                }

                InputSystemButtonControl primaryControl =
                    FindInputSystemButton(
                        xrController,
                        PrimaryButtonControlNames
                    );

                InputSystemButtonControl secondaryControl =
                    FindInputSystemButton(
                        xrController,
                        SecondaryButtonControlNames
                    );

                if (
                    primaryControl != null &&
                    primaryControl.isPressed
                )
                {
                    primaryPressed = true;
                }

                if (
                    secondaryControl != null &&
                    secondaryControl.isPressed
                )
                {
                    secondaryPressed = true;
                }
            }
        }
#endif

        if (
            enableOvrInputFallback &&
            ovrInputReader.TryRead(
                out bool ovrPrimaryPressed,
                out bool ovrSecondaryPressed
            )
        )
        {
            primaryPressed |=
                ovrPrimaryPressed;

            secondaryPressed |=
                ovrSecondaryPressed;
        }
    }

#if ENABLE_INPUT_SYSTEM
    private static InputSystemButtonControl FindInputSystemButton(
        InputSystemXRController controller,
        IReadOnlyList<string> controlNames
    )
    {
        foreach (string controlName in controlNames)
        {
            InputSystemButtonControl control =
                controller.TryGetChildControl<InputSystemButtonControl>(
                    controlName
                );

            if (control != null)
            {
                return control;
            }
        }

        return null;
    }
#endif

    private IEnumerator BindMetaWrappersRoutine()
    {
        yield return null;

        while (enabled)
        {
            DiscoverAndBindMetaWrappers();

            yield return
                new WaitForSecondsRealtime(
                    Mathf.Max(
                        0.1f,
                        rescanInterval
                    )
                );
        }
    }

    private void DiscoverAndBindMetaWrappers()
    {
        Component[] components =
            GetComponentsInChildren<Component>(
                includeInactive: true
            );

        foreach (Component component in components)
        {
            if (
                component == null ||
                boundWrappers.Contains(component)
            )
            {
                continue;
            }

            if (
                !IsMetaEventWrapper(component) ||
                !ReferencesRayInteractable(component)
            )
            {
                continue;
            }

            TryBindMetaWrapper(component);
        }
    }

    private bool TryBindMetaWrapper(
        Component wrapper
    )
    {
        UnityEvent hover =
            GetUnityEvent(
                wrapper,
                "WhenHover",
                "_whenHover"
            );

        UnityEvent unhover =
            GetUnityEvent(
                wrapper,
                "WhenUnhover",
                "WhenUnHover",
                "_whenUnhover",
                "_whenUnHover"
            );

        UnityEvent select =
            GetUnityEvent(
                wrapper,
                "WhenSelect",
                "_whenSelect"
            );

        if (hover == null || unhover == null)
        {
            return false;
        }

        MetaBinding binding =
            new MetaBinding
            {
                Wrapper = wrapper,
                Hover = hover,
                Unhover = unhover,
                Select = select
            };

        binding.HoverAction =
            () => HandleMetaHoverEntered(wrapper);

        binding.UnhoverAction =
            () => HandleMetaHoverExited(wrapper);

        binding.SelectAction =
            ExecuteSelectAction;

        hover.AddListener(
            binding.HoverAction
        );

        unhover.AddListener(
            binding.UnhoverAction
        );

        if (
            select != null &&
            selectAction != RaySelectAction.None
        )
        {
            select.AddListener(
                binding.SelectAction
            );
        }

        bindings.Add(binding);
        boundWrappers.Add(wrapper);

        if (logStateChanges)
        {
            Debug.Log(
                "Meta XR RayInteractableへ接続: " +
                wrapper.gameObject.name,
                this
            );
        }

        return true;
    }

    private void UnbindMetaEvents()
    {
        foreach (MetaBinding binding in bindings)
        {
            if (binding.Hover != null)
            {
                binding.Hover.RemoveListener(
                    binding.HoverAction
                );
            }

            if (binding.Unhover != null)
            {
                binding.Unhover.RemoveListener(
                    binding.UnhoverAction
                );
            }

            if (binding.Select != null)
            {
                binding.Select.RemoveListener(
                    binding.SelectAction
                );
            }
        }

        bindings.Clear();
        boundWrappers.Clear();
    }

    private void HandleMetaHoverEntered(
        Component wrapper
    )
    {
        if (wrapper != null)
        {
            hoveredWrappers.Add(wrapper);
        }

        LogFocusChange();
    }

    private void HandleMetaHoverExited(
        Component wrapper
    )
    {
        if (wrapper != null)
        {
            hoveredWrappers.Remove(wrapper);
        }

        LogFocusChange();
    }

    private void ExecuteSelectAction()
    {
        switch (selectAction)
        {
            case RaySelectAction.NextOrOpen:
                InvokeBookInput(
                    handleRightInputMethod
                );
                break;

            case RaySelectAction.PreviousOrClose:
                InvokeBookInput(
                    handleLeftInputMethod
                );
                break;
        }
    }

    private void InvokeBookInput(
        MethodInfo method
    )
    {
        if (bookController == null)
        {
            ResolveBookController();
            CacheBookControllerMembers();
        }

        if (
            bookController == null ||
            method == null
        )
        {
            return;
        }

        try
        {
            method.Invoke(
                bookController,
                null
            );
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(
                exception.InnerException ?? exception,
                this
            );
        }
    }

    private void LogFocusChange()
    {
        if (!logStateChanges)
        {
            return;
        }

        Debug.Log(
            IsRayFocused
                ? "Rayが本に入りました。"
                : "Rayが本から外れました。",
            this
        );
    }

    private static bool IsMetaEventWrapper(
        Component component
    )
    {
        Type type = component.GetType();

        return
            type.Name ==
            "InteractableUnityEventWrapper" ||
            type.FullName ==
            "Oculus.Interaction.InteractableUnityEventWrapper";
    }

    private static bool ReferencesRayInteractable(
        Component wrapper
    )
    {
        object interactable =
            GetMemberValue(
                wrapper,
                "InteractableView",
                "_interactableView"
            );

        if (interactable == null)
        {
            return false;
        }

        Type type = interactable.GetType();

        return
            type.Name == "RayInteractable" ||
            (
                type.FullName != null &&
                type.FullName.EndsWith(
                    ".RayInteractable",
                    StringComparison.Ordinal
                )
            );
    }

    private static UnityEvent GetUnityEvent(
        Component component,
        params string[] names
    )
    {
        return
            GetMemberValue(
                component,
                names
            ) as UnityEvent;
    }

    private static object GetMemberValue(
        object target,
        params string[] names
    )
    {
        if (target == null)
        {
            return null;
        }

        Type type = target.GetType();

        const BindingFlags Flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        foreach (string name in names)
        {
            PropertyInfo property =
                type.GetProperty(
                    name,
                    Flags
                );

            if (
                property != null &&
                property.GetIndexParameters().Length == 0
            )
            {
                try
                {
                    return property.GetValue(target);
                }
                catch
                {
                    // 次の候補を試します。
                }
            }

            FieldInfo field =
                type.GetField(
                    name,
                    Flags
                );

            if (field != null)
            {
                try
                {
                    return field.GetValue(target);
                }
                catch
                {
                    // 次の候補を試します。
                }
            }
        }

        return null;
    }
}
