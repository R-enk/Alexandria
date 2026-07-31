using System;
using System.Collections;
using System.IO;
using SimpleFileBrowser;
using UnityEngine;

public class FileBrowserController : MonoBehaviour
{
    [Header("UI")]
    public GameObject buttonCanvas;
    public GameObject fileBrowserCanvas;

    [Header("Book")]
    public GameObject book;
    public BookController bookController;

    public void OpenFileBrowser()
    {
        SetCanvasState(showButtonCanvas: false, showFileBrowserCanvas: true);
        StartCoroutine(ShowLoadDialogCoroutine());
    }

    private IEnumerator ShowLoadDialogCoroutine()
    {
        Debug.Log("Opening file browser");

        FileBrowser.SetDefaultFilter(".epub");

        yield return FileBrowser.WaitForLoadDialog(
            FileBrowser.PickMode.Files,
            false,
            null,
            null,
            "Load EPUB",
            "Load"
        );

        if (!FileBrowser.Success || FileBrowser.Result.Length == 0)
        {
            Debug.Log("User canceled file browser");
            ShowFileSelectionButton();
            yield break;
        }

        PrepareAndLoadEpub(FileBrowser.Result[0]);
    }

    private void PrepareAndLoadEpub(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            Debug.LogError("The selected EPUB path is empty.");
            ShowFileSelectionButton();
            return;
        }

        if (
            !string.Equals(
                Path.GetExtension(sourcePath),
                ".epub",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Debug.LogError("The selected file is not an EPUB: " + sourcePath);
            ShowFileSelectionButton();
            return;
        }

        try
        {
            string sourceFullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(sourceFullPath))
            {
                Debug.LogError(
                    "The selected EPUB was not found: " + sourceFullPath
                );
                ShowFileSelectionButton();
                return;
            }

            // Runtime-imported files belong in persistentDataPath.
            // StreamingAssets can be read-only, especially in Android builds.
            string importDirectory = Path.Combine(
                Application.persistentDataPath,
                "ImportedBooks"
            );
            Directory.CreateDirectory(importDirectory);

            string destinationFullPath = Path.GetFullPath(
                Path.Combine(
                    importDirectory,
                    Path.GetFileName(sourceFullPath)
                )
            );

            Debug.Log("Source path: " + sourceFullPath);
            Debug.Log("Destination path: " + destinationFullPath);

            if (
                !string.Equals(
                    sourceFullPath,
                    destinationFullPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                File.Copy(
                    sourceFullPath,
                    destinationFullPath,
                    true
                );
                Debug.Log(
                    "EPUB copied to the import folder: "
                    + destinationFullPath
                );
            }
            else
            {
                Debug.Log(
                    "The selected EPUB is already in the import folder."
                );
            }

            BookController controller = ResolveBookController();
            if (controller == null)
            {
                Debug.LogError(
                    "BookController was not found. "
                    + "Assign Book Controller or Book in the Inspector."
                );
                ShowFileSelectionButton();
                return;
            }

            bool loaded = controller.LoadEpubFromPath(
                destinationFullPath
            );

            if (loaded)
            {
                HideFileUi();
            }
            else
            {
                ShowFileSelectionButton();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError(
                "Error preparing EPUB: "
                + ex.GetType().Name
                + ": "
                + ex.Message
            );
            ShowFileSelectionButton();
        }
    }

    private BookController ResolveBookController()
    {
        if (bookController != null)
        {
            if (!bookController.gameObject.activeSelf)
            {
                bookController.gameObject.SetActive(true);
            }

            return bookController;
        }

        if (book == null)
        {
            return null;
        }

        if (!book.activeSelf)
        {
            book.SetActive(true);
        }

        bookController = book.GetComponent<BookController>();
        if (bookController == null)
        {
            bookController =
                book.GetComponentInChildren<BookController>(true);
        }

        return bookController;
    }

    private void ShowFileSelectionButton()
    {
        SetCanvasState(
            showButtonCanvas: true,
            showFileBrowserCanvas: false
        );
    }

    private void HideFileUi()
    {
        SetCanvasState(
            showButtonCanvas: false,
            showFileBrowserCanvas: false
        );
    }

    private void SetCanvasState(
        bool showButtonCanvas,
        bool showFileBrowserCanvas
    )
    {
        if (buttonCanvas != null)
        {
            buttonCanvas.SetActive(showButtonCanvas);
        }

        if (fileBrowserCanvas != null)
        {
            fileBrowserCanvas.SetActive(showFileBrowserCanvas);
        }
    }
}
