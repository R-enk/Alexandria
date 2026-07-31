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
    public EndlessBook book;
    public PageRenderer pageRenderer;
    public float openCloseTime = 1.0f;

    public AudioClip bookOpenClip;
    public AudioClip bookCloseClip;
    public AudioClip pageTurnClip;

    private int charIndex = 0;
    private string fullBookContent;
    private readonly List<int> pageStartIndices = new List<int>();
    private int currentLeftPageIndex = 0;
    private bool isEpubLoaded = false;

    private AudioSource bookOpenSound;
    private AudioSource bookCloseSound;
    private AudioSource pageTurnSound;

    private float inputDelay = 0.5f;
    private float lastInputTime = 0f;

    private void Start()
    {
        LoadAudioClips();
    }

    private void LoadAudioClips()
    {
        bookOpenClip = Resources.Load<AudioClip>("Sounds/BookOpen");
        bookCloseClip = Resources.Load<AudioClip>("Sounds/BookClose");
        pageTurnClip = Resources.Load<AudioClip>("Sounds/PageTurn");

        bookOpenSound = gameObject.AddComponent<AudioSource>();
        bookOpenSound.clip = bookOpenClip;
        bookOpenSound.playOnAwake = false;

        bookCloseSound = gameObject.AddComponent<AudioSource>();
        bookCloseSound.clip = bookCloseClip;
        bookCloseSound.playOnAwake = false;

        pageTurnSound = gameObject.AddComponent<AudioSource>();
        pageTurnSound.clip = pageTurnClip;
        pageTurnSound.playOnAwake = false;
    }

    public void LoadEpubFromPath(string filePath)
    {
        isEpubLoaded = false;
        fullBookContent = null;
        charIndex = 0;
        currentLeftPageIndex = 0;
        pageStartIndices.Clear();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            Debug.LogError("EPUB path is empty.");
            return;
        }

        if (!File.Exists(filePath))
        {
            Debug.LogError("EPUB file was not found: " + filePath);
            return;
        }

        try
        {
            Debug.Log("Loading EPUB from: " + filePath);

            byte[] epubBytes = File.ReadAllBytes(filePath);
            if (epubBytes.Length == 0)
            {
                Debug.LogError("EPUB file is empty: " + filePath);
                return;
            }

            HandleEpubData(epubBytes);

            if (string.IsNullOrWhiteSpace(fullBookContent))
            {
                Debug.LogError("No readable text was extracted from the EPUB.");
                return;
            }

            isEpubLoaded = true;

            Debug.Log("EPUB loaded successfully.");
            Debug.Log("EPUB size: " + epubBytes.Length);
            Debug.Log("Extracted character count: " + fullBookContent.Length);
        }
        catch (System.Exception ex)
        {
            isEpubLoaded = false;
            fullBookContent = null;
            Debug.LogError(
                "Failed to load or parse EPUB: "
                + ex.GetType().Name
                + ": "
                + ex.Message
            );
        }
    }

    private void HandleEpubData(byte[] epubData)
    {
        using (MemoryStream epubStream = new MemoryStream(epubData))
        {
            EpubBook epubBook = EpubReader.ReadBook(epubStream);
            fullBookContent = ExtractContent(epubBook);

            string preview = fullBookContent.Length >= 20
                ? fullBookContent.Substring(0, 20)
                : fullBookContent;
            Debug.Log("Book content: " + preview);
        }
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
        Debug.Log("Turn left");
        TurnPageLeft();
    }

    private void OpenBookAndAddFirstPages()
    {
        book.SetState(
            EndlessBook.StateEnum.OpenMiddle,
            openCloseTime,
            OnBookStateChanged
        );
        pageTurnSound.Play();

        if (pageStartIndices.Count == 0)
        {
            // The first pages are added when the user turns right after opening.
            return;
        }

        string leftPageText = RenderPageBasedOnIndex(currentLeftPageIndex);
        string rightPageText = RenderPageBasedOnIndex(currentLeftPageIndex + 1);

        Material leftPageMaterial = pageRenderer.RenderLeftPageToMaterial(leftPageText);
        Material rightPageMaterial = pageRenderer.RenderRightPageToMaterial(rightPageText);

        book.UpdatePageDataMaterial(book.CurrentLeftPageNumber, leftPageMaterial);
        book.UpdatePageDataMaterial(book.CurrentRightPageNumber, rightPageMaterial);
        currentLeftPageIndex += 2;
    }

    private void TurnPageRight()
    {
        if (currentLeftPageIndex < pageStartIndices.Count - 1)
        {
            currentLeftPageIndex += 2;
            Debug.Log("After page turn " + currentLeftPageIndex);

            UpdatePageMaterials();
            pageTurnSound.Play();

            book.TurnToPage(
                book.CurrentLeftPageNumber + 2,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                0.5f
            );
        }
        else if (IsAtLastPage())
        {
            const int charsPerLine = 38;
            const int maxLinesPerPage = 16;
            const int charsPerPage = charsPerLine * maxLinesPerPage;

            if (
                charIndex >= fullBookContent.Length
                || charIndex + charsPerPage >= fullBookContent.Length
            )
            {
                Debug.Log("Last page and end of content, close book");
                bookCloseSound.Play();
                book.SetState(
                    EndlessBook.StateEnum.ClosedFront,
                    openCloseTime,
                    OnBookStateChanged
                );
                currentLeftPageIndex = 0;
                book.SetPageNumber(1);
            }
            else
            {
                Debug.Log("Last page but not end of content, add new page");

                currentLeftPageIndex += 2;
                Debug.Log("After page turn " + currentLeftPageIndex);

                AddNewPage();
                pageTurnSound.Play();
                book.TurnToPage(
                    book.CurrentLeftPageNumber + 2,
                    EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                    0.5f
                );
            }
        }
        else
        {
            Debug.Log("Closing book");

            bookCloseSound.Play();
            book.SetState(
                EndlessBook.StateEnum.ClosedFront,
                openCloseTime,
                OnBookStateChanged
            );
            currentLeftPageIndex = 0;
            book.SetPageNumber(1);
        }
    }

    private void OnBookStateChanged(
        EndlessBook.StateEnum fromState,
        EndlessBook.StateEnum toState,
        int pageNumber
    )
    {
        Debug.Log("Book state changed from " + fromState + " to " + toState);
    }

    private void TurnPageLeft()
    {
        if (currentLeftPageIndex > 0)
        {
            currentLeftPageIndex -= 2;
            UpdatePageMaterials();
            Debug.Log("After page turn " + currentLeftPageIndex);
            book.TurnToPage(
                book.CurrentLeftPageNumber - 2,
                EndlessBook.PageTurnTimeTypeEnum.TimePerPage,
                0.3f
            );
            pageTurnSound.Play();
        }
        else if (book.CurrentState != EndlessBook.StateEnum.ClosedFront)
        {
            book.SetState(
                EndlessBook.StateEnum.ClosedFront,
                openCloseTime,
                OnBookStateChanged
            );
            bookCloseSound.Play();
        }
    }

    private void AddNewPage()
    {
        string leftPageText = GetNextPageText();
        string rightPageText = GetNextPageText();

        if (!string.IsNullOrEmpty(leftPageText) || !string.IsNullOrEmpty(rightPageText))
        {
            Material leftPageMaterial = pageRenderer.RenderLeftPageToMaterial(leftPageText);
            Material rightPageMaterial = pageRenderer.RenderRightPageToMaterial(rightPageText);

            book.AddPageData(leftPageMaterial);
            book.AddPageData(rightPageMaterial);
        }
    }

    private void UpdatePageMaterials()
    {
        string leftPageText = RenderPageBasedOnIndex(currentLeftPageIndex - 2);
        string rightPageText = RenderPageBasedOnIndex(currentLeftPageIndex - 1);

        Material leftPageMaterial = pageRenderer.RenderLeftPageToMaterial(leftPageText);
        Material rightPageMaterial = pageRenderer.RenderRightPageToMaterial(rightPageText);

        book.UpdatePageDataMaterial(book.CurrentLeftPageNumber, leftPageMaterial);
        book.UpdatePageDataMaterial(book.CurrentRightPageNumber, rightPageMaterial);
    }

    private bool IsAtLastPage()
    {
        return book.CurrentRightPageNumber >= book.LastPageNumber;
    }

    private string RenderPageBasedOnIndex(int pageIndex)
    {
        const int charsPerLine = 38;
        const int paragraphBreakFrequency = 6;

        if (pageIndex < 0 || pageIndex >= pageStartIndices.Count)
        {
            book.SetState(
                EndlessBook.StateEnum.ClosedFront,
                openCloseTime,
                OnBookStateChanged
            );
            return "";
        }

        int startIndex = pageStartIndices[pageIndex];
        int endIndex = pageIndex < pageStartIndices.Count - 1
            ? pageStartIndices[pageIndex + 1]
            : fullBookContent.Length;

        string pageText = fullBookContent.Substring(startIndex, endIndex - startIndex);
        StringBuilder pageBuilder = new StringBuilder();
        int lineCounter = 0;

        for (int i = 0; i < pageText.Length; i += charsPerLine)
        {
            int length = i + charsPerLine > pageText.Length
                ? pageText.Length - i
                : charsPerLine;
            pageBuilder.AppendLine(pageText.Substring(i, length));
            lineCounter++;

            if (lineCounter >= paragraphBreakFrequency && Random.Range(0, 2) > 0)
            {
                pageBuilder.AppendLine("\n");
                lineCounter = 0;
            }
        }

        return pageBuilder.ToString().TrimEnd(' ', '\n');
    }

    private string GetNextPageText()
    {
        const int charsPerLine = 38;
        const int maxLinesPerPage = 16;
        const int charsPerPage = charsPerLine * maxLinesPerPage;
        const int paragraphBreakFrequency = 6;

        if (charIndex + charsPerPage > fullBookContent.Length)
        {
            return null;
        }

        pageStartIndices.Add(charIndex);

        string pageText = fullBookContent.Substring(
            charIndex,
            Mathf.Min(charsPerPage, fullBookContent.Length - charIndex)
        );
        StringBuilder pageBuilder = new StringBuilder();
        int lineCounter = 0;

        for (int i = 0; i < pageText.Length; i += charsPerLine)
        {
            int length = charsPerLine;
            if (i + length > pageText.Length)
            {
                length = pageText.Length - i;
            }

            pageBuilder.AppendLine(pageText.Substring(i, length));
            lineCounter++;

            if (lineCounter >= paragraphBreakFrequency && Random.Range(0, 2) > 0)
            {
                pageBuilder.AppendLine("\n");
                lineCounter = 0;
            }
        }

        charIndex += pageText.Length;
        return pageBuilder.ToString().TrimEnd(' ', '\n');
    }

    private string ExtractContent(EpubBook epubBook)
    {
        StringBuilder fullContent = new StringBuilder();

        foreach (EpubLocalTextContentFile textContentFile in epubBook.ReadingOrder)
        {
            string content = ExtractPlainText(textContentFile);
            string normalizedContent = Regex.Replace(content, @"\s+", " ");
            fullContent.Append(normalizedContent);
        }

        return fullContent.ToString();
    }

    private string ExtractPlainText(EpubLocalTextContentFile textContentFile)
    {
        HtmlDocument htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(textContentFile.Content);
        StringBuilder sb = new StringBuilder();

        HtmlNodeCollection textNodes = htmlDocument.DocumentNode.SelectNodes("//text()");
        if (textNodes == null)
        {
            return "";
        }

        foreach (HtmlNode node in textNodes)
        {
            string text = node.InnerText.Trim();
            text = Regex.Replace(text, @"\r\n?|\n", " ");
            sb.Append(text + " ");
        }

        return Regex.Replace(sb.ToString(), @"[ ]{2,}", "\n\n");
    }
}
