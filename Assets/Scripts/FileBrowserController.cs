using System.Collections;
using System.IO;
using SimpleFileBrowser;
using UnityEngine;

public class FileBrowserController : MonoBehaviour
{
    public GameObject buttonCanvas;
    public GameObject fileBrowserCanvas;
    public GameObject book;

    public void OpenFileBrowser()
    {
        if (buttonCanvas != null)
        {
            buttonCanvas.SetActive(false);
        }

        if (fileBrowserCanvas != null)
        {
            fileBrowserCanvas.SetActive(true);
        }

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
            "Load Epub",
            "Load"
        );

        if (FileBrowser.Success)
        {
            string filePath = FileBrowser.Result[0];
            CopyFileToStreamingAssets(filePath);
        }
        else
        {
            Debug.Log("User canceled file browser");

            if (buttonCanvas != null)
            {
                buttonCanvas.SetActive(true);
            }

            if (fileBrowserCanvas != null)
            {
                fileBrowserCanvas.SetActive(false);
            }
        }
    }

    private void CopyFileToStreamingAssets(string filePath)
    {
        string destinationPath = Path.Combine(
            Application.streamingAssetsPath,
            Path.GetFileName(filePath)
        );

        Debug.Log("Source path: " + filePath);
        Debug.Log("Destination path: " + destinationPath);

        try
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
            {
                Directory.CreateDirectory(Application.streamingAssetsPath);
            }

            string sourceFullPath = Path.GetFullPath(filePath);
            string destinationFullPath = Path.GetFullPath(destinationPath);

            if (!string.Equals(
                    sourceFullPath,
                    destinationFullPath,
                    System.StringComparison.OrdinalIgnoreCase
                ))
            {
                File.Copy(sourceFullPath, destinationFullPath, true);
                Debug.Log(
                    "File copied to StreamingAssets: "
                    + destinationFullPath
                );
            }
            else
            {
                Debug.Log("The selected EPUB is already in StreamingAssets.");
            }

            if (book == null)
            {
                Debug.LogError(
                    "Book GameObject is not assigned in FileBrowserController."
                );
                RestoreFileBrowserUi();
                return;
            }

            book.SetActive(true);

            BookController bookController = book.GetComponent<BookController>();
            if (bookController == null)
            {
                bookController = book.GetComponentInChildren<BookController>();
            }

            if (bookController == null)
            {
                Debug.LogError(
                    "BookController was not found on the Book GameObject."
                );
                RestoreFileBrowserUi();
                return;
            }

            bookController.LoadEpubFromPath(destinationFullPath);

            if (fileBrowserCanvas != null)
            {
                fileBrowserCanvas.SetActive(false);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError(
                "Error preparing EPUB: "
                + ex.GetType().Name
                + ": "
                + ex.Message
            );
            RestoreFileBrowserUi();
        }
    }

    private void RestoreFileBrowserUi()
    {
        if (buttonCanvas != null)
        {
            buttonCanvas.SetActive(true);
        }

        if (fileBrowserCanvas != null)
        {
            fileBrowserCanvas.SetActive(false);
        }
    }
}
