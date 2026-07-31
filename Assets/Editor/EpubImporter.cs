using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// .epubファイルをEpubAssetとしてUnityへ取り込むインポーターです。
/// </summary>
[ScriptedImporter(1, "epub")]
public sealed class EpubImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext context)
    {
        if (string.IsNullOrWhiteSpace(context.assetPath))
        {
            Debug.LogError("EPUBのアセットパスが空です。");
            return;
        }

        string absolutePath = Path.GetFullPath(context.assetPath);

        if (!File.Exists(absolutePath))
        {
            Debug.LogError(
                "EPUBファイルが見つかりません: " +
                absolutePath
            );
            return;
        }

        try
        {
            byte[] epubBytes = File.ReadAllBytes(absolutePath);

            if (epubBytes == null || epubBytes.Length == 0)
            {
                Debug.LogError(
                    "EPUBファイルが空です: " +
                    absolutePath
                );
                return;
            }

            EpubAsset epubAsset =
                ScriptableObject.CreateInstance<EpubAsset>();

            epubAsset.name =
                Path.GetFileNameWithoutExtension(context.assetPath);

            epubAsset.Initialize(
                epubBytes,
                Path.GetFileName(context.assetPath)
            );

            context.AddObjectToAsset(
                "EpubAsset",
                epubAsset
            );

            context.SetMainObject(epubAsset);
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                "EPUBのインポートに失敗しました: " +
                exception.GetType().Name +
                ": " +
                exception.Message
            );
        }
    }
}