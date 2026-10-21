using System.Globalization;

namespace HWSuite.Core.Model;

/// <summary>
/// Firmware and driver dates come in US order ("09/10/2026" is 10 September; drivers use
/// "10-18-2026"). The model stores ISO 8601 so reports can render them per language.
/// </summary>
public static class DateText
{
    private static readonly string[] SourceFormats =
    [
        "MM/dd/yyyy", "M/d/yyyy", "MM/dd/yy", "M/d/yy", "M-d-yyyy", "MM-dd-yyyy", "yyyy-MM-dd", "yyyyMMdd",
    ];

    /// <summary>Returns "yyyy-MM-dd", or the trimmed input when it is not a recognizable date.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        return DateOnly.TryParseExact(text, SourceFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : text;
    }

    /// <summary>Formats an ISO date in the reader's short date pattern; other text passes through.</summary>
    public static string? Display(string? value, CultureInfo culture) =>
        value is not null && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d", culture)
            : value;
}
