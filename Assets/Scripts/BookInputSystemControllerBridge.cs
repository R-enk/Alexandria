using System;
using System.Reflection;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Unity Input SystemからMeta Questコントローラーの
/// A/X（primaryButton）とB/Y（secondaryButton）を読み取り、
/// 既存のBookControllerへ渡します。
///
/// BookRayInteractionBridgeと併用することで、Rayが本に
/// 当たっている間だけボタン操作を許可できます。
/// </summary>
[DefaultExecutionOrder(-1100)]
[DisallowMultipleComponent]
public sealed class BookInputSystemControllerBridge : MonoBehaviour
{
    [Header("Book")]

    [Tooltip(
        "操作対象のBookControllerです。" +
        "別GameObjectに付いている場合はInspectorで指定してください。"
    )]
    [SerializeField]
    private BookController bookController;

    [Tooltip(
        "RayのHover状態を取得するBookRayInteractionBridgeです。" +
        "通常は実際の本に付いているコンポーネントを指定します。"
    )]
    [SerializeField]
    private BookRayInteractionBridge rayInteractionBridge;

    [Header("Input")]

    [Tooltip(
        "ONの場合、Rayが本に当たっている間だけA/X・B/Yを受け付けます。"
    )]
    [SerializeField]
    private bool requireRayFocus = true;

    [Tooltip(
        "ONの場合、右手のA/Bに加えて左手のX/Yも同じ操作として扱います。"
    )]
    [SerializeField]
    private bool includeLeftController = true;

    [Tooltip("ボタン検出をConsoleへ表示します。動作確認時だけONにしてください。")]
    [SerializeField]
    private bool logButtonPresses;

#if ENABLE_INPUT_SYSTEM
    private InputAction primaryButtonAction;
    private InputAction secondaryButtonAction;
#endif

    private FieldInfo previousPrimaryButtonField;
    private FieldInfo previousSecondaryButtonField;
    private MethodInfo handleRightInputMethod;
    private MethodInfo handleLeftInputMethod;

    private bool reflectionReady;
    private bool setupErrorLogged;

    private void Awake()
    {
        ResolveReferences();
        CacheBookControllerMembers();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheBookControllerMembers();

#if ENABLE_INPUT_SYSTEM
        CreateAndEnableActions();
#else
        Debug.LogWarning(
            "BookInputSystemControllerBridgeは無効です。" +
            "Player SettingsのActive Input HandlingでInput Systemを有効にしてください。",
            this
        );
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        DisableAndDisposeActions();
#endif
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (
            !reflectionReady ||
            primaryButtonAction == null ||
            secondaryButtonAction == null
        )
        {
            return;
        }

        bool primaryPressed =
            primaryButtonAction.IsPressed();

        bool secondaryPressed =
            secondaryButtonAction.IsPressed();

        /*
         * BookController自身も旧XR入力APIを確認しています。
         * 同じボタンが両方の入力経路で検出されても二重操作に
         * ならないよう、BookController側の前フレーム状態を
         * 現在値へ同期します。
         */
        SynchronizeBookControllerButtonState(
            primaryPressed,
            secondaryPressed
        );

        bool canOperate =
            !requireRayFocus ||
            (
                rayInteractionBridge != null &&
                rayInteractionBridge.IsRayFocused
            );

        if (!canOperate)
        {
            return;
        }

        bool primaryPressedThisFrame =
            primaryButtonAction
                .WasPressedThisFrame();

        bool secondaryPressedThisFrame =
            secondaryButtonAction
                .WasPressedThisFrame();

        if (primaryPressedThisFrame)
        {
            if (logButtonPresses)
            {
                Debug.Log(
                    "Input System: A/X（primaryButton）を検出しました。",
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
                    "Input System: B/Y（secondaryButton）を検出しました。",
                    this
                );
            }

            InvokeBookInput(
                handleLeftInputMethod
            );
        }
#endif
    }

    private void ResolveReferences()
    {
        if (bookController == null)
        {
            bookController =
                GetComponent<BookController>() ??
                GetComponentInParent<BookController>();
        }

        if (rayInteractionBridge == null)
        {
            rayInteractionBridge =
                GetComponent<BookRayInteractionBridge>() ??
                GetComponentInParent<BookRayInteractionBridge>();
        }

        if (
            bookController == null &&
            !setupErrorLogged
        )
        {
            setupErrorLogged = true;

            Debug.LogError(
                "BookInputSystemControllerBridgeのBook Controllerが設定されていません。",
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

        Type controllerType =
            bookController.GetType();

        const BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        previousPrimaryButtonField =
            controllerType.GetField(
                "wasPrimaryButtonPressed",
                flags
            );

        previousSecondaryButtonField =
            controllerType.GetField(
                "wasSecondaryButtonPressed",
                flags
            );

        handleRightInputMethod =
            controllerType.GetMethod(
                "HandleRightInput",
                flags
            );

        handleLeftInputMethod =
            controllerType.GetMethod(
                "HandleLeftInput",
                flags
            );

        reflectionReady =
            previousPrimaryButtonField != null &&
            previousSecondaryButtonField != null &&
            handleRightInputMethod != null &&
            handleLeftInputMethod != null;

        if (
            !reflectionReady &&
            !setupErrorLogged
        )
        {
            setupErrorLogged = true;

            Debug.LogError(
                "BookControllerの入力メンバーを取得できません。" +
                "BookController.csのフィールド名またはメソッド名が変更されていないか確認してください。",
                this
            );
        }
    }

#if ENABLE_INPUT_SYSTEM
    private void CreateAndEnableActions()
    {
        DisableAndDisposeActions();

        primaryButtonAction =
            new InputAction(
                name: "BookPrimaryButton",
                type: InputActionType.Button
            );

        secondaryButtonAction =
            new InputAction(
                name: "BookSecondaryButton",
                type: InputActionType.Button
            );

        primaryButtonAction.AddBinding(
            "<XRController>{RightHand}/primaryButton"
        );

        secondaryButtonAction.AddBinding(
            "<XRController>{RightHand}/secondaryButton"
        );

        if (includeLeftController)
        {
            primaryButtonAction.AddBinding(
                "<XRController>{LeftHand}/primaryButton"
            );

            secondaryButtonAction.AddBinding(
                "<XRController>{LeftHand}/secondaryButton"
            );
        }

        primaryButtonAction.Enable();
        secondaryButtonAction.Enable();
    }

    private void DisableAndDisposeActions()
    {
        if (primaryButtonAction != null)
        {
            primaryButtonAction.Disable();
            primaryButtonAction.Dispose();
            primaryButtonAction = null;
        }

        if (secondaryButtonAction != null)
        {
            secondaryButtonAction.Disable();
            secondaryButtonAction.Dispose();
            secondaryButtonAction = null;
        }
    }
#endif

    private void SynchronizeBookControllerButtonState(
        bool primaryPressed,
        bool secondaryPressed
    )
    {
        if (
            !reflectionReady ||
            bookController == null
        )
        {
            return;
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

    private void InvokeBookInput(
        MethodInfo inputMethod
    )
    {
        if (
            bookController == null ||
            inputMethod == null
        )
        {
            return;
        }

        try
        {
            inputMethod.Invoke(
                bookController,
                null
            );
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(
                exception.InnerException ??
                exception,
                this
            );
        }
    }
}
