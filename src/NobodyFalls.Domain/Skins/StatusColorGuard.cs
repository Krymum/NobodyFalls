using System.Globalization;

namespace NobodyFalls.Domain.Skins;

/// <summary>
/// Status colours are reserved for statuses (ART_DIRECTION, Palette): no clothing may
/// look like the attention amber or the danger red, otherwise a coat could be read as
/// a warning.
/// </summary>
public static class StatusColorGuard
{
    public const string Attention = "#D8A85A";
    public const string Danger = "#D0645F";

    /// <summary>Minimum RGB distance from a status colour.</summary>
    public const double MinDistance = 64;

    public static bool TryParse(string? hex, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (hex is not { Length: 7 } || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        rgb = ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
        return true;
    }

    public static bool IsReserved(string hex)
    {
        if (!TryParse(hex, out var color))
        {
            throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
        }

        return Distance(color, Attention) < MinDistance || Distance(color, Danger) < MinDistance;
    }

    private static double Distance((int R, int G, int B) color, string reference)
    {
        if (!TryParse(reference, out var r))
        {
            throw new InvalidOperationException($"Reference colour '{reference}' is invalid.");
        }

        var dr = color.R - r.R;
        var dg = color.G - r.G;
        var db = color.B - r.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }
}
