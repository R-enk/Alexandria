using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// EPUBから抽出した本文を、決定的な規則でページへ分割します。
/// UnityのGameObjectに依存しないため、単体テストしやすいクラスです。
/// </summary>
public static class BookPaginator
{
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

        List<string> pages = new List<string>();

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return pages;
        }

        string normalizedText = NormalizeText(sourceText);
        TextElementEnumerator enumerator =
            StringInfo.GetTextElementEnumerator(normalizedText);

        List<string> pageLines = new List<string>(linesPerPage);
        StringBuilder currentLine = new StringBuilder();
        int currentLineElementCount = 0;
        bool previousElementWasSpace = false;

        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();

            if (element == "\r")
            {
                continue;
            }

            if (element == "\n")
            {
                CommitLine(
                    currentLine,
                    ref currentLineElementCount,
                    pageLines,
                    pages,
                    linesPerPage,
                    allowEmptyLine: true
                );

                previousElementWasSpace = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(element))
            {
                if (
                    currentLine.Length == 0 ||
                    previousElementWasSpace
                )
                {
                    continue;
                }

                element = " ";
                previousElementWasSpace = true;
            }
            else
            {
                previousElementWasSpace = false;
            }

            currentLine.Append(element);
            currentLineElementCount++;

            if (currentLineElementCount >= charactersPerLine)
            {
                CommitLine(
                    currentLine,
                    ref currentLineElementCount,
                    pageLines,
                    pages,
                    linesPerPage,
                    allowEmptyLine: false
                );

                previousElementWasSpace = false;
            }
        }

        if (currentLine.Length > 0)
        {
            CommitLine(
                currentLine,
                ref currentLineElementCount,
                pageLines,
                pages,
                linesPerPage,
                allowEmptyLine: false
            );
        }

        CommitPage(pageLines, pages);

        return pages;
    }

    private static string NormalizeText(string text)
    {
        string normalized = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\t', ' ')
            .Replace('\u00A0', ' ');

        normalized = Regex.Replace(normalized, @"[ ]{2,}", " ");
        normalized = Regex.Replace(normalized, @" *\n *", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");

        return normalized.Trim();
    }

    private static void CommitLine(
        StringBuilder currentLine,
        ref int currentLineElementCount,
        List<string> pageLines,
        List<string> pages,
        int linesPerPage,
        bool allowEmptyLine
    )
    {
        string line = currentLine.ToString().TrimEnd();
        currentLine.Clear();
        currentLineElementCount = 0;

        if (line.Length == 0)
        {
            if (!allowEmptyLine)
            {
                return;
            }

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
            CommitPage(pageLines, pages);
        }
    }

    private static void CommitPage(
        List<string> pageLines,
        List<string> pages
    )
    {
        if (pageLines.Count == 0)
        {
            return;
        }

        while (
            pageLines.Count > 0 &&
            pageLines[pageLines.Count - 1].Length == 0
        )
        {
            pageLines.RemoveAt(pageLines.Count - 1);
        }

        if (pageLines.Count == 0)
        {
            return;
        }

        pages.Add(string.Join("\n", pageLines));
        pageLines.Clear();
    }
}
