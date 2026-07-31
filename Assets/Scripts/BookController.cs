using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using echo17.EndlessBook;
using HtmlAgilityPack;
using UnityEngine;
using UnityEngine.XR;
using VersOne.Epub;

public sealed class BookController : MonoBehaviour
{
    private const int PagesPerSpread = 2;
    private const int FirstEndlessBookPageNumber = 1;

    private static readonly HashSet<string> BlockElementNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "address",
            "article",
            "aside",
            "blockquote",
            "div",
            "figcaption",
            "figure",
            "footer",
            "h1",
            "h2",
            "h3",
            "h4",
            "h5",
            "h6",
            "header",
            "li",
            "main",
            "nav",
            "ol",
            "p",
            "section",
            "table",
            "tr",
            "ul"
        };

    private enum ReaderState
    {
        Loading,
        Closed,
        Opening,
        Open,
        Turning,
        Closing,
        Error
    }

    [Header("EPUB")]

    [Tooltip(
        "Projectウィンドウに追加したEPUBファイルを指定します。"
    )]
    [SerializeField]
    private EpubAsset epubFile;

    [Header("Book References")]

    [Tooltip("EndlessBookコンポーネントを指定します。")]
    [SerializeField]
    private EndlessBook book;

    [Tooltip("PageRendererコンポーネントを指定します。")]
    [SerializeField]
    private PageRenderer pageRenderer;

    [Header("Pagination")]

    [Tooltip("1行に配置する最大文字数です。")]
    [SerializeField, Min(1)]
    private int charactersPerLine = 38;

    [Tooltip("1ページに配置する最大行数です。")]
    [SerializeField, Min(1)]
    private int linesPerPage = 16;

    [Header("Animation")]

    [Tooltip("本を開閉するアニメーション時間です。")]
    [SerializeField, Min(0.01f)]
    private float openCloseTime = 1.0f;

    [Tooltip("EndlessBookの1ページあたりのページ送り時間です。")]
    [SerializeField, Min(0.01f)]
    private float pageTurnTimePerPage = 0.5f;

    [Tooltip("アニメーション完了判定に加える余裕時間です。")]
    [SerializeField, Min(0f)]
    private float animationSafetyMargin = 0.1f;

    private readonly List<string> pages =
        new List<string>();

    private readonly Dictionary<int, Material> pageMaterialCache =
        new Dictionary<int, Material>();

    private readonly List<InputDevice> inputDevices =
        new List<InputDevice>();

    private ReaderState readerState = ReaderState.Loading;
    private int currentSpreadIndex;
    private int addedSpreadCount;
    private int operationVersion;

    private bool wasPrimaryButtonPressed;
    private bool wasSecondaryButtonPressed;

    private int SpreadCount =>
        (pages.Count + PagesPerSpread - 1) /
        PagesPerSpread;

    private void Start()
    {
        if (!ValidateReferences())
        {
            SetErrorState(
                "BookControllerの初期化に必要な参照が不足しています。"
            );
            return;
        }

        LoadAssignedEpub();
    }

    private void OnValidate()
    {
        charactersPerLine = Mathf.Max(1, charactersPerLine);
        linesPerPage = Mathf.Max(1, linesPerPage);
        openCloseTime = Mathf.Max(0.01f, openCloseTime);
        pageTurnTimePerPage =
            Mathf.Max(0.01f, pageTurnTimePerPage);
        animationSafetyMargin =
            Mathf.Max(0f, animationSafetyMargin);
    }

    private void OnDisable()
    {
        operationVersion++;
        StopAllCoroutines();

        wasPrimaryButtonPressed = false;
        wasSecondaryButtonPressed = false;

        if (
            readerState == ReaderState.Opening ||
            readerState == ReaderState.Turning ||
            readerState == ReaderState.Closing
        )
        {
            readerState =
                book != null &&
                book.CurrentState == EndlessBook.StateEnum.ClosedFront
                    ? ReaderState.Closed
                    : ReaderState.Open;
        }
    }

    private void OnDestroy()
    {
        if (pageRenderer != null)
        {
            pageRenderer.ReleaseGeneratedResources();
        }
    }

    private void Update()
    {
        ReadVrButtonEdges(
            out bool primaryPressedThisFrame,
            out bool secondaryPressedThisFrame
        );

        if (!CanAcceptInput())
        {
            return;
        }

        bool moveRight =
            Input.GetKeyDown(KeyCode.RightArrow) ||
            primaryPressedThisFrame;

        bool moveLeft =
            Input.GetKeyDown(KeyCode.LeftArrow) ||
            secondaryPressedThisFrame;

        if (moveRight)
        {
            HandleRightInput();
        }
        else if (moveLeft)
        {
            HandleLeftInput();
        }
    }

    private bool ValidateReferences()
    {
        bool isValid = true;

        if (epubFile == null)
        {
            Debug.LogError(
                "BookControllerのEPUB Fileが設定されていません。",
                this
            );
            isValid = false;
        }

        if (book == null)
        {
            Debug.LogError(
                "BookControllerのBookが設定されていません。",
                this
            );
            isValid = false;
        }

        if (pageRenderer == null)
        {
            Debug.LogError(
                "BookControllerのPage Rendererが設定されていません。",
                this
            );
            isValid = false;
        }
        else if (!pageRenderer.ValidateReferences(logErrors: true))
        {
            isValid = false;
        }

        return isValid;
    }

    private void LoadAssignedEpub()
    {
        ResetRuntimeState();

        if (epubFile == null)
        {
            SetErrorState("EPUB Fileが設定されていません。");
            return;
        }

        byte[] epubBytes = epubFile.Data;

        if (epubBytes == null || epubBytes.Length == 0)
        {
            SetErrorState(
                "指定されたEPUBにデータがありません: " +
                epubFile.OriginalFileName
            );
            return;
        }

        try
        {
            string fullBookContent =
                ExtractEpubContent(epubBytes);

            if (string.IsNullOrWhiteSpace(fullBookContent))
            {
                SetErrorState(
                    "EPUBから表示可能な本文を抽出できませんでした: " +
                    epubFile.OriginalFileName
                );
                return;
            }

            pages.AddRange(
                BookPaginator.Paginate(
                    fullBookContent,
                    charactersPerLine,
                    linesPerPage
                )
            );

            if (pages.Count == 0)
            {
                SetErrorState(
                    "EPUB本文をページへ分割できませんでした: " +
                    epubFile.OriginalFileName
                );
                return;
            }

            // 見開きの右ページが存在しない場合は空ページを追加します。
            if (pages.Count % PagesPerSpread != 0)
            {
                pages.Add(string.Empty);
            }

            readerState = ReaderState.Closed;

            Debug.Log(
                "EPUBの読み込みに成功しました: " +
                epubFile.OriginalFileName +
                " / pages=" +
                pages.Count +
                " / spreads=" +
                SpreadCount,
                this
            );
        }
        catch (Exception exception)
        {
            SetErrorState(
                "EPUBの読み込みまたは解析に失敗しました: " +
                exception.GetType().Name +
                ": " +
                exception.Message
            );
        }
    }

    private void ResetRuntimeState()
    {
        operationVersion++;
        StopAllCoroutines();

        pages.Clear();
        pageMaterialCache.Clear();

        currentSpreadIndex = 0;
        addedSpreadCount = 0;

        readerState = ReaderState.Loading;
    }

    private string ExtractEpubContent(byte[] epubData)
    {
        using MemoryStream epubStream =
            new MemoryStream(epubData, writable: false);

        EpubBook epubBook =
            EpubReader.ReadBook(epubStream);

        return ExtractContent(epubBook).Trim();
    }

    private void HandleRightInput()
    {
        if (readerState == ReaderState.Closed)
        {
            OpenBook();
            return;
        }

        if (readerState == ReaderState.Open)
        {
            TurnToNextSpread();
        }
    }

    private void HandleLeftInput()
    {
        if (readerState != ReaderState.Open)
        {
            return;
        }

        if (currentSpreadIndex <= 0)
        {
            CloseBook();
            return;
        }

        TurnToPreviousSpread();
    }

    private bool CanAcceptInput()
    {
        return
            readerState == ReaderState.Closed ||
            readerState == ReaderState.Open;
    }

    private void OpenBook()
    {
        try
        {
            EnsureSpreadAdded(0);

            currentSpreadIndex = 0;
            book.SetPageNumber(FirstEndlessBookPageNumber);

            int operationId =
                BeginOperation(ReaderState.Opening);

            book.SetState(
                EndlessBook.StateEnum.OpenMiddle,
                openCloseTime,
                (fromState, toState, pageNumber) =>
                    CompleteBookStateOperation(
                        operationId,
                        ReaderState.Open,
                        fromState,
                        toState,
                        pageNumber
                    )
            );

            StartCoroutine(
                CompleteOperationAfterDelay(
                    operationId,
                    openCloseTime + animationSafetyMargin,
                    ReaderState.Open
                )
            );
        }
        catch (Exception exception)
        {
            SetErrorState(
                "本を開く処理に失敗しました: " +
                exception.Message
            );
        }
    }

    private void TurnToNextSpread()
    {
        int targetSpreadIndex =
            currentSpreadIndex + 1;

        if (targetSpreadIndex >= SpreadCount)
        {
            CloseBook();
            return;
        }

        TurnToSpread(targetSpreadIndex);
    }

    private void TurnToPreviousSpread()
    {
        int targetSpreadIndex =
            currentSpreadIndex - 1;

        if (targetSpreadIndex < 0)
        {
            CloseBook();
            return;
        }

        TurnToSpread(targetSpreadIndex);
    }

    private void TurnToSpread(int targetSpreadIndex)
    {
        if (
            targetSpreadIndex < 0 ||
            targetSpreadIndex >= SpreadCount
        )
        {
            Debug.LogWarning(
                "範囲外の見開きが指定されました: " +
                targetSpreadIndex,
                this
            );
            return;
        }

        try
        {
            EnsureSpreadAdded(targetSpreadIndex);

            int targetPageNumber =
                GetEndlessBookPageNumber(targetSpreadIndex);

            int operationId =
                BeginOperation(ReaderState.Turning);

            book.TurnToPage(
                targetPageNumber,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                pageTurnTimePerPage
            );

            currentSpreadIndex = targetSpreadIndex;

            float lockDuration =
                pageTurnTimePerPage * PagesPerSpread +
                animationSafetyMargin;

            StartCoroutine(
                CompleteOperationAfterDelay(
                    operationId,
                    lockDuration,
                    ReaderState.Open
                )
            );
        }
        catch (Exception exception)
        {
            SetErrorState(
                "ページ送りに失敗しました: " +
                exception.Message
            );
        }
    }

    private void CloseBook()
    {
        if (
            readerState == ReaderState.Closed ||
            readerState == ReaderState.Closing
        )
        {
            return;
        }

        try
        {
            int operationId =
                BeginOperation(ReaderState.Closing);

            book.SetState(
                EndlessBook.StateEnum.ClosedFront,
                openCloseTime,
                (fromState, toState, pageNumber) =>
                    CompleteBookStateOperation(
                        operationId,
                        ReaderState.Closed,
                        fromState,
                        toState,
                        pageNumber
                    )
            );

            StartCoroutine(
                CompleteOperationAfterDelay(
                    operationId,
                    openCloseTime + animationSafetyMargin,
                    ReaderState.Closed
                )
            );
        }
        catch (Exception exception)
        {
            SetErrorState(
                "本を閉じる処理に失敗しました: " +
                exception.Message
            );
        }
    }

    private void EnsureSpreadAdded(int targetSpreadIndex)
    {
        while (addedSpreadCount <= targetSpreadIndex)
        {
            int spreadIndex = addedSpreadCount;
            int leftPageIndex = spreadIndex * PagesPerSpread;
            int rightPageIndex = leftPageIndex + 1;

            Material leftMaterial =
                GetOrCreatePageMaterial(
                    leftPageIndex,
                    isLeftPage: true
                );

            Material rightMaterial =
                GetOrCreatePageMaterial(
                    rightPageIndex,
                    isLeftPage: false
                );

            if (leftMaterial == null || rightMaterial == null)
            {
                throw new InvalidOperationException(
                    "ページMaterialの生成に失敗しました。"
                );
            }

            book.AddPageData(leftMaterial);
            book.AddPageData(rightMaterial);

            addedSpreadCount++;
        }
    }

    private Material GetOrCreatePageMaterial(
        int pageIndex,
        bool isLeftPage
    )
    {
        if (
            pageIndex < 0 ||
            pageIndex >= pages.Count
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                "ページ範囲外です。"
            );
        }

        if (
            pageMaterialCache.TryGetValue(
                pageIndex,
                out Material cachedMaterial
            )
        )
        {
            return cachedMaterial;
        }

        string resourceName =
            "BookPage_" +
            (pageIndex + 1).ToString("D4");

        Material material = isLeftPage
            ? pageRenderer.RenderLeftPageToMaterial(
                pages[pageIndex],
                resourceName
            )
            : pageRenderer.RenderRightPageToMaterial(
                pages[pageIndex],
                resourceName
            );

        pageMaterialCache.Add(pageIndex, material);

        return material;
    }

    private int GetEndlessBookPageNumber(int spreadIndex)
    {
        return
            FirstEndlessBookPageNumber +
            spreadIndex * PagesPerSpread;
    }

    private int BeginOperation(ReaderState state)
    {
        operationVersion++;
        readerState = state;
        return operationVersion;
    }

    private void CompleteBookStateOperation(
        int operationId,
        ReaderState completedState,
        EndlessBook.StateEnum fromState,
        EndlessBook.StateEnum toState,
        int pageNumber
    )
    {
        Debug.Log(
            "Book state changed from " +
            fromState +
            " to " +
            toState +
            ", page=" +
            pageNumber,
            this
        );

        CompleteOperation(operationId, completedState);
    }

    private IEnumerator CompleteOperationAfterDelay(
        int operationId,
        float delay,
        ReaderState completedState
    )
    {
        yield return new WaitForSecondsRealtime(
            Mathf.Max(0.01f, delay)
        );

        CompleteOperation(operationId, completedState);
    }

    private void CompleteOperation(
        int operationId,
        ReaderState completedState
    )
    {
        if (operationId != operationVersion)
        {
            return;
        }

        readerState = completedState;

        if (completedState == ReaderState.Closed)
        {
            currentSpreadIndex = 0;
            book.SetPageNumber(FirstEndlessBookPageNumber);
        }
    }

    private void ReadVrButtonEdges(
        out bool primaryPressedThisFrame,
        out bool secondaryPressedThisFrame
    )
    {
        inputDevices.Clear();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller,
            inputDevices
        );

        bool primaryButtonPressed = false;
        bool secondaryButtonPressed = false;

        foreach (InputDevice device in inputDevices)
        {
            if (
                device.TryGetFeatureValue(
                    CommonUsages.primaryButton,
                    out bool primaryPressed
                ) &&
                primaryPressed
            )
            {
                primaryButtonPressed = true;
            }

            if (
                device.TryGetFeatureValue(
                    CommonUsages.secondaryButton,
                    out bool secondaryPressed
                ) &&
                secondaryPressed
            )
            {
                secondaryButtonPressed = true;
            }
        }

        primaryPressedThisFrame =
            primaryButtonPressed &&
            !wasPrimaryButtonPressed;

        secondaryPressedThisFrame =
            secondaryButtonPressed &&
            !wasSecondaryButtonPressed;

        wasPrimaryButtonPressed = primaryButtonPressed;
        wasSecondaryButtonPressed = secondaryButtonPressed;
    }

    private void SetErrorState(string message)
    {
        operationVersion++;
        StopAllCoroutines();
        readerState = ReaderState.Error;

        Debug.LogError(message, this);
    }

    private string ExtractContent(EpubBook epubBook)
    {
        if (epubBook == null)
        {
            return string.Empty;
        }

        StringBuilder fullContent =
            new StringBuilder();

        foreach (
            EpubLocalTextContentFile textContentFile
            in epubBook.ReadingOrder
        )
        {
            string content =
                ExtractPlainText(textContentFile);

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (fullContent.Length > 0)
            {
                AppendLineBreak(fullContent);
                AppendLineBreak(fullContent);
            }

            fullContent.Append(content.Trim());
        }

        return NormalizeExtractedText(
            fullContent.ToString()
        );
    }

    private string ExtractPlainText(
        EpubLocalTextContentFile textContentFile
    )
    {
        if (
            textContentFile == null ||
            string.IsNullOrWhiteSpace(
                textContentFile.Content
            )
        )
        {
            return string.Empty;
        }

        HtmlDocument htmlDocument =
            new HtmlDocument();

        htmlDocument.LoadHtml(
            textContentFile.Content
        );

        StringBuilder textBuilder =
            new StringBuilder();

        AppendNodeText(
            htmlDocument.DocumentNode,
            textBuilder
        );

        return NormalizeExtractedText(
            textBuilder.ToString()
        );
    }

    private void AppendNodeText(
        HtmlNode node,
        StringBuilder builder
    )
    {
        if (node == null)
        {
            return;
        }

        if (
            node.NodeType == HtmlNodeType.Comment ||
            node.Name.Equals(
                "script",
                StringComparison.OrdinalIgnoreCase
            ) ||
            node.Name.Equals(
                "style",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return;
        }

        if (node.NodeType == HtmlNodeType.Text)
        {
            AppendTextNode(node.InnerText, builder);
            return;
        }

        if (
            node.Name.Equals(
                "br",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            AppendLineBreak(builder);
            return;
        }

        bool isBlockElement =
            BlockElementNames.Contains(node.Name);

        if (isBlockElement)
        {
            AppendLineBreak(builder);
        }

        foreach (HtmlNode childNode in node.ChildNodes)
        {
            AppendNodeText(childNode, builder);
        }

        if (isBlockElement)
        {
            AppendLineBreak(builder);
        }
    }

    private void AppendTextNode(
        string rawText,
        StringBuilder builder
    )
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return;
        }

        string decodedText =
            HtmlEntity.DeEntitize(rawText)
                .Replace('\u00A0', ' ');

        bool hadLeadingWhitespace =
            char.IsWhiteSpace(decodedText[0]);

        bool hadTrailingWhitespace =
            char.IsWhiteSpace(
                decodedText[decodedText.Length - 1]
            );

        string normalizedText = Regex.Replace(
            decodedText.Trim(),
            @"\s+",
            " "
        );

        if (normalizedText.Length == 0)
        {
            return;
        }

        if (
            hadLeadingWhitespace &&
            builder.Length > 0 &&
            !char.IsWhiteSpace(builder[builder.Length - 1])
        )
        {
            builder.Append(' ');
        }

        builder.Append(normalizedText);

        if (hadTrailingWhitespace)
        {
            builder.Append(' ');
        }
    }

    private static void AppendLineBreak(
        StringBuilder builder
    )
    {
        while (
            builder.Length > 0 &&
            builder[builder.Length - 1] == ' '
        )
        {
            builder.Length--;
        }

        if (
            builder.Length > 0 &&
            builder[builder.Length - 1] != '\n'
        )
        {
            builder.Append('\n');
        }
    }

    private static string NormalizeExtractedText(
        string text
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ');

        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @" *\n *", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");

        return normalized.Trim();
    }
}
