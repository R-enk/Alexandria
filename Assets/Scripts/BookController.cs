using System;
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
    [Header("Book")]
    public EndlessBook book;
    public PageRenderer pageRenderer;
    public float openCloseTime = 1.0f;

    [Header("Page layout")]
    [SerializeField] private int charsPerLine = 38;
    [SerializeField] private int maxLinesPerPage = 16;

    [Header("Audio")]
    public AudioClip bookOpenClip;
    public AudioClip bookCloseClip;
    public AudioClip pageTurnClip;

    private readonly List<int> pageStartIndices = new List<int>();
    private string fullBookContent;
    private int charIndex;
    // The value points to the position immediately after the currently displayed page pair.
    private int currentLeftPageIndex = 2;
    private bool isEpubLoaded;

    private AudioSource bookOpenSound;
    private AudioSource bookCloseSound;
    private AudioSource pageTurnSound;

    private const float InputDelay = 0.5f;
    private float lastInputTime;

    public bool IsEpubLoaded => isEpubLoaded;

    private void Awake()
    {
        LoadAudioClips();
    }

    private void LoadAudioClips()
    {
        bookOpenClip = bookOpenClip != null
            ? bookOpenClip
            : Resources.Load<AudioClip>("Sounds/BookOpen");
        bookCloseClip = bookCloseClip != null
            ? bookCloseClip
            : Resources.Load<AudioClip>("Sounds/BookClose");
        pageTurnClip = pageTurnClip != null
            ? pageTurnClip
            : Resources.Load<AudioClip>("Sounds/PageTurn");

        bookOpenSound = CreateAudioSource(bookOpenClip);
        bookCloseSound = CreateAudioSource(bookCloseClip);
        pageTurnSound = CreateAudioSource(pageTurnClip);
    }

    private AudioSource CreateAudioSource(AudioClip clip)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.playOnAwake = false;
        return source;
    }

    /// <summary>
    /// Loads the EPUB selected by FileBrowserController.
    /// A fixed file name and UnityWebRequest are intentionally not used because
    /// the selected file is a normal local file.
    /// </summary>
    public bool LoadEpubFromPath(string filePath)
    {
        ResetLoadedBookState();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            Debug.LogError("EPUB path is empty.");
            return false;
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            Debug.LogError("Invalid EPUB path: " + ex.Message);
            return false;
        }

        if (!File.Exists(fullPath))
        {
            Debug.LogError("EPUB file was not found: " + fullPath);
            return false;
        }

        try
        {
            Debug.Log("Loading EPUB from: " + fullPath);

            byte[] epubBytes = File.ReadAllBytes(fullPath);
            if (epubBytes.Length == 0)
            {
                Debug.LogError("EPUB file is empty: " + fullPath);
                return false;
            }

            HandleEpubData(epubBytes);

            if (string.IsNullOrWhiteSpace(fullBookContent))
            {
                Debug.LogError("No readable text was extracted from the EPUB.");
                return false;
            }

            isEpubLoaded = true;

            Debug.Log("EPUB loaded successfully.");
            Debug.Log("EPUB size: " + epubBytes.Length + " bytes");
            Debug.Log("Extracted character count: " + fullBookContent.Length);
            Debug.Log(
                "Book content: "
                + (fullBookContent.Length > 40
                    ? fullBookContent.Substring(0, 40)
                    : fullBookContent)
            );

            return true;
        }
        catch (Exception ex)
        {
            ResetLoadedBookState();
            Debug.LogError(
                "Failed to load or parse EPUB: "
                + ex.GetType().Name
                + ": "
                + ex.Message
            );
            return false;
        }
    }

    private void ResetLoadedBookState()
    {
        isEpubLoaded = false;
        fullBookContent = null;
        charIndex = 0;
        currentLeftPageIndex = 2;
        pageStartIndices.Clear();

        if (book != null)
        {
            book.SetPageNumber(1);
        }
    }

    private void HandleEpubData(byte[] epubData)
    {
        using (MemoryStream epubStream = new MemoryStream(epubData))
        {
            EpubBook epubBook = EpubReader.ReadBook(epubStream);
            fullBookContent = ExtractContent(epubBook);
        }
    }

    private void Update()
    {
        if (!isEpubLoaded || book == null || pageRenderer == null)
        {
            return;
        }

        if (Time.time - lastInputTime <= InputDelay)
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

        var inputDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller,
            inputDevices
        );

        foreach (InputDevice device in inputDevices)
        {
            if (
                device.TryGetFeatureValue(
                    CommonUsages.primaryButton,
                    out bool primaryButtonPressed
                )
                && primaryButtonPressed
            )
            {
                HandleRightTurn();
                lastInputTime = Time.time;
                break;
            }

            if (
                device.TryGetFeatureValue(
                    CommonUsages.secondaryButton,
                    out bool secondaryButtonPressed
                )
                && secondaryButtonPressed
            )
            {
                HandleLeftTurn();
                lastInputTime = Time.time;
                break;
            }
        }
    }

    private void HandleRightTurn()
    {
        if (!CanOperateBook())
        {
            return;
        }

        Debug.Log("Turn right");

        if (book.CurrentState == EndlessBook.StateEnum.ClosedFront)
        {
            OpenBookAndAddFirstPages();
        }
        else
        {
            TurnPageRight();
        }
    }

    private void HandleLeftTurn()
    {
        if (!CanOperateBook())
        {
            return;
        }

        Debug.Log("Turn left");
        TurnPageLeft();
    }

    private bool CanOperateBook()
    {
        if (!isEpubLoaded || string.IsNullOrEmpty(fullBookContent))
        {
            Debug.LogWarning("The EPUB has not finished loading.");
            return false;
        }

        if (book == null || pageRenderer == null)
        {
            Debug.LogError("Book or PageRenderer is not assigned.");
            return false;
        }

        return true;
    }

    private void OpenBookAndAddFirstPages()
    {
        if (pageStartIndices.Count == 0)
        {
            if (!AddNewPagePair())
            {
                Debug.LogWarning("The EPUB does not contain renderable page text.");
                return;
            }

            currentLeftPageIndex = 2;
        }
        else
        {
            currentLeftPageIndex = 2;
            UpdatePageMaterials();
        }

        PlaySound(bookOpenSound);
        book.SetState(
            EndlessBook.StateEnum.OpenMiddle,
            openCloseTime,
            OnBookStateChanged
        );
    }

    private void TurnPageRight()
    {
        // Move to a page pair that has already been generated.
        if (currentLeftPageIndex < pageStartIndices.Count)
        {
            currentLeftPageIndex += 2;
            UpdatePageMaterials();
            PlaySound(pageTurnSound);
            book.TurnToPage(
                book.CurrentLeftPageNumber + 2,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                0.5f
            );
            return;
        }

        // Generate the next page pair only when unread text remains.
        if (charIndex < fullBookContent.Length)
        {
            if (!AddNewPagePair())
            {
                CloseBookAtEnd();
                return;
            }

            currentLeftPageIndex += 2;
            PlaySound(pageTurnSound);
            book.TurnToPage(
                book.CurrentLeftPageNumber + 2,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                0.5f
            );
            return;
        }

        CloseBookAtEnd();
    }

    private void TurnPageLeft()
    {
        // currentLeftPageIndex == 2 means that the first page pair is displayed.
        if (currentLeftPageIndex > 2)
        {
            currentLeftPageIndex -= 2;
            UpdatePageMaterials();
            PlaySound(pageTurnSound);
            book.TurnToPage(
                book.CurrentLeftPageNumber - 2,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                0.3f
            );
            return;
        }

        CloseBook();
    }

    private void CloseBookAtEnd()
    {
        Debug.Log("Last page and end of content, close book");
        currentLeftPageIndex = 2;
        book.SetPageNumber(1);
        CloseBook();
    }

    private void CloseBook()
    {
        if (book.CurrentState == EndlessBook.StateEnum.ClosedFront)
        {
            return;
        }

        PlaySound(bookCloseSound);
        book.SetState(
            EndlessBook.StateEnum.ClosedFront,
            openCloseTime,
            OnBookStateChanged
        );
    }

    private void OnBookStateChanged(
        EndlessBook.StateEnum fromState,
        EndlessBook.StateEnum toState,
        int pageNumber
    )
    {
        Debug.Log(
            "Book state changed from "
            + fromState
            + " to "
            + toState
            + " at page "
            + pageNumber
        );
    }

    private bool AddNewPagePair()
    {
        string leftPageText = GetNextPageText();
        if (leftPageText == null)
        {
            return false;
        }

        string rightPageText = GetNextPageText() ?? string.Empty;

        Material leftPageMaterial =
            pageRenderer.RenderLeftPageToMaterial(leftPageText);
        Material rightPageMaterial =
            pageRenderer.RenderRightPageToMaterial(rightPageText);

        book.AddPageData(leftPageMaterial);
        book.AddPageData(rightPageMaterial);
        return true;
    }

    private void UpdatePageMaterials()
    {
        int leftIndex = currentLeftPageIndex - 2;
        int rightIndex = currentLeftPageIndex - 1;

        string leftPageText = RenderPageBasedOnIndex(leftIndex);
        string rightPageText = RenderPageBasedOnIndex(rightIndex);

        Material leftPageMaterial =
            pageRenderer.RenderLeftPageToMaterial(leftPageText);
        Material rightPageMaterial =
            pageRenderer.RenderRightPageToMaterial(rightPageText);

        book.UpdatePageDataMaterial(
            book.CurrentLeftPageNumber,
            leftPageMaterial
        );
        book.UpdatePageDataMaterial(
            book.CurrentRightPageNumber,
            rightPageMaterial
        );
    }

    private string RenderPageBasedOnIndex(int pageIndex)
    {
        if (
            pageIndex < 0
            || pageIndex >= pageStartIndices.Count
            || string.IsNullOrEmpty(fullBookContent)
        )
        {
            return string.Empty;
        }

        int startIndex = pageStartIndices[pageIndex];
        int endIndex = pageIndex < pageStartIndices.Count - 1
            ? pageStartIndices[pageIndex + 1]
            : fullBookContent.Length;

        string pageText = fullBookContent.Substring(
            startIndex,
            endIndex - startIndex
        );

        return FormatPageText(pageText);
    }

    private string GetNextPageText()
    {
        if (
            string.IsNullOrEmpty(fullBookContent)
            || charIndex >= fullBookContent.Length
        )
        {
            return null;
        }

        int charsPerPage =
            Mathf.Max(1, charsPerLine) * Mathf.Max(1, maxLinesPerPage);

        pageStartIndices.Add(charIndex);

        int length = Mathf.Min(
            charsPerPage,
            fullBookContent.Length - charIndex
        );
        string pageText = fullBookContent.Substring(charIndex, length);
        charIndex += length;

        return FormatPageText(pageText);
    }

    private string FormatPageText(string pageText)
    {
        if (string.IsNullOrEmpty(pageText))
        {
            return string.Empty;
        }

        int lineLength = Mathf.Max(1, charsPerLine);
        StringBuilder pageBuilder = new StringBuilder();

        for (int i = 0; i < pageText.Length; i += lineLength)
        {
            int length = Mathf.Min(lineLength, pageText.Length - i);
            pageBuilder.AppendLine(pageText.Substring(i, length));
        }

        return pageBuilder
            .ToString()
            .TrimEnd(' ', '\r', '\n');
    }

    private string ExtractContent(EpubBook epubBook)
    {
        StringBuilder fullContent = new StringBuilder();

        foreach (
            EpubLocalTextContentFile textContentFile
            in epubBook.ReadingOrder
        )
        {
            string content = ExtractPlainText(textContentFile);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (fullContent.Length > 0)
            {
                fullContent.AppendLine();
                fullContent.AppendLine();
            }

            fullContent.Append(content.Trim());
        }

        string normalizedContent = fullContent
            .ToString()
            .Normalize(NormalizationForm.FormC);

        normalizedContent = Regex.Replace(
            normalizedContent,
            @"[ \t\u00A0]+",
            " "
        );
        normalizedContent = Regex.Replace(
            normalizedContent,
            @" *\r?\n *",
            "\n"
        );
        normalizedContent = Regex.Replace(
            normalizedContent,
            @"\n{3,}",
            "\n\n"
        );

        return normalizedContent.Trim();
    }

    private string ExtractPlainText(
        EpubLocalTextContentFile textContentFile
    )
    {
        HtmlDocument htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(textContentFile.Content);

        HtmlNode body =
            htmlDocument.DocumentNode.SelectSingleNode("//body")
            ?? htmlDocument.DocumentNode;

        HtmlNodeCollection textNodes = body.SelectNodes(
            ".//text()["
            + "not(ancestor::script) and "
            + "not(ancestor::style) and "
            + "not(ancestor::rt) and "
            + "not(ancestor::rp)"
            + "]"
        );

        if (textNodes == null)
        {
            return string.Empty;
        }

        StringBuilder content = new StringBuilder();

        foreach (HtmlNode node in textNodes)
        {
            string text = HtmlEntity.DeEntitize(node.InnerText);
            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            content.Append(text);

            string parentName = node.ParentNode?.Name?.ToLowerInvariant();
            if (
                parentName == "p"
                || parentName == "div"
                || parentName == "li"
                || parentName == "h1"
                || parentName == "h2"
                || parentName == "h3"
                || parentName == "h4"
                || parentName == "h5"
                || parentName == "h6"
            )
            {
                content.AppendLine();
            }
            // Inline text is concatenated without adding an ASCII space.
            // This prevents unwanted spaces around Japanese ruby elements.
        }

        return content.ToString();
    }

    private static void PlaySound(AudioSource source)
    {
        if (source != null && source.clip != null)
        {
            source.Play();
        }
    }
}
