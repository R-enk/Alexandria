using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

/// <summary>
/// Meta XR Interaction SDK の RayInteractable と既存の BookController を接続します。
///
/// ・Ray が本に Hover していない間は、BookController の A/X・B/Y 入力を抑止します。
/// ・Ray の Select（通常は Trigger）から本を開く／ページを送ることもできます。
/// ・Meta XR SDK の型を直接参照しないため、SDK がない環境でもコンパイルできます。
///
/// 本または子オブジェクトには、Meta XR Interaction SDK の
/// Collider、ColliderSurface、RayInteractable、
/// InteractableUnityEventWrapper を設定してください。
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

    [Header("Book")]
    [Tooltip("操作対象の BookController です。未設定なら同じオブジェクトか親から取得します。")]
    [SerializeField]
    private BookController bookController;

    [Header("Meta XR Interaction SDK")]
    [Tooltip("このオブジェクトと子オブジェクトの InteractableUnityEventWrapper を自動検出します。")]
    [SerializeField]
    private bool autoBindMetaEventWrappers = true;

    [Tooltip("Ray の Select（通常は Trigger）で実行する操作です。")]
    [SerializeField]
    private RaySelectAction selectAction = RaySelectAction.NextOrOpen;

    [Tooltip("実行中に追加された Meta XR コンポーネントを再検索する間隔です。")]
    [SerializeField, Min(0.1f)]
    private float rescanInterval = 1.0f;

    [Header("Input Gate")]
    [Tooltip("Ray が本に当たっていない間、既存 BookController のコントローラーボタン入力を抑止します。")]
    [SerializeField]
    private bool suppressControllerButtonsOutsideRay = true;

    [SerializeField]
    private bool logStateChanges;

    private readonly List<InputDevice> inputDevices = new List<InputDevice>();
    private readonly List<MetaBinding> bindings = new List<MetaBinding>();
    private readonly HashSet<Component> boundWrappers = new HashSet<Component>();
    private readonly HashSet<Component> hoveredWrappers = new HashSet<Component>();

    private FieldInfo previousPrimaryButtonField;
    private FieldInfo previousSecondaryButtonField;
    private MethodInfo handleRightInputMethod;
    private MethodInfo handleLeftInputMethod;

    private Coroutine bindingCoroutine;
    private bool manualHoverActive;
    private bool reflectionReady;
    private bool reflectionErrorLogged;

    public bool IsRayFocused => manualHoverActive || hoveredWrappers.Count > 0;

    private void Awake()
    {
        ResolveBookController();
        CacheBookControllerMembers();
    }

    private void OnEnable()
    {
        ResolveBookController();
        CacheBookControllerMembers();

        if (autoBindMetaEventWrappers)
        {
            bindingCoroutine = StartCoroutine(BindMetaWrappersRoutine());
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
    }

    private void Update()
    {
        // BookController の Update より先に実行されます。
        // Ray 外では「前フレームも現在と同じボタン状態」にして、
        // BookController 側の押下エッジを発生させません。
        if (suppressControllerButtonsOutsideRay && !IsRayFocused)
        {
            SuppressBookControllerButtonEdges();
        }
    }

    // Meta XR の When Hover へ Inspector から直接登録することもできます。
    public void OnRayHoverEntered()
    {
        manualHoverActive = true;
        LogFocusChange();
    }

    // Meta XR の When Unhover へ Inspector から直接登録することもできます。
    public void OnRayHoverExited()
    {
        manualHoverActive = false;
        LogFocusChange();
    }

    // Meta XR の When Select へ Inspector から直接登録することもできます。
    public void OnRaySelected()
    {
        ExecuteSelectAction();
    }

    public void OnRaySelectNext()
    {
        InvokeBookInput(handleRightInputMethod);
    }

    public void OnRaySelectPrevious()
    {
        InvokeBookInput(handleLeftInputMethod);
    }

    private void ResolveBookController()
    {
        if (bookController != null)
        {
            return;
        }

        bookController = GetComponent<BookController>() ??
                         GetComponentInParent<BookController>();

        if (bookController == null && !reflectionErrorLogged)
        {
            reflectionErrorLogged = true;
            Debug.LogError(
                "BookRayInteractionBridge から BookController を取得できません。",
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
        const BindingFlags flags = BindingFlags.Instance |
                                   BindingFlags.Public |
                                   BindingFlags.NonPublic;

        previousPrimaryButtonField = type.GetField("wasPrimaryButtonPressed", flags);
        previousSecondaryButtonField = type.GetField("wasSecondaryButtonPressed", flags);
        handleRightInputMethod = type.GetMethod("HandleRightInput", flags);
        handleLeftInputMethod = type.GetMethod("HandleLeftInput", flags);

        reflectionReady = previousPrimaryButtonField != null &&
                          previousSecondaryButtonField != null &&
                          handleRightInputMethod != null &&
                          handleLeftInputMethod != null;

        if (!reflectionReady && !reflectionErrorLogged)
        {
            reflectionErrorLogged = true;
            Debug.LogError(
                "BookController の入力メンバーを取得できません。" +
                "BookController.cs のメソッド名またはフィールド名が変更されていないか確認してください。",
                this
            );
        }
    }

    private void SuppressBookControllerButtonEdges()
    {
        if (!reflectionReady || bookController == null)
        {
            return;
        }

        inputDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller,
            inputDevices
        );

        bool primaryPressed = false;
        bool secondaryPressed = false;

        foreach (InputDevice device in inputDevices)
        {
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary) && primary)
            {
                primaryPressed = true;
            }

            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool secondary) && secondary)
            {
                secondaryPressed = true;
            }
        }

        previousPrimaryButtonField.SetValue(bookController, primaryPressed);
        previousSecondaryButtonField.SetValue(bookController, secondaryPressed);
    }

    private IEnumerator BindMetaWrappersRoutine()
    {
        // Meta XR のバージョンによっては Start 後に UnityEvent が初期化されます。
        yield return null;

        while (enabled)
        {
            DiscoverAndBindMetaWrappers();
            yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, rescanInterval));
        }
    }

    private void DiscoverAndBindMetaWrappers()
    {
        Component[] components = GetComponentsInChildren<Component>(true);

        foreach (Component component in components)
        {
            if (component == null || boundWrappers.Contains(component))
            {
                continue;
            }

            if (!IsMetaEventWrapper(component) || !ReferencesRayInteractable(component))
            {
                continue;
            }

            TryBindMetaWrapper(component);
        }
    }

    private bool TryBindMetaWrapper(Component wrapper)
    {
        UnityEvent hover = GetUnityEvent(wrapper, "WhenHover", "_whenHover");
        UnityEvent unhover = GetUnityEvent(
            wrapper,
            "WhenUnhover",
            "WhenUnHover",
            "_whenUnhover",
            "_whenUnHover"
        );
        UnityEvent select = GetUnityEvent(wrapper, "WhenSelect", "_whenSelect");

        if (hover == null || unhover == null)
        {
            return false;
        }

        MetaBinding binding = new MetaBinding
        {
            Wrapper = wrapper,
            Hover = hover,
            Unhover = unhover,
            Select = select
        };

        binding.HoverAction = () => HandleMetaHoverEntered(wrapper);
        binding.UnhoverAction = () => HandleMetaHoverExited(wrapper);
        binding.SelectAction = ExecuteSelectAction;

        hover.AddListener(binding.HoverAction);
        unhover.AddListener(binding.UnhoverAction);

        if (select != null && selectAction != RaySelectAction.None)
        {
            select.AddListener(binding.SelectAction);
        }

        bindings.Add(binding);
        boundWrappers.Add(wrapper);

        if (logStateChanges)
        {
            Debug.Log("Meta XR RayInteractable へ接続: " + wrapper.gameObject.name, this);
        }

        return true;
    }

    private void UnbindMetaEvents()
    {
        foreach (MetaBinding binding in bindings)
        {
            if (binding.Hover != null)
            {
                binding.Hover.RemoveListener(binding.HoverAction);
            }

            if (binding.Unhover != null)
            {
                binding.Unhover.RemoveListener(binding.UnhoverAction);
            }

            if (binding.Select != null)
            {
                binding.Select.RemoveListener(binding.SelectAction);
            }
        }

        bindings.Clear();
        boundWrappers.Clear();
    }

    private void HandleMetaHoverEntered(Component wrapper)
    {
        if (wrapper != null)
        {
            hoveredWrappers.Add(wrapper);
        }

        LogFocusChange();
    }

    private void HandleMetaHoverExited(Component wrapper)
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
                InvokeBookInput(handleRightInputMethod);
                break;
            case RaySelectAction.PreviousOrClose:
                InvokeBookInput(handleLeftInputMethod);
                break;
        }
    }

    private void InvokeBookInput(MethodInfo method)
    {
        if (bookController == null)
        {
            ResolveBookController();
            CacheBookControllerMembers();
        }

        if (bookController == null || method == null)
        {
            return;
        }

        // Select は Ray が対象へ当たった結果として発火するため、
        // Hover -> Select の状態遷移で Unhover が先に来ても操作を許可します。
        try
        {
            method.Invoke(bookController, null);
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception, this);
        }
    }

    private void LogFocusChange()
    {
        if (!logStateChanges)
        {
            return;
        }

        Debug.Log(
            IsRayFocused ? "Ray が本に入りました。" : "Ray が本から外れました。",
            this
        );
    }

    private static bool IsMetaEventWrapper(Component component)
    {
        Type type = component.GetType();
        return type.Name == "InteractableUnityEventWrapper" ||
               type.FullName == "Oculus.Interaction.InteractableUnityEventWrapper";
    }

    private static bool ReferencesRayInteractable(Component wrapper)
    {
        object interactable = GetMemberValue(wrapper, "InteractableView", "_interactableView");

        if (interactable == null)
        {
            return false;
        }

        Type type = interactable.GetType();
        return type.Name == "RayInteractable" ||
               (type.FullName != null &&
                type.FullName.EndsWith(".RayInteractable", StringComparison.Ordinal));
    }

    private static UnityEvent GetUnityEvent(Component component, params string[] names)
    {
        return GetMemberValue(component, names) as UnityEvent;
    }

    private static object GetMemberValue(object target, params string[] names)
    {
        if (target == null)
        {
            return null;
        }

        Type type = target.GetType();
        const BindingFlags flags = BindingFlags.Instance |
                                   BindingFlags.Public |
                                   BindingFlags.NonPublic;

        foreach (string name in names)
        {
            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
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

            FieldInfo field = type.GetField(name, flags);
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
