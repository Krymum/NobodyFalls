using System.Text;

namespace NobodyFalls.Domain.Feedback;

/// <summary>Cleaning of free text written by players (ideas, requests, reports).</summary>
public static class Text
{
    /// <summary>
    /// Trims, normalises to NFC, drops control and invisible format characters (zero-width
    /// spaces, direction marks), keeping line breaks when asked,
    /// and collapses runs of spaces. Lengths are checked on the cleaned text.
    /// </summary>
    public static string Clean(string? raw, bool keepLines = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var builder = new StringBuilder(raw.Length);
        var lastWasSpace = false;
        foreach (var c in raw.Normalize(NormalizationForm.FormC))
        {
            if (c == '\n' && keepLines)
            {
                builder.Append('\n');
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
            }
            else if (!char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }

        var text = builder.ToString();
        if (keepLines)
        {
            // At most one empty line in a row, no spaces around line breaks.
            var lines = text.Split('\n').Select(l => l.Trim());
            text = string.Join('\n', lines);
            while (text.Contains("\n\n\n", StringComparison.Ordinal))
            {
                text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
            }
        }

        return text.Trim();
    }
}
