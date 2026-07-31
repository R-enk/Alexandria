using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// EPUBから抽出した本文をページ単位へ分割します。
///
/// 主な処理:
/// ・Unicodeの書記素単位で文字を扱う
/// ・EPUBから抽出した改行を維持する
/// ・日本語の行頭禁則と行末禁則を適用する
/// ・同じ本文から常に同じページを生成する
/// </summary>
public static class BookPaginator
{
    /// <summary>
    /// 行頭に配置しない文字です。
    ///
    /// 句読点、閉じ括弧、小書き文字、長音記号などを含みます。
    /// </summary>
    private static readonly HashSet<string>
        ProhibitedLineStartCharacters =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "、",
                "。",
                "，",
                "．",
                "・",
                "：",
                "；",
                "！",
                "？",
                "‼",
                "⁇",
                "⁈",
                "⁉",

                "）",
                "〕",
                "］",
                "｝",
                "〉",
                "》",
                "」",
                "』",
                "】",
                "〙",
                "〗",
                "〟",
                "’",
                "”",
                "｠",
                "»",

                "ぁ",
                "ぃ",
                "ぅ",
                "ぇ",
                "ぉ",
                "っ",
                "ゃ",
                "ゅ",
                "ょ",
                "ゎ",

                "ァ",
                "ィ",
                "ゥ",
                "ェ",
                "ォ",
                "ッ",
                "ャ",
                "ュ",
                "ョ",
                "ヮ",
                "ヵ",
                "ヶ",

                "ー",
                "〜",
                "～",
                "…",
                "‥",
                "ヽ",
                "ヾ",
                "ゝ",
                "ゞ",
                "々",

                "％",
                "%",
                "℃",
                "°"
            };

    /// <summary>
    /// 行末に配置しない文字です。
    ///
    /// 開き括弧や開き引用符などを含みます。
    /// </summary>
    private static readonly HashSet<string>
        ProhibitedLineEndCharacters =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "（",
                "〔",
                "［",
                "｛",
                "〈",
                "《",
                "「",
                "『",
                "【",
                "〘",
                "〖",
                "〝",
                "‘",
                "“",
                "｟",
                "«"
            };

    /// <summary>
    /// 本文をページへ分割します。
    /// </summary>
    public static List<string> Paginate(
        string sourceText,
        int charactersPerLine,
        int linesPerPage
    )
    {
        if (charactersPerLine <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(charactersPerLine),
                "1行の文字数は1以上である必要があります。"
            );
        }

        if (linesPerPage <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(linesPerPage),
                "1ページの行数は1以上である必要があります。"
            );
        }

        List<string> pages =
            new List<string>();

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return pages;
        }

        string normalizedText =
            NormalizeText(sourceText);

        List<string> textElements =
            GetTextElements(normalizedText);

        List<string> pageLines =
            new List<string>(linesPerPage);

        List<string> currentLine =
            new List<string>(charactersPerLine + 4);

        bool previousElementWasSpace = false;
        int index = 0;

        while (index < textElements.Count)
        {
            string element = textElements[index];

            if (element == "\r")
            {
                index++;
                continue;
            }

            // EPUBに記録された明示的な改行です。
            if (element == "\n")
            {
                CommitLine(
                    currentLine,
                    pageLines,
                    pages,
                    linesPerPage,
                    allowEmptyLine: true
                );

                previousElementWasSpace = false;
                index++;
                continue;
            }

            // 半角空白などは連続させません。
            // 全角スペースは字下げとして残します。
            if (IsCollapsibleWhitespace(element))
            {
                if (
                    currentLine.Count == 0 ||
                    previousElementWasSpace
                )
                {
                    index++;
                    continue;
                }

                element = " ";
                previousElementWasSpace = true;
            }
            else
            {
                previousElementWasSpace = false;
            }

            currentLine.Add(element);
            index++;

            if (currentLine.Count < charactersPerLine)
            {
                continue;
            }

            /*
             * 次の文字が「、」「。」などの場合は、
             * 次行の先頭へ送らず現在行へ追加します。
             *
             * その結果、指定文字数を1～数文字だけ超えることがあります。
             * これは句読点が行頭へ来るより自然な表示を優先するためです。
             */
            AbsorbProhibitedLineStartCharacters(
                textElements,
                ref index,
                currentLine
            );

            /*
             * 現在行の末尾が「「」や「（」の場合は、
             * その開き括弧を次行へ移動します。
             */
            List<string> carryOver =
                DetachProhibitedLineEndCharacters(
                    currentLine
                );

            CommitLine(
                currentLine,
                pageLines,
                pages,
                linesPerPage,
                allowEmptyLine: false
            );

            currentLine.AddRange(carryOver);

            previousElementWasSpace =
                currentLine.Count > 0 &&
                currentLine[currentLine.Count - 1] == " ";
        }

        if (currentLine.Count > 0)
        {
            CommitLine(
                currentLine,
                pageLines,
                pages,
                linesPerPage,
                allowEmptyLine: false
            );
        }

        CommitPage(
            pageLines,
            pages
        );

        return pages;
    }

    /// <summary>
    /// Unicode文字列を書記素単位へ分解します。
    ///
    /// 絵文字や結合文字を途中で分割しにくくします。
    /// </summary>
    private static List<string> GetTextElements(
        string text
    )
    {
        List<string> elements =
            new List<string>(text.Length);

        TextElementEnumerator enumerator =
            StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
        {
            elements.Add(
                enumerator.GetTextElement()
            );
        }

        return elements;
    }

    /// <summary>
    /// 次行の先頭に置けない文字を現在行へ取り込みます。
    /// </summary>
    private static void AbsorbProhibitedLineStartCharacters(
        IReadOnlyList<string> textElements,
        ref int index,
        List<string> currentLine
    )
    {
        while (index < textElements.Count)
        {
            string nextElement =
                textElements[index];

            if (
                nextElement == "\n" ||
                nextElement == "\r"
            )
            {
                return;
            }

            if (
                !ProhibitedLineStartCharacters.Contains(
                    nextElement
                )
            )
            {
                return;
            }

            currentLine.Add(nextElement);
            index++;
        }
    }

    /// <summary>
    /// 行末に置けない文字を取り外し、次行へ引き継ぎます。
    /// </summary>
    private static List<string>
        DetachProhibitedLineEndCharacters(
            List<string> currentLine
        )
    {
        List<string> carryOver =
            new List<string>();

        /*
         * 行が完全に空になるのを避けるため、
         * 最低1文字は現在行に残します。
         */
        while (currentLine.Count > 1)
        {
            string lastElement =
                currentLine[currentLine.Count - 1];

            if (
                !ProhibitedLineEndCharacters.Contains(
                    lastElement
                )
            )
            {
                break;
            }

            currentLine.RemoveAt(
                currentLine.Count - 1
            );

            carryOver.Insert(
                0,
                lastElement
            );
        }

        return carryOver;
    }

    /// <summary>
    /// 現在行をページ内の行として確定します。
    /// </summary>
    private static void CommitLine(
        List<string> currentLine,
        List<string> pageLines,
        List<string> pages,
        int linesPerPage,
        bool allowEmptyLine
    )
    {
        string line =
            BuildString(currentLine)
                .TrimEnd(' ', '\t');

        currentLine.Clear();

        if (line.Length == 0)
        {
            if (!allowEmptyLine)
            {
                return;
            }

            /*
             * ページ先頭の空行と、
             * 連続しすぎる空行は追加しません。
             */
            if (
                pageLines.Count == 0 ||
                pageLines[pageLines.Count - 1].Length == 0
            )
            {
                return;
            }
        }

        pageLines.Add(line);

        if (pageLines.Count >= linesPerPage)
        {
            CommitPage(
                pageLines,
                pages
            );
        }
    }

    /// <summary>
    /// ページ内の行を1ページの文字列として確定します。
    /// </summary>
    private static void CommitPage(
        List<string> pageLines,
        List<string> pages
    )
    {
        if (pageLines.Count == 0)
        {
            return;
        }

        // ページ末尾の空行は削除します。
        while (
            pageLines.Count > 0 &&
            pageLines[pageLines.Count - 1].Length == 0
        )
        {
            pageLines.RemoveAt(
                pageLines.Count - 1
            );
        }

        if (pageLines.Count == 0)
        {
            return;
        }

        pages.Add(
            string.Join(
                "\n",
                pageLines
            )
        );

        pageLines.Clear();
    }

    private static string BuildString(
        IEnumerable<string> elements
    )
    {
        StringBuilder builder =
            new StringBuilder();

        foreach (string element in elements)
        {
            builder.Append(element);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 半角系の空白か判定します。
    ///
    /// 全角スペース U+3000 は、
    /// 日本語の段落字下げに使われるため対象外です。
    /// </summary>
    private static bool IsCollapsibleWhitespace(
        string element
    )
    {
        return
            element == " " ||
            element == "\t" ||
            element == "\f" ||
            element == "\v" ||
            element == "\u00A0";
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

        // 半角空白だけをまとめます。
        // 全角スペースは保持します。
        normalized = Regex.Replace(
            normalized,
            @"[ ]{2,}",
            " "
        );

        // 改行の直前・直後にある半角空白を削除します。
        normalized = Regex.Replace(
            normalized,
            @" *\n *",
            "\n"
        );

        // 空行は最大1行まで残します。
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