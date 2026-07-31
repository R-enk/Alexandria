using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// TextMeshProの実際の表示領域を測定し、本文をページ単位へ分割します。
/// 左右ページのTextMeshProを交互に使用するため、左右で表示領域が異なる場合も
/// それぞれの領域に収まる位置で分割されます。
/// </summary>
public static class BookPaginator
{
    private static readonly HashSet<string>
        ProhibitedPageStartCharacters =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "、", "。", "，", "．", "・",
                "：", "；", "！", "？",
                "）", "〕", "］", "｝",
                "〉", "》", "」", "』", "】",
                "〙", "〗", "〟",
                "’", "”", "｠", "»",
                "ぁ", "ぃ", "ぅ", "ぇ", "ぉ",
                "っ", "ゃ", "ゅ", "ょ", "ゎ",
                "ァ", "ィ", "ゥ", "ェ", "ォ",
                "ッ", "ャ", "ュ", "ョ", "ヮ",
                "ヵ", "ヶ",
                "ー", "〜", "～", "…", "‥",
                "ヽ", "ヾ", "ゝ", "ゞ", "々",
                "％", "%", "℃", "°"
            };

    private static readonly HashSet<string>
        ProhibitedPageEndCharacters =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "（", "〔", "［", "｛",
                "〈", "《", "「", "『", "【",
                "〘", "〖", "〝",
                "‘", "“", "｟", "«"
            };

    public static List<string> Paginate(
        string sourceText,
        TMP_Text leftPageTextComponent,
        TMP_Text rightPageTextComponent
    )
    {
        if (leftPageTextComponent == null)
        {
            throw new ArgumentNullException(
                nameof(leftPageTextComponent)
            );
        }

        if (rightPageTextComponent == null)
        {
            throw new ArgumentNullException(
                nameof(rightPageTextComponent)
            );
        }

        ValidateMeasurementComponent(
            leftPageTextComponent,
            "左ページ"
        );

        ValidateMeasurementComponent(
            rightPageTextComponent,
            "右ページ"
        );

        List<string> pages =
            new List<string>();

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return pages;
        }

        string normalizedText =
            NormalizeText(sourceText);

        int sourceIndex = 0;
        int pageIndex = 0;
        int safetyCounter = 0;

        while (sourceIndex < normalizedText.Length)
        {
            sourceIndex =
                SkipAsciiBoundaryWhitespace(
                    normalizedText,
                    sourceIndex
                );

            if (sourceIndex >= normalizedText.Length)
            {
                break;
            }

            TMP_Text measurementComponent =
                pageIndex % 2 == 0
                    ? leftPageTextComponent
                    : rightPageTextComponent;

            string remainingText =
                normalizedText.Substring(sourceIndex);

            int pageLength =
                MeasurePageLength(
                    remainingText,
                    measurementComponent
                );

            pageLength =
                AlignToTextElementBoundary(
                    remainingText,
                    pageLength
                );

            pageLength =
                AdjustJapanesePageBoundary(
                    remainingText,
                    pageLength
                );

            if (pageLength <= 0)
            {
                throw new InvalidOperationException(
                    "TextMeshProによるページ分割位置を決定できませんでした。"
                );
            }

            string pageText =
                remainingText
                    .Substring(0, pageLength)
                    .TrimEnd(' ', '\t');

            if (pageText.Length == 0)
            {
                int firstElementLength =
                    GetFirstTextElementLength(
                        remainingText
                    );

                if (firstElementLength <= 0)
                {
                    throw new InvalidOperationException(
                        "本文から表示可能な文字を取得できませんでした。"
                    );
                }

                pageLength = firstElementLength;
                pageText =
                    remainingText.Substring(
                        0,
                        pageLength
                    );
            }

            pages.Add(pageText);

            sourceIndex += pageLength;
            pageIndex++;
            safetyCounter++;

            if (
                safetyCounter >
                normalizedText.Length + 1
            )
            {
                throw new InvalidOperationException(
                    "ページ分割処理が安全上限を超えました。"
                );
            }
        }

        ClearMeasurementText(
            leftPageTextComponent
        );

        ClearMeasurementText(
            rightPageTextComponent
        );

        return pages;
    }

    private static int MeasurePageLength(
        string remainingText,
        TMP_Text measurementComponent
    )
    {
        measurementComponent.text =
            remainingText;

        measurementComponent.ForceMeshUpdate(
            ignoreActiveState: true,
            forceTextReparsing: true
        );

        bool isOverflowing =
            measurementComponent.isTextOverflowing ||
            measurementComponent.isTextTruncated;

        int overflowIndex =
            measurementComponent
                .firstOverflowCharacterIndex;

        if (
            !isOverflowing ||
            overflowIndex < 0
        )
        {
            return remainingText.Length;
        }

        if (overflowIndex > 0)
        {
            return overflowIndex;
        }

        int firstElementLength =
            GetFirstTextElementLength(
                remainingText
            );

        if (firstElementLength <= 0)
        {
            throw new InvalidOperationException(
                "TextMeshProの表示領域へ文字を配置できません。"
            );
        }

        string firstElement =
            remainingText.Substring(
                0,
                firstElementLength
            );

        measurementComponent.text =
            firstElement;

        measurementComponent.ForceMeshUpdate(
            ignoreActiveState: true,
            forceTextReparsing: true
        );

        if (
            measurementComponent.isTextOverflowing ||
            measurementComponent.isTextTruncated
        )
        {
            throw new InvalidOperationException(
                "TextMeshProの表示領域に1文字も収まりません。" +
                "ページの幅・高さ、フォントサイズ、マージンを確認してください。"
            );
        }

        return firstElementLength;
    }

    private static int AlignToTextElementBoundary(
        string text,
        int requestedIndex
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (requestedIndex >= text.Length)
        {
            return text.Length;
        }

        if (requestedIndex <= 0)
        {
            return GetFirstTextElementLength(text);
        }

        int[] elementStarts =
            StringInfo.ParseCombiningCharacters(text);

        int alignedIndex = 0;

        foreach (int elementStart in elementStarts)
        {
            if (elementStart > requestedIndex)
            {
                break;
            }

            alignedIndex = elementStart;
        }

        if (alignedIndex <= 0)
        {
            return GetFirstTextElementLength(text);
        }

        return alignedIndex;
    }

    private static int AdjustJapanesePageBoundary(
        string text,
        int splitIndex
    )
    {
        if (
            splitIndex <= 0 ||
            splitIndex >= text.Length
        )
        {
            return splitIndex;
        }

        int adjustedIndex = splitIndex;
        int adjustmentCount = 0;
        const int MaxAdjustments = 8;

        while (
            adjustedIndex > 0 &&
            adjustedIndex < text.Length &&
            adjustmentCount < MaxAdjustments
        )
        {
            string nextElement =
                StringInfo.GetNextTextElement(
                    text,
                    adjustedIndex
                );

            if (
                !ProhibitedPageStartCharacters.Contains(
                    nextElement
                )
            )
            {
                break;
            }

            int previousIndex =
                GetPreviousTextElementStart(
                    text,
                    adjustedIndex
                );

            if (previousIndex <= 0)
            {
                break;
            }

            adjustedIndex = previousIndex;
            adjustmentCount++;
        }

        while (
            adjustedIndex > 0 &&
            adjustmentCount < MaxAdjustments
        )
        {
            int previousIndex =
                GetPreviousTextElementStart(
                    text,
                    adjustedIndex
                );

            if (previousIndex < 0)
            {
                break;
            }

            string previousElement =
                StringInfo.GetNextTextElement(
                    text,
                    previousIndex
                );

            if (
                !ProhibitedPageEndCharacters.Contains(
                    previousElement
                )
            )
            {
                break;
            }

            if (previousIndex <= 0)
            {
                break;
            }

            adjustedIndex = previousIndex;
            adjustmentCount++;
        }

        return adjustedIndex;
    }

    private static int GetPreviousTextElementStart(
        string text,
        int currentIndex
    )
    {
        if (
            string.IsNullOrEmpty(text) ||
            currentIndex <= 0
        )
        {
            return -1;
        }

        int[] elementStarts =
            StringInfo.ParseCombiningCharacters(text);

        int previousIndex = -1;

        foreach (int elementStart in elementStarts)
        {
            if (elementStart >= currentIndex)
            {
                break;
            }

            previousIndex = elementStart;
        }

        return previousIndex;
    }

    private static int GetFirstTextElementLength(
        string text
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return StringInfo
            .GetNextTextElement(text, 0)
            .Length;
    }

    private static int SkipAsciiBoundaryWhitespace(
        string text,
        int startIndex
    )
    {
        int index = startIndex;

        while (index < text.Length)
        {
            char character = text[index];

            if (
                character != ' ' &&
                character != '\t'
            )
            {
                break;
            }

            index++;
        }

        return index;
    }

    private static void ValidateMeasurementComponent(
        TMP_Text textComponent,
        string pageName
    )
    {
        Rect rect =
            textComponent.rectTransform.rect;

        if (
            rect.width <= 0.01f ||
            rect.height <= 0.01f
        )
        {
            throw new InvalidOperationException(
                pageName +
                "のTextMeshPro表示領域の幅または高さが0です。"
            );
        }
    }

    private static void ClearMeasurementText(
        TMP_Text textComponent
    )
    {
        textComponent.text = string.Empty;

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
