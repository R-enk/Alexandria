using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// TextMeshProの文字列をページ専用Texture2Dへ焼き込み、
/// EndlessBookで使用するMaterialを生成します。
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
        "ページ用Materialのテンプレートです。未設定の場合はUnlit/Textureを使用します。"
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
        ValidateReferences(logErrors: true);
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

        if (pageMaterialTemplate == null)
        {
            fallbackShader = Shader.Find("Unlit/Texture");

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
        return RenderLeftPageToMaterial(text, "LeftPage");
    }

    public Material RenderRightPageToMaterial(string text)
    {
        return RenderRightPageToMaterial(text, "RightPage");
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

        string safeName = string.IsNullOrWhiteSpace(resourceName)
            ? "Page"
            : resourceName;

        textComponent.text = text ?? string.Empty;
        textComponent.ForceMeshUpdate(
            ignoreActiveState: true,
            forceTextReparsing: true
        );

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
            renderCamera.targetTexture = renderTexture;
            renderCamera.Render();

            RenderTexture.active = renderTexture;

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
                wrapMode = TextureWrapMode.Clamp
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

            Material sourceMaterial = pageMaterialTemplate;

            if (sourceMaterial != null)
            {
                pageMaterial = new Material(sourceMaterial);
            }
            else
            {
                if (fallbackShader == null)
                {
                    fallbackShader = Shader.Find("Unlit/Texture");
                }

                if (fallbackShader == null)
                {
                    throw new InvalidOperationException(
                        "ページ用Shaderを取得できませんでした。"
                    );
                }

                pageMaterial = new Material(fallbackShader);
            }

            pageMaterial.name = safeName + "_Material";
            pageMaterial.mainTexture = pageTexture;

            generatedTextures.Add(pageTexture);
            generatedMaterials.Add(pageMaterial);

            return pageMaterial;
        }
        catch
        {
            if (
                pageMaterial != null &&
                !generatedMaterials.Contains(pageMaterial)
            )
            {
                DestroyRuntimeObject(pageMaterial);
            }

            if (
                pageTexture != null &&
                !generatedTextures.Contains(pageTexture)
            )
            {
                DestroyRuntimeObject(pageTexture);
            }

            throw;
        }
        finally
        {
            renderCamera.targetTexture = previousCameraTarget;
            RenderTexture.active = previousActiveTexture;
        }
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
                "PageRendererの" + fieldName +
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
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
