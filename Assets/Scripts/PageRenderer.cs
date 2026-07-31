using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class PageRenderer : MonoBehaviour
{
    [Header("Page Cameras")]

    [SerializeField]
    private Camera leftPageCamera;

    [SerializeField]
    private Camera rightPageCamera;

    [Header("Page Text")]

    [SerializeField]
    private TextMeshPro leftPageTextComponent;

    [SerializeField]
    private TextMeshPro rightPageTextComponent;

    [Header("Render Textures")]

    [SerializeField]
    private RenderTexture leftPageRenderTexture;

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
        if (!ValidateReferences(logErrors: true))
        {
            enabled = false;
            return;
        }

        PrepareTextComponent(leftPageTextComponent);
        PrepareTextComponent(rightPageTextComponent);
    }

    private void OnDestroy()
    {
        ReleaseGeneratedResources();
    }

    /// <summary>
    /// 左右ページのTextMeshProを交互に測定し、
    /// 実際の表示領域へ収まる位置で本文を分割します。
    /// </summary>
    public List<string> PaginateText(string fullText)
    {
        if (!ValidateReferences(logErrors: true))
        {
            throw new InvalidOperationException(
                "PageRendererの必須参照が設定されていません。"
            );
        }

        PrepareTextComponent(leftPageTextComponent);
        PrepareTextComponent(rightPageTextComponent);

        return BookPaginator.Paginate(
            fullText,
            leftPageTextComponent,
            rightPageTextComponent
        );
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
    public Material RenderLeftPageToMaterial(string text)
    {
        return RenderLeftPageToMaterial(
            text,
            "LeftPage"
        );
    }

    public Material RenderRightPageToMaterial(string text)
    {
        return RenderRightPageToMaterial(
            text,
            "RightPage"
        );
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
            rightPageCamera,
            "Right Page Camera",
            logErrors
        );

        isValid &= ValidateReference(
            leftPageTextComponent,
            "Left Page Text Component",
            logErrors
        );

        isValid &= ValidateReference(
            rightPageTextComponent,
            "Right Page Text Component",
            logErrors
        );

        isValid &= ValidateReference(
            leftPageRenderTexture,
            "Left Page Render Texture",
            logErrors
        );

        isValid &= ValidateReference(
            rightPageRenderTexture,
            "Right Page Render Texture",
            logErrors
        );

        if (
            leftPageRenderTexture != null &&
            (
                leftPageRenderTexture.width <= 0 ||
                leftPageRenderTexture.height <= 0
            )
        )
        {
            if (logErrors)
            {
                Debug.LogError(
                    "Left Page Render Textureのサイズが不正です。",
                    this
                );
            }

            isValid = false;
        }

        if (
            rightPageRenderTexture != null &&
            (
                rightPageRenderTexture.width <= 0 ||
                rightPageRenderTexture.height <= 0
            )
        )
        {
            if (logErrors)
            {
                Debug.LogError(
                    "Right Page Render Textureのサイズが不正です。",
                    this
                );
            }

            isValid = false;
        }

        return isValid;
    }

    public void ReleaseGeneratedResources()
    {
        if (resourcesReleased)
        {
            return;
        }

        resourcesReleased = true;

        foreach (Material material in generatedMaterials)
        {
            DestroyRuntimeObject(material);
        }

        foreach (Texture2D texture in generatedTextures)
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

        string safeName =
            string.IsNullOrWhiteSpace(resourceName)
                ? "Page"
                : resourceName;

        PrepareTextComponent(textComponent);

        textComponent.text =
            text ?? string.Empty;

        textComponent.ForceMeshUpdate(
            ignoreActiveState: true,
            forceTextReparsing: true
        );

        if (textComponent.isTextOverflowing)
        {
            Debug.LogWarning(
                safeName +
                "の文章がTextMeshProの表示領域を超えています。" +
                "ページ分割後にTextMeshProのサイズ、フォント、" +
                "Font Size、Marginを変更していないか確認してください。",
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

            pageTexture = new Texture2D(
                renderTexture.width,
                renderTexture.height,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: false
            )
            {
                name = safeName + "_Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
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

            if (pageMaterialTemplate != null)
            {
                pageMaterial =
                    new Material(
                        pageMaterialTemplate
                    );
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
                        "ページ用Shaderを取得できませんでした。" +
                        "Page Material Templateを設定してください。"
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

            pageMaterial.hideFlags =
                HideFlags.DontSave;

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

    /// <summary>
    /// 測定時と描画時のTextMeshPro設定を一致させます。
    /// Auto Sizeを無効にし、現在のFont Sizeを固定して測定します。
    /// </summary>
    private static void PrepareTextComponent(
        TMP_Text textComponent
    )
    {
        if (textComponent == null)
        {
            return;
        }

        textComponent.richText = false;
        textComponent.enableAutoSizing = false;
        textComponent.enableWordWrapping = true;
        textComponent.overflowMode =
            TextOverflowModes.Truncate;

        textComponent.maxVisibleCharacters =
            int.MaxValue;

        textComponent.maxVisibleLines =
            int.MaxValue;

        textComponent.maxVisibleWords =
            int.MaxValue;

        textComponent.rectTransform
            .ForceUpdateRectTransforms();
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
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}