using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;

public class PageRenderer : MonoBehaviour
{
    [Header("Page cameras")]
    public Camera leftPageCamera;
    public Camera rightPageCamera;

    [Header("Page text")]
    public TextMeshPro leftPageTextComponent;
    public TextMeshPro rightPageTextComponent;

    [Header("Render textures")]
    public RenderTexture leftPageRenderTexture;
    public RenderTexture rightPageRenderTexture;

    [Header("Japanese font")]
    [Tooltip(
        "Optional. Assign a Japanese TMP font asset here for builds. "
        + "When empty, a dynamic Japanese font is created from OS fonts."
    )]
    public TMP_FontAsset japaneseFontAsset;

    [SerializeField] private bool createRuntimeJapaneseFont = true;
    [SerializeField] private int runtimeFontSize = 64;

    private TMP_FontAsset activeFontAsset;
    private TMP_FontAsset runtimeJapaneseFontAsset;
    private Font runtimeSourceFont;

    private static readonly string[] PreferredJapaneseFontNames =
    {
        "Yu Gothic UI",
        "Yu Gothic",
        "Meiryo",
        "MS Gothic",
        "Noto Sans CJK JP",
        "Noto Sans JP",
        "NotoSansCJK-Regular",
        "Droid Sans Japanese",
        "Arial Unicode MS",
        "sans-serif"
    };

    private void Awake()
    {
        InitializeFont();
    }

    private void OnDestroy()
    {
        if (runtimeJapaneseFontAsset != null)
        {
            Destroy(runtimeJapaneseFontAsset);
        }

        if (runtimeSourceFont != null)
        {
            Destroy(runtimeSourceFont);
        }
    }

    public Material RenderLeftPageToMaterial(string text)
    {
        PrepareTextComponent(leftPageTextComponent, text);

        if (leftPageCamera == null || leftPageRenderTexture == null)
        {
            Debug.LogError(
                "Left page camera or render texture is not assigned."
            );
            return null;
        }

        leftPageCamera.targetTexture = leftPageRenderTexture;
        leftPageCamera.Render();

        Material material = new Material(Shader.Find("Unlit/Texture"));
        material.mainTexture = leftPageRenderTexture;
        return material;
    }

    public Material RenderRightPageToMaterial(string text)
    {
        PrepareTextComponent(rightPageTextComponent, text);

        if (rightPageCamera == null || rightPageRenderTexture == null)
        {
            Debug.LogError(
                "Right page camera or render texture is not assigned."
            );
            return null;
        }

        rightPageCamera.targetTexture = rightPageRenderTexture;
        rightPageCamera.Render();

        Material material = new Material(Shader.Find("Unlit/Texture"));
        material.mainTexture = rightPageRenderTexture;
        return material;
    }

    private void InitializeFont()
    {
        if (japaneseFontAsset != null)
        {
            activeFontAsset = japaneseFontAsset;
            AssignFontToPageComponents(activeFontAsset);
            Debug.Log(
                "Using assigned Japanese TMP font asset: "
                + activeFontAsset.name
            );
            return;
        }

        if (!createRuntimeJapaneseFont)
        {
            Debug.LogWarning(
                "No Japanese TMP font asset is assigned. "
                + "Japanese characters may be displayed as squares."
            );
            return;
        }

        runtimeSourceFont = CreateJapaneseOsFont();
        if (runtimeSourceFont == null)
        {
            Debug.LogError(
                "A Japanese OS font could not be created. "
                + "Assign a Japanese TMP font asset to PageRenderer."
            );
            return;
        }

        runtimeJapaneseFontAsset =
            TMP_FontAsset.CreateFontAsset(runtimeSourceFont);

        if (runtimeJapaneseFontAsset == null)
        {
            Debug.LogError(
                "Failed to create a dynamic TMP font asset "
                + "from the selected OS font."
            );
            return;
        }

        runtimeJapaneseFontAsset.name =
            "Runtime Japanese Font Asset";
        runtimeJapaneseFontAsset.atlasPopulationMode =
            AtlasPopulationMode.Dynamic;
        runtimeJapaneseFontAsset.isMultiAtlasTexturesEnabled = true;

        activeFontAsset = runtimeJapaneseFontAsset;
        AssignFontToPageComponents(activeFontAsset);

        Debug.Log(
            "Created a dynamic Japanese TMP font from OS fonts."
        );
    }

    private Font CreateJapaneseOsFont()
    {
        string[] installedFontNames =
            Font.GetOSInstalledFontNames();

        List<string> fontCandidates = new List<string>();

        foreach (string preferredName in PreferredJapaneseFontNames)
        {
            AddUniqueFontName(fontCandidates, preferredName);
        }

        if (installedFontNames != null)
        {
            foreach (string installedName in installedFontNames)
            {
                AddUniqueFontName(fontCandidates, installedName);
            }
        }

        if (fontCandidates.Count == 0)
        {
            return null;
        }

        try
        {
            Font font = Font.CreateDynamicFontFromOSFont(
                fontCandidates.ToArray(),
                Mathf.Max(16, runtimeFontSize)
            );

            if (font == null)
            {
                return null;
            }

            const string sample = "日本語ごん狐";
            font.RequestCharactersInTexture(
                sample,
                Mathf.Max(16, runtimeFontSize),
                FontStyle.Normal
            );

            bool hasJapaneseGlyph = true;
            foreach (char character in sample)
            {
                if (!font.HasCharacter(character))
                {
                    hasJapaneseGlyph = false;
                    break;
                }
            }

            if (!hasJapaneseGlyph)
            {
                Debug.LogWarning(
                    "The generated OS font did not report every "
                    + "Japanese test glyph. Assigning a Japanese TMP "
                    + "font asset in the Inspector is recommended "
                    + "for device builds."
                );
            }

            return font;
        }
        catch (Exception ex)
        {
            Debug.LogError(
                "Failed to create a Japanese OS font: "
                + ex.GetType().Name
                + ": "
                + ex.Message
            );
            return null;
        }
    }

    private static void AddUniqueFontName(
        ICollection<string> destination,
        string fontName
    )
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return;
        }

        foreach (string existingName in destination)
        {
            if (
                string.Equals(
                    existingName,
                    fontName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }
        }

        destination.Add(fontName);
    }

    private void AssignFontToPageComponents(
        TMP_FontAsset fontAsset
    )
    {
        if (leftPageTextComponent != null)
        {
            leftPageTextComponent.font = fontAsset;
        }

        if (rightPageTextComponent != null)
        {
            rightPageTextComponent.font = fontAsset;
        }
    }

    private void PrepareTextComponent(
        TextMeshPro textComponent,
        string text
    )
    {
        if (textComponent == null)
        {
            Debug.LogError(
                "A page TextMeshPro component is not assigned."
            );
            return;
        }

        string safeText = text ?? string.Empty;

        if (activeFontAsset != null)
        {
            textComponent.font = activeFontAsset;

            if (
                activeFontAsset.atlasPopulationMode
                != AtlasPopulationMode.Static
                && !string.IsNullOrEmpty(safeText)
            )
            {
                activeFontAsset.TryAddCharacters(
                    safeText,
                    out string missingCharacters
                );

                if (!string.IsNullOrEmpty(missingCharacters))
                {
                    Debug.LogWarning(
                        "The active font does not contain these "
                        + "characters: "
                        + missingCharacters
                    );
                }
            }
        }

        textComponent.text = safeText;
        textComponent.ForceMeshUpdate(true, true);
    }
}
