using System;
using UnityEngine;

/// <summary>
/// Unity上で参照可能なEPUBアセットです。
/// EpubImporterによって.epubファイルの内容が格納されます。
/// </summary>
public sealed class EpubAsset : ScriptableObject
{
    [SerializeField, HideInInspector]
    private byte[] data = Array.Empty<byte>();

    [SerializeField, HideInInspector]
    private string originalFileName = string.Empty;

    /// <summary>
    /// EPUBファイルのバイナリデータ。
    /// </summary>
    public byte[] Data => data;

    /// <summary>
    /// 元のEPUBファイル名。
    /// </summary>
    public string OriginalFileName => originalFileName;

    /// <summary>
    /// EPUBファイルのデータサイズ。
    /// </summary>
    public int DataLength => data?.Length ?? 0;

#if UNITY_EDITOR
    /// <summary>
    /// EpubImporterからEPUBデータを設定します。
    /// </summary>
    public void Initialize(byte[] sourceData, string fileName)
    {
        data = sourceData ?? Array.Empty<byte>();
        originalFileName = fileName ?? string.Empty;
    }
#endif
}