using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// TextMeshProの実際の表示領域を使って本文をページへ分割します。
/// 固定文字数・固定行数では分割しません。
/// </summary>
public static class BookPaginator
{
    private const float BoundsTolerance = 0.5f;

    /// <summary>
    /// ページ先頭に置かない文字です。
    /// TextMeshPro内部の禁則処理に加え、ページ境界でも使用します。
    /// </summary>
    private static readonly HashSet<string>
        ProhibitedPageStartCharacters =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "、", "。", "，", "．", "・",
                "：", "；", "！", "？",
                "‼", "⁇", "⁈", "⁉",
                "）", "〕", "］", "｝",
                "〉", "》", "」", "』", "】",
                "〙", "〗", "〟", "’", "”",
                "｠", "»",
                "ぁ", "ぃ", "ぅ", "ぇ", "ぉ",
                "っ", "ゃ", "ゅ", "ょ", "ゎ",
                "ァ", "ィ", "ゥ", "ェ", "ォ",
                "ッ", "ャ", "ュ", "ョ", "ヮ",
                "ヵ", "ヶ",
                "ー", "〜", "～", "…", "‥",
                "ヽ", "ヾ", "ゝ", "ゞ", "々",
                "％", "%", "℃", "°"
            };

    /// <summary>
    /// ページ末尾に置かない文字です。
    /// </summary>
    private static readonly HashSet<string>
        ProhibitedPageEndCharacters =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "（", "〔", "［", "｛",
                "〈", "《", "「", "『", "【",
                "〘", "〖", "〝", "‘", "“",
                "｟", "«"
            };

    /// <summary>
    /// 左右ページのTextMeshProを交互に使い、
    /// 実際に収まる最大範囲で本文を分割します。
    /// </summary>
    public static List<string> Paginate(
        string sourceText,
        TMP_Text leftPageText,
        TMP_Text rightPageText
    )
    {
        if (leftPageText == null)
        {
            throw new ArgumentNullException(
                nameof(leftPageText)
            );
        }

        if (rightPageText == null)
        {
            throw new ArgumentNullException(
                nameof(rightPageText)
            );
        }

        List<string> pages =
            new List<string>();

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return pages;
        }

        ValidateTextContainer(
            leftPageText,
            "左ページ"
        );

        ValidateTextContainer(
            rightPageText,
            "右ページ"
        );

        string normalizedText =
            NormalizeText(sourceText);

        int[] elementBoundaries =
            BuildTextElementBoundaries(
                normalizedText
            );

        int elementCount =
            elementBoundaries.Length - 1;

        int currentElementIndex =
            SkipPageLeadingWhitespace(
                normalizedText,
                elementBoundaries,
                0,
                elementCount
            );

        string originalLeftText =
            leftPageText.text;

        string originalRightText =
            rightPageText.text;

        try
        {
            while (
                currentElementIndex <
                elementCount
            )
            {
                TMP_Text measurementText =
                    pages.Count % 2 == 0
                        ? leftPageText
                        : rightPageText;

                int endElementIndex =
                    FindLargestFittingEnd(
                        normalizedText,
                        elementBoundaries,
                        currentElementIndex,
                        elementCount,
                        measurementText
                    );

                if (
                    endElementIndex <=
                    currentElementIndex
                )
                {
                    throw new InvalidOperationException(
                        "TextMeshProの表示領域に1文字も収まりません。" +
                        "フォントサイズ、マージン、またはTextMeshProの" +
                        "RectTransformサイズを確認してください。"
                    );
                }

                endElementIndex =
                    AdjustPageBoundaryForJapaneseText(
                        normalizedText,
                        elementBoundaries,
                        currentElementIndex,
                        endElementIndex,
                        elementCount
                    );

                int startCharacterIndex =
                    elementBoundaries[
                        currentElementIndex
                    ];

                int endCharacterIndex =
                    elementBoundaries[
                        endElementIndex
                    ];

                string pageText =
                    normalizedText.Substring(
                        startCharacterIndex,
                        endCharacterIndex -
                        startCharacterIndex
                    );

                pageText =
                    TrimPageEnd(pageText);

                if (pageText.Length > 0)
                {
                    pages.Add(pageText);
                }

                currentElementIndex =
                    SkipPageLeadingWhitespace(
                        normalizedText,
                        elementBoundaries,
                        endElementIndex,
                        elementCount
                    );
            }
        }
        finally
        {
            RestoreTextComponent(
                leftPageText,
                originalLeftText
            );

            RestoreTextComponent(
                rightPageText,
                originalRightText
            );
        }

        return pages;
    }

    /// <summary>
    /// 候補文字列が収まるかを二分探索し、
    /// 収まる最大の書記素境界を返します。
    /// </summary>
    private static int FindLargestFittingEnd(
        string text,
        int[] elementBoundaries,
        int startElementIndex,
        int elementCount,
        TMP_Text measurementText
    )
    {
        int low = startElementIndex + 1;
        int high = elementCount;
        int best = startElementIndex;

        while (low <= high)
        {
            int middle =
                low + (high - low) / 2;

            bool fits =
                FitsInTextContainer(
                    text,
                    elementBoundaries,
                    startElementIndex,
                    middle,
                    measurementText
                );

            if (fits)
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return best;
    }

    private static bool FitsInTextContainer(
        string text,
        int[] elementBoundaries,
        int startElementIndex,
        int endElementIndex,
        TMP_Text measurementText
    )
    {
        int startCharacterIndex =
            elementBoundaries[
                startElementIndex
            ];

        int endCharacterIndex =
            elementBoundaries[
                endElementIndex
            ];

        string candidateText =
            text.Substring(
                startCharacterIndex,
                endCharacterIndex -
                startCharacterIndex
            );

        measurementText.text = candidateText;

        measurementText.ForceMeshUpdate(
            ignoreActiveState: true,
            forceTextReparsing: true
        );

        if (measurementText.isTextOverflowing)
        {
            return false;
        }

        Rect textRect =
            measurementText.rectTransform.rect;

        Vector4 margin =
            measurementText.margin;

        float availableWidth =
            Mathf.Max(
                0f,
                textRect.width -
                margin.x -
                margin.z
            );

        float availableHeight =
            Mathf.Max(
                0f,
                textRect.height -
                margin.y -
                margin.w
            );

        Vector2 renderedSize =
            measurementText.GetRenderedValues(
                onlyVisibleCharacters: false
            );

        return
            renderedSize.x <=
                availableWidth + BoundsTolerance &&
            renderedSize.y <=
                availableHeight + BoundsTolerance;
    }

    /// <summary>
    /// ページ先頭の句読点やページ末尾の開き括弧を避けます。
    /// 調整は後方へだけ行うため、調整後のページは必ず測定時の範囲内です。
    /// </summary>
    private static int AdjustPageBoundaryForJapaneseText(
        string text,
        int[] elementBoundaries,
        int startElementIndex,
        int endElementIndex,
        int elementCount
    )
    {
        int adjustedEnd =
            endElementIndex;

        while (
            adjustedEnd >
            startElementIndex + 1
        )
        {
            string lastElement =
                GetTextElement(
                    text,
                    elementBoundaries,
                    adjustedEnd - 1
                );

            if (
                !ProhibitedPageEndCharacters.Contains(
                    lastElement
                )
            )
            {
                break;
            }

            adjustedEnd--;
        }

        while (
            adjustedEnd >
                startElementIndex + 1 &&
            adjustedEnd <
                elementCount
        )
        {
            int nextVisibleElementIndex =
                FindNextVisibleElementIndex(
                    text,
                    elementBoundaries,
                    adjustedEnd,
                    elementCount
                );

            if (
                nextVisibleElementIndex >=
                elementCount
            )
            {
                break;
            }

            string nextVisibleElement =
                GetTextElement(
                    text,
                    elementBoundaries,
                    nextVisibleElementIndex
                );

            if (
                !ProhibitedPageStartCharacters.Contains(
                    nextVisibleElement
                )
            )
            {
                break;
            }

            int movedEnd =
                MoveBoundaryBeforePreviousVisibleElement(
                    text,
                    elementBoundaries,
                    startElementIndex,
                    adjustedEnd
                );

            if (movedEnd >= adjustedEnd)
            {
                break;
            }

            adjustedEnd = movedEnd;
        }

        return Mathf.Max(
            startElementIndex + 1,
            adjustedEnd
        );
    }

    private static int MoveBoundaryBeforePreviousVisibleElement(
        string text,
        int[] elementBoundaries,
        int startElementIndex,
        int endElementIndex
    )
    {
        int candidate =
            endElementIndex - 1;

        while (
            candidate >
            startElementIndex
        )
        {
            string element =
                GetTextElement(
                    text,
                    elementBoundaries,
                    candidate
                );

            if (!IsPageBoundaryWhitespace(element))
            {
                return candidate;
            }

            candidate--;
        }

        return endElementIndex;
    }

    private static int FindNextVisibleElementIndex(
        string text,
        int[] elementBoundaries,
        int startElementIndex,
        int elementCount
    )
    {
        int index = startElementIndex;

        while (index < elementCount)
        {
            string element =
                GetTextElement(
                    text,
                    elementBoundaries,
                    index
                );

            if (!IsPageBoundaryWhitespace(element))
            {
                break;
            }

            index++;
        }

        return index;
    }

    private static int SkipPageLeadingWhitespace(
        string text,
        int[] elementBoundaries,
        int startElementIndex,
        int elementCount
    )
    {
        int index = startElementIndex;

        while (index < elementCount)
        {
            string element =
                GetTextElement(
                    text,
                    elementBoundaries,
                    index
                );

            if (!IsPageBoundaryWhitespace(element))
            {
                break;
            }

            index++;
        }

        return index;
    }

    /// <summary>
    /// ページ境界で削除してよい空白です。
    /// 全角スペースは日本語段落の字下げとして残します。
    /// </summary>
    private static bool IsPageBoundaryWhitespace(
        string element
    )
    {
        return
            element == " " ||
            element == "\t" ||
            element == "\r" ||
            element == "\n" ||
            element == "\u00A0";
    }

    private static string TrimPageEnd(
        string pageText
    )
    {
        return pageText.TrimEnd(
            ' ',
            '\t',
            '\r',
            '\n',
            '\u00A0'
        );
    }

    private static string GetTextElement(
        string text,
        int[] elementBoundaries,
        int elementIndex
    )
    {
        int startIndex =
            elementBoundaries[elementIndex];

        int endIndex =
            elementBoundaries[elementIndex + 1];

        return text.Substring(
            startIndex,
            endIndex - startIndex
        );
    }

    private static int[] BuildTextElementBoundaries(
        string text
    )
    {
        int[] elementStarts =
            StringInfo.ParseCombiningCharacters(text);

        int[] boundaries =
            new int[elementStarts.Length + 1];

        Array.Copy(
            elementStarts,
            boundaries,
            elementStarts.Length
        );

        boundaries[boundaries.Length - 1] =
            text.Length;

        return boundaries;
    }

    private static void ValidateTextContainer(
        TMP_Text textComponent,
        string displayName
    )
    {
        textComponent.rectTransform
            .ForceUpdateRectTransforms();

        Rect rect =
            textComponent.rectTransform.rect;

        Vector4 margin =
            textComponent.margin;

        float availableWidth =
            rect.width -
            margin.x -
            margin.z;

        float availableHeight =
            rect.height -
            margin.y -
            margin.w;

        if (
            availableWidth <= 0f ||
            availableHeight <= 0f
        )
        {
            throw new InvalidOperationException(
                displayName +
                "のTextMeshPro表示領域が0以下です。" +
                "RectTransformとMarginを確認してください。"
            );
        }
    }

    private static void RestoreTextComponent(
        TMP_Text textComponent,
        string originalText
    )
    {
        if (textComponent == null)
        {
            return;
        }

        textComponent.text =
            originalText ?? string.Empty;

        textComponent.ForceMeshUpdate(
            ignoreActiveState: true,
            forceTextReparsing: true
        );
    }

    private static string NormalizeText(
        string text
    )
    {
        string normalized = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\t', ' ')
            .Replace('\u00A0', ' ');

        normalized = Regex.Replace(
            normalized,
            @"[ ]{2,}",
            " "
        );

        normalized = Regex.Replace(
            normalized,
            @" *\n *",
            "\n"
        );

        normalized = Regex.Replace(
            normalized,
            @"\n{3,}",
            "\n\n"
        );

        return normalized.Trim(
            ' ',
            '\n'
        );
    }
}