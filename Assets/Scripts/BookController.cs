using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using echo17.EndlessBook;
using HtmlAgilityPack;
using UnityEngine;
using UnityEngine.XR;
using VersOne.Epub;

public class BookController : MonoBehaviour
{
    private const int CharsPerLine = 38;
    private const int MaxLinesPerPage = 16;
    private const int CharsPerPage =
        CharsPerLine * MaxLinesPerPage;

    private const int ParagraphBreakFrequency = 6;

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

    [Header("Animation")]

    [Tooltip("本を開閉するアニメーション時間です。")]
    [SerializeField]
    private float openCloseTime = 1.0f;

    [Header("Input")]

    [Tooltip("ページ操作を連続して受け付けない時間です。")]
    [SerializeField]
    private float inputDelay = 0.5f;

    [Header("Audio")]

    [SerializeField]
    private AudioClip bookOpenClip;

    [SerializeField]
    private AudioClip bookCloseClip;

    [SerializeField]
    private AudioClip pageTurnClip;

    // EPUB全体から抽出した本文
    private string fullBookContent = string.Empty;

    // 次にページへ追加する本文の文字位置
    private int charIndex;

    // 生成済みの各ページの開始文字位置
    private readonly List<int> pageStartIndices =
        new List<int>();

    // 現在の左ページに対応するインデックス
    private int currentLeftPageIndex;

    // EPUBの読み込みが完了しているか
    private bool isEpubLoaded;

    // 最後にページ操作を受け付けた時刻
    private float lastInputTime;

    // VRコントローラー取得用リスト
    private readonly List<InputDevice> inputDevices =
        new List<InputDevice>();

    // 音声再生用AudioSource
    private AudioSource bookOpenSound;
    private AudioSource bookCloseSound;
    private AudioSource pageTurnSound;

    private void Start()
    {
        LoadAudioClips();

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        LoadAssignedEpub();
    }

    private void Update()
    {
        if (!isEpubLoaded)
        {
            return;
        }

        if (Time.time - lastInputTime <= inputDelay)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            HandleRightTurn();
            lastInputTime = Time.time;
            return;
        }

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            HandleLeftTurn();
            lastInputTime = Time.time;
            return;
        }

        CheckVrControllerInput();
    }

    /// <summary>
    /// Inspector上の必須参照が設定されているか確認します。
    /// </summary>
    private bool ValidateReferences()
    {
        bool isValid = true;

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

        return isValid;
    }

    /// <summary>
    /// Inspectorで指定されたEPUBを読み込みます。
    /// </summary>
    private void LoadAssignedEpub()
    {
        ResetEpubState();

        if (epubFile == null)
        {
            Debug.LogError(
                "EPUB Fileが設定されていません。" +
                "Projectウィンドウの.epubファイルを、" +
                "BookControllerのEPUB File欄へドラッグしてください。",
                this
            );

            return;
        }

        byte[] epubBytes = epubFile.Data;

        if (epubBytes == null || epubBytes.Length == 0)
        {
            Debug.LogError(
                "指定されたEPUBにデータがありません: " +
                epubFile.OriginalFileName,
                this
            );

            return;
        }

        try
        {
            Debug.Log(
                "EPUBを読み込みます: " +
                epubFile.OriginalFileName,
                this
            );

            Debug.Log(
                "EPUBサイズ: " +
                epubBytes.Length +
                " bytes",
                this
            );

            HandleEpubData(epubBytes);

            if (string.IsNullOrWhiteSpace(fullBookContent))
            {
                Debug.LogError(
                    "EPUBから表示可能な本文を抽出できませんでした: " +
                    epubFile.OriginalFileName,
                    this
                );

                return;
            }

            isEpubLoaded = true;

            Debug.Log(
                "EPUBの読み込みに成功しました: " +
                epubFile.OriginalFileName,
                this
            );

            Debug.Log(
                "抽出文字数: " +
                fullBookContent.Length,
                this
            );
        }
        catch (System.Exception exception)
        {
            isEpubLoaded = false;
            fullBookContent = string.Empty;

            Debug.LogError(
                "EPUBの読み込みまたは解析に失敗しました: " +
                exception.GetType().Name +
                ": " +
                exception.Message,
                this
            );
        }
    }

    /// <summary>
    /// EPUB関連の状態を初期化します。
    /// </summary>
    private void ResetEpubState()
    {
        isEpubLoaded = false;
        fullBookContent = string.Empty;

        charIndex = 0;
        currentLeftPageIndex = 0;

        pageStartIndices.Clear();
    }

    /// <summary>
    /// EPUBのバイト配列をVersOne.Epubで解析します。
    /// </summary>
    private void HandleEpubData(byte[] epubData)
    {
        using MemoryStream epubStream =
            new MemoryStream(epubData);

        EpubBook epubBook =
            EpubReader.ReadBook(epubStream);

        fullBookContent =
            ExtractContent(epubBook).Trim();

        string preview =
            fullBookContent.Length > 20
                ? fullBookContent.Substring(0, 20)
                : fullBookContent;

        Debug.Log(
            "本文の先頭: " +
            preview,
            this
        );
    }

    /// <summary>
    /// Resourcesフォルダから効果音を読み込みます。
    /// Inspectorですでに設定されている場合は、その設定を優先します。
    /// </summary>
    private void LoadAudioClips()
    {
        if (bookOpenClip == null)
        {
            bookOpenClip =
                Resources.Load<AudioClip>(
                    "Sounds/BookOpen"
                );
        }

        if (bookCloseClip == null)
        {
            bookCloseClip =
                Resources.Load<AudioClip>(
                    "Sounds/BookClose"
                );
        }

        if (pageTurnClip == null)
        {
            pageTurnClip =
                Resources.Load<AudioClip>(
                    "Sounds/PageTurn"
                );
        }

        bookOpenSound =
            CreateAudioSource(bookOpenClip);

        bookCloseSound =
            CreateAudioSource(bookCloseClip);

        pageTurnSound =
            CreateAudioSource(pageTurnClip);
    }

    /// <summary>
    /// 効果音用AudioSourceを作成します。
    /// </summary>
    private AudioSource CreateAudioSource(
        AudioClip clip
    )
    {
        AudioSource audioSource =
            gameObject.AddComponent<AudioSource>();

        audioSource.clip = clip;
        audioSource.playOnAwake = false;

        return audioSource;
    }

    /// <summary>
    /// AudioClipが設定されている場合だけ再生します。
    /// </summary>
    private static void PlaySound(
        AudioSource audioSource
    )
    {
        if (
            audioSource != null &&
            audioSource.clip != null
        )
        {
            audioSource.Play();
        }
    }

    /// <summary>
    /// VRコントローラーのボタン入力を確認します。
    /// </summary>
    private void CheckVrControllerInput()
    {
        inputDevices.Clear();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller,
            inputDevices
        );

        foreach (InputDevice device in inputDevices)
        {
            bool primaryPressed;

            if (
                device.TryGetFeatureValue(
                    CommonUsages.primaryButton,
                    out primaryPressed
                ) &&
                primaryPressed
            )
            {
                HandleRightTurn();
                lastInputTime = Time.time;
                return;
            }

            bool secondaryPressed;

            if (
                device.TryGetFeatureValue(
                    CommonUsages.secondaryButton,
                    out secondaryPressed
                ) &&
                secondaryPressed
            )
            {
                HandleLeftTurn();
                lastInputTime = Time.time;
                return;
            }
        }
    }

    /// <summary>
    /// 本を開く、または右方向へページを進めます。
    /// </summary>
    private void HandleRightTurn()
    {
        if (!CanOperateBook())
        {
            return;
        }

        Debug.Log("Turn right", this);

        if (
            book.CurrentState ==
            EndlessBook.StateEnum.ClosedFront
        )
        {
            OpenBookAndAddFirstPages();
        }
        else
        {
            TurnPageRight();
        }
    }

    /// <summary>
    /// 左方向へページを戻します。
    /// </summary>
    private void HandleLeftTurn()
    {
        if (!CanOperateBook())
        {
            return;
        }

        Debug.Log("Turn left", this);

        TurnPageLeft();
    }

    /// <summary>
    /// ページ操作が可能か確認します。
    /// </summary>
    private bool CanOperateBook()
    {
        if (!isEpubLoaded)
        {
            Debug.LogWarning(
                "EPUBの読み込みが完了していないため、" +
                "ページを操作できません。",
                this
            );

            return false;
        }

        if (string.IsNullOrEmpty(fullBookContent))
        {
            Debug.LogWarning(
                "本文が空のため、ページを操作できません。",
                this
            );

            return false;
        }

        if (book == null || pageRenderer == null)
        {
            Debug.LogError(
                "BookまたはPage Rendererが設定されていません。",
                this
            );

            return false;
        }

        return true;
    }

    /// <summary>
    /// 本を開き、最初の左右ページを生成します。
    /// </summary>
    private void OpenBookAndAddFirstPages()
    {
        book.SetState(
            EndlessBook.StateEnum.OpenMiddle,
            openCloseTime,
            OnBookStateChanged
        );

        PlaySound(bookOpenSound);

        if (pageStartIndices.Count == 0)
        {
            AddNewPage();
        }
        else
        {
            string leftPageText =
                RenderPageBasedOnIndex(
                    currentLeftPageIndex
                );

            string rightPageText =
                RenderPageBasedOnIndex(
                    currentLeftPageIndex + 1
                );

            Material leftPageMaterial =
                pageRenderer.RenderLeftPageToMaterial(
                    leftPageText
                );

            Material rightPageMaterial =
                pageRenderer.RenderRightPageToMaterial(
                    rightPageText
                );

            book.UpdatePageDataMaterial(
                book.CurrentLeftPageNumber,
                leftPageMaterial
            );

            book.UpdatePageDataMaterial(
                book.CurrentRightPageNumber,
                rightPageMaterial
            );

            currentLeftPageIndex += 2;
        }
    }

    /// <summary>
    /// 右方向へページを進めます。
    /// </summary>
    private void TurnPageRight()
    {
        if (
            currentLeftPageIndex <
            pageStartIndices.Count - 1
        )
        {
            currentLeftPageIndex += 2;

            Debug.Log(
                "After page turn: " +
                currentLeftPageIndex,
                this
            );

            UpdatePageMaterials();

            PlaySound(pageTurnSound);

            book.TurnToPage(
                book.CurrentLeftPageNumber + 2,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                0.5f
            );

            return;
        }

        if (IsAtLastPage())
        {
            if (charIndex >= fullBookContent.Length)
            {
                Debug.Log(
                    "本文の最後に到達したため、本を閉じます。",
                    this
                );

                CloseAndResetBook();
                return;
            }

            Debug.Log(
                "新しいページを追加します。",
                this
            );

            currentLeftPageIndex += 2;

            AddNewPage();

            PlaySound(pageTurnSound);

            book.TurnToPage(
                book.CurrentLeftPageNumber + 2,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                0.5f
            );

            return;
        }

        Debug.Log(
            "本を閉じます。",
            this
        );

        CloseAndResetBook();
    }

    /// <summary>
    /// 左方向へページを戻します。
    /// </summary>
    private void TurnPageLeft()
    {
        if (currentLeftPageIndex > 2)
        {
            currentLeftPageIndex -= 2;

            UpdatePageMaterials();

            Debug.Log(
                "After page turn: " +
                currentLeftPageIndex,
                this
            );

            book.TurnToPage(
                book.CurrentLeftPageNumber - 2,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                0.3f
            );

            PlaySound(pageTurnSound);
        }
        else
        {
            if (
                book.CurrentState !=
                EndlessBook.StateEnum.ClosedFront
            )
            {
                CloseBookWithoutResettingContent();
            }
        }
    }

    /// <summary>
    /// 本を閉じ、表示位置を先頭へ戻します。
    /// EPUB本文自体は再読み込みしません。
    /// </summary>
    private void CloseAndResetBook()
    {
        PlaySound(bookCloseSound);

        book.SetState(
            EndlessBook.StateEnum.ClosedFront,
            openCloseTime,
            OnBookStateChanged
        );

        currentLeftPageIndex = 0;

        book.SetPageNumber(1);
    }

    /// <summary>
    /// 本文の生成状態を維持したまま本を閉じます。
    /// </summary>
    private void CloseBookWithoutResettingContent()
    {
        PlaySound(bookCloseSound);

        book.SetState(
            EndlessBook.StateEnum.ClosedFront,
            openCloseTime,
            OnBookStateChanged
        );

        currentLeftPageIndex = 0;

        book.SetPageNumber(1);
    }

    /// <summary>
    /// 新しい左右ページを生成してEndlessBookへ追加します。
    /// </summary>
    private void AddNewPage()
    {
        string leftPageText =
            GetNextPageText();

        string rightPageText =
            GetNextPageText();

        if (
            string.IsNullOrEmpty(leftPageText) &&
            string.IsNullOrEmpty(rightPageText)
        )
        {
            Debug.Log(
                "追加できる本文がありません。",
                this
            );

            return;
        }

        Material leftPageMaterial =
            pageRenderer.RenderLeftPageToMaterial(
                leftPageText ?? string.Empty
            );

        Material rightPageMaterial =
            pageRenderer.RenderRightPageToMaterial(
                rightPageText ?? string.Empty
            );

        book.AddPageData(leftPageMaterial);
        book.AddPageData(rightPageMaterial);
    }

    /// <summary>
    /// 現在位置に対応する左右ページのマテリアルを更新します。
    /// </summary>
    private void UpdatePageMaterials()
    {
        int leftPageIndex =
            currentLeftPageIndex - 2;

        int rightPageIndex =
            currentLeftPageIndex - 1;

        string leftPageText =
            RenderPageBasedOnIndex(leftPageIndex);

        string rightPageText =
            RenderPageBasedOnIndex(rightPageIndex);

        Material leftPageMaterial =
            pageRenderer.RenderLeftPageToMaterial(
                leftPageText
            );

        Material rightPageMaterial =
            pageRenderer.RenderRightPageToMaterial(
                rightPageText
            );

        book.UpdatePageDataMaterial(
            book.CurrentLeftPageNumber,
            leftPageMaterial
        );

        book.UpdatePageDataMaterial(
            book.CurrentRightPageNumber,
            rightPageMaterial
        );
    }

    /// <summary>
    /// 現在、EndlessBook上の最後のページにいるか確認します。
    /// </summary>
    private bool IsAtLastPage()
    {
        return
            book.CurrentRightPageNumber >=
            book.LastPageNumber;
    }

    /// <summary>
    /// 指定ページインデックスの本文を生成します。
    /// </summary>
    private string RenderPageBasedOnIndex(
        int pageIndex
    )
    {
        if (
            pageIndex < 0 ||
            pageIndex >= pageStartIndices.Count
        )
        {
            return string.Empty;
        }

        int startIndex =
            pageStartIndices[pageIndex];

        int endIndex =
            pageIndex <
            pageStartIndices.Count - 1
                ? pageStartIndices[pageIndex + 1]
                : fullBookContent.Length;

        if (
            startIndex < 0 ||
            startIndex >= fullBookContent.Length ||
            endIndex < startIndex
        )
        {
            return string.Empty;
        }

        endIndex =
            Mathf.Min(
                endIndex,
                fullBookContent.Length
            );

        string pageText =
            fullBookContent.Substring(
                startIndex,
                endIndex - startIndex
            );

        return FormatPageText(pageText);
    }

    /// <summary>
    /// 次の1ページ分の本文を取得します。
    /// 最終ページが608文字未満でも取得します。
    /// </summary>
    private string GetNextPageText()
    {
        if (
            string.IsNullOrEmpty(fullBookContent) ||
            charIndex >= fullBookContent.Length
        )
        {
            return null;
        }

        pageStartIndices.Add(charIndex);

        int remainingCharacters =
            fullBookContent.Length - charIndex;

        int pageLength =
            Mathf.Min(
                CharsPerPage,
                remainingCharacters
            );

        string pageText =
            fullBookContent.Substring(
                charIndex,
                pageLength
            );

        charIndex += pageLength;

        return FormatPageText(pageText);
    }

    /// <summary>
    /// 1ページ分の文字列へ改行を追加します。
    /// </summary>
    private string FormatPageText(
        string pageText
    )
    {
        if (string.IsNullOrEmpty(pageText))
        {
            return string.Empty;
        }

        StringBuilder pageBuilder =
            new StringBuilder();

        int lineCounter = 0;

        for (
            int index = 0;
            index < pageText.Length;
            index += CharsPerLine
        )
        {
            int length =
                Mathf.Min(
                    CharsPerLine,
                    pageText.Length - index
                );

            pageBuilder.AppendLine(
                pageText.Substring(
                    index,
                    length
                )
            );

            lineCounter++;

            if (
                lineCounter >=
                ParagraphBreakFrequency &&
                Random.Range(0, 2) > 0
            )
            {
                pageBuilder.AppendLine();
                lineCounter = 0;
            }
        }

        return pageBuilder
            .ToString()
            .TrimEnd(' ', '\r', '\n');
    }

    /// <summary>
    /// EPUBのReadingOrderから本文を抽出します。
    /// </summary>
    private string ExtractContent(
        EpubBook epubBook
    )
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

            string normalizedContent =
                Regex.Replace(
                    content,
                    @"\s+",
                    " "
                );

            if (
                fullContent.Length > 0 &&
                !char.IsWhiteSpace(
                    fullContent[
                        fullContent.Length - 1
                    ]
                )
            )
            {
                fullContent.Append(' ');
            }

            fullContent.Append(
                normalizedContent.Trim()
            );
        }

        return fullContent.ToString();
    }

    /// <summary>
    /// XHTMLまたはHTMLから表示用のプレーンテキストを抽出します。
    /// </summary>
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

        HtmlNodeCollection textNodes =
            htmlDocument.DocumentNode.SelectNodes(
                "//text()[not(ancestor::script)" +
                " and not(ancestor::style)]"
            );

        if (textNodes == null)
        {
            return string.Empty;
        }

        StringBuilder textBuilder =
            new StringBuilder();

        foreach (HtmlNode node in textNodes)
        {
            if (node == null)
            {
                continue;
            }

            string text =
                HtmlEntity.DeEntitize(
                    node.InnerText
                );

            text = Regex.Replace(
                text,
                @"\r\n?|\n",
                " "
            );

            text = text.Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (textBuilder.Length > 0)
            {
                textBuilder.Append(' ');
            }

            textBuilder.Append(text);
        }

        return Regex.Replace(
            textBuilder.ToString(),
            @"[ ]{2,}",
            " "
        );
    }

    /// <summary>
    /// EndlessBookの状態変化時に呼ばれます。
    /// </summary>
    private void OnBookStateChanged(
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
            ", page: " +
            pageNumber,
            this
        );
    }
}