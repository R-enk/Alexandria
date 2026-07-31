using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// TextMeshProを使って本文のページ分割位置を測定し、
/// 各ページを専用Texture2Dへ焼き込んでEndlessBook用Materialを生成します。
/// </summary>
public sealed class PageRenderer : MonoBehaviour
{
    [Header("Left Page")]

    [SerializeField]
    private Camera leftPageCamera;

    [SerializeField]
    private TextMeshPro leftPageTextComponent;

    [SerializeField]
    private RenderTexture leftPageRenderTexture;

    [Header("Right Page")]

    [SerializeField]
    private Camera rightPageCamera;

    [SerializeField]
    private TextMeshPro rightPageTextComponent;

    [SerializeField]
    private RenderTexture rightPageRenderTexture;

    [Header("Material")]

    [Tooltip(
        "ページ用Materialのテンプレートです。" +
        "未設定の場合はUnlit/Textureを使用します。"
    )]
    [SerializeField]
    private Material pageMaterialTemplate;

    private readonly List<Material> generatedMaterials =
        new List<Material>();

    private readonly List<Texture2D> generatedTextures =
        new List<Texture2D>();

    private Shader fallbackShader;
    private bool resourcesReleased;

    private void Awake()
    {
        if (ValidateReferences(logErrors: true))
        {
            ConfigureTextComponents();
        }
    }

    private void OnDestroy()
    {
        ReleaseGeneratedResources();
    }

    public bool ValidateReferences(bool logErrors)
    {
        bool isValid = true;

        isValid &= ValidateReference(
            leftPageCamera,
            "Left Page Camera",
            logErrors
        );

        isValid &= ValidateReference(
            leftPageTextComponent,
            "Left Page Text Component",
            logErrors
        );

        isValid &= ValidateReference(
            leftPageRenderTexture,
            "Left Page Render Texture",
            logErrors
        );

        isValid &= ValidateReference(
            rightPageCamera,
            "Right Page Camera",
            logErrors
        );

        isValid &= ValidateReference(
            rightPageTextComponent,
            "Right Page Text Component",
            logErrors
        );

        isValid &= ValidateReference(
            rightPageRenderTexture,
            "Right Page Render Texture",
            logErrors
        );

        if (leftPageTextComponent != null)
        {
            isValid &= ValidateTextArea(
                leftPageTextComponent,
                "Left Page Text Component",
                logErrors
            );
        }

        if (rightPageTextComponent != null)
        {
            isValid &= ValidateTextArea(
                rightPageTextComponent,
                "Right Page Text Component",
                logErrors
            );
        }

        if (pageMaterialTemplate == null)
        {
            fallbackShader =
                Shader.Find("Unlit/Texture");

            if (fallbackShader == null)
            {
                if (logErrors)
                {
                    Debug.LogError(
                        "Page Material Templateが未設定で、" +
                        "Unlit/Texture Shaderも見つかりません。",
                        this
                    );
                }

                isValid = false;
            }
        }

        return isValid;
    }

    /// <summary>
    /// 左右ページのTextMeshPro表示領域を交互に測定し、
    /// 実際に収まる位置で本文をページへ分割します。
    /// </summary>
    public List<string> PaginateText(
        string sourceText
    )
    {
        if (!ValidateReferences(logErrors: true))
        {
            throw new InvalidOperationException(
                "PageRendererの必須参照が設定されていません。"
            );
        }

        ConfigureTextComponents();

        string previousLeftText =
            leftPageTextComponent.text;

        string previousRightText =
            rightPageTextComponent.text;

        try
        {
            return BookPaginator.Paginate(
                sourceText,
                leftPageTextComponent,
                rightPageTextComponent
            );
        }
        finally
        {
            RestoreTextComponent(
                leftPageTextComponent,
                previousLeftText
            );

            RestoreTextComponent(
                rightPageTextComponent,
                previousRightText
            );
        }
    }

    public Material RenderLeftPageToMaterial(
        string text,
        string resourceName
    )
    {
        return RenderPageToMaterial(
            text,
            leftPageCamera,
            leftPageTextComponent,
            leftPageRenderTexture,
            resourceName
        );
    }

    public Material RenderRightPageToMaterial(
        string text,
        string resourceName
    )
    {
        return RenderPageToMaterial(
            text,
            rightPageCamera,
            rightPageTextComponent,
            rightPageRenderTexture,
            resourceName
        );
    }

    // 既存コードとの互換用オーバーロードです。
    public Material RenderLeftPageToMaterial(
        string text
    )
    {
        return RenderLeftPageToMaterial(
            text,
            "LeftPage"
        );
    }

    public Material RenderRightPageToMaterial(
        string text
    )
    {
        return RenderRightPageToMaterial(
            text,
            "RightPage"
        );
    }

    public void ReleaseGeneratedResources()
    {
        if (resourcesReleased)
        {
            return;
        }

        resourcesReleased = true;

        foreach (
            Material material
            in generatedMaterials
        )
        {
            DestroyRuntimeObject(material);
        }

        foreach (
            Texture2D texture
            in generatedTextures
        )
        {
            DestroyRuntimeObject(texture);
        }

        generatedMaterials.Clear();
        generatedTextures.Clear();
    }

    private Material RenderPageToMaterial(
        string text,
        Camera renderCamera,
        TextMeshPro textComponent,
        RenderTexture renderTexture,
        string resourceName
    )
    {
        if (!ValidateReferences(logErrors: true))
        {
            throw new InvalidOperationException(
                "PageRendererの必須参照が設定されていません。"
            );
        }

        resourcesReleased = false;

        ConfigureTextComponent(
            textComponent
        );

        string safeName =
            string.IsNullOrWhiteSpace(
                resourceName
            )
                ? "Page"
                : resourceName;

        textComponent.text =
            text ?? string.Empty;

        textComponent.ForceMeshUpdate(
            ignoreActiveState: true,
            forceTextReparsing: true
        );

        if (
            textComponent.isTextOverflowing ||
            textComponent.isTextTruncated
        )
        {
            Debug.LogWarning(
                safeName +
                "の本文がTextMeshPro表示領域を超えています。" +
                "フォントやTextMeshProの領域が分割時から変更されていないか確認してください。",
                this
            );
        }

        if (!renderTexture.IsCreated())
        {
            renderTexture.Create();
        }

        Texture2D pageTexture = null;
        Material pageMaterial = null;

        RenderTexture previousActiveTexture =
            RenderTexture.active;

        RenderTexture previousCameraTarget =
            renderCamera.targetTexture;

        try
        {
            renderCamera.targetTexture =
                renderTexture;

            renderCamera.Render();

            RenderTexture.active =
                renderTexture;

            pageTexture =
                new Texture2D(
                    renderTexture.width,
                    renderTexture.height,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: false
                )
                {
                    name =
                        safeName + "_Texture",

                    filterMode =
                        FilterMode.Bilinear,

                    wrapMode =
                        TextureWrapMode.Clamp
                };

            pageTexture.ReadPixels(
                new Rect(
                    0,
                    0,
                    renderTexture.width,
                    renderTexture.height
                ),
                0,
                0,
                recalculateMipMaps: false
            );

            pageTexture.Apply(
                updateMipmaps: false,
                makeNoLongerReadable: true
            );

            Material sourceMaterial =
                pageMaterialTemplate;

            if (sourceMaterial != null)
            {
                pageMaterial =
                    new Material(sourceMaterial);
            }
            else
            {
                if (fallbackShader == null)
                {
                    fallbackShader =
                        Shader.Find(
                            "Unlit/Texture"
                        );
                }

                if (fallbackShader == null)
                {
                    throw new InvalidOperationException(
                        "ページ用Shaderを取得できませんでした。"
                    );
                }

                pageMaterial =
                    new Material(
                        fallbackShader
                    );
            }

            pageMaterial.name =
                safeName + "_Material";

            pageMaterial.mainTexture =
                pageTexture;

            generatedTextures.Add(
                pageTexture
            );

            generatedMaterials.Add(
                pageMaterial
            );

            return pageMaterial;
        }
        catch
        {
            if (
                pageMaterial != null &&
                !generatedMaterials.Contains(
                    pageMaterial
                )
            )
            {
                DestroyRuntimeObject(
                    pageMaterial
                );
            }

            if (
                pageTexture != null &&
                !generatedTextures.Contains(
                    pageTexture
                )
            )
            {
                DestroyRuntimeObject(
                    pageTexture
                );
            }

            throw;
        }
        finally
        {
            renderCamera.targetTexture =
                previousCameraTarget;

            RenderTexture.active =
                previousActiveTexture;
        }
    }

    private void ConfigureTextComponents()
    {
        ConfigureTextComponent(
            leftPageTextComponent
        );

        ConfigureTextComponent(
            rightPageTextComponent
        );
    }

    /// <summary>
    /// 測定時と描画時で同じレイアウト条件を使用します。
    /// Auto Sizeを無効化し、TextMeshPro自身の自動折り返しを有効にします。
    /// </summary>
    private static void ConfigureTextComponent(
        TMP_Text textComponent
    )
    {
        if (textComponent == null)
        {
            return;
        }

        textComponent.enableAutoSizing =
            false;

        textComponent.enableWordWrapping =
            true;

        textComponent.overflowMode =
            TextOverflowModes.Truncate;

        textComponent.richText =
            false;
    }

    private static void RestoreTextComponent(
        TMP_Text textComponent,
        string previousText
    )
    {
        if (textComponent == null)
        {
            return;
        }

        textComponent.text =
            previousText ?? string.Empty;

        textComponent.ForceMeshUpdate(
            ignoreActiveState: true,
            forceTextReparsing: true
        );
    }

    private bool ValidateTextArea(
        TMP_Text textComponent,
        string fieldName,
        bool logErrors
    )
    {
        Rect textRect =
            textComponent.rectTransform.rect;

        if (
            textRect.width > 0.01f &&
            textRect.height > 0.01f
        )
        {
            return true;
        }

        if (logErrors)
        {
            Debug.LogError(
                "PageRendererの" +
                fieldName +
                "の表示領域の幅または高さが0です。",
                this
            );
        }

        return false;
    }

    private bool ValidateReference(
        UnityEngine.Object reference,
        string fieldName,
        bool logErrors
    )
    {
        if (reference != null)
        {
            return true;
        }

        if (logErrors)
        {
            Debug.LogError(
                "PageRendererの" +
                fieldName +
                "が設定されていません。",
                this
            );
        }

        return false;
    }

    private static void DestroyRuntimeObject(
        UnityEngine.Object target
    )
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(
                target
            );
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(
                target
            );
        }
    }
}
