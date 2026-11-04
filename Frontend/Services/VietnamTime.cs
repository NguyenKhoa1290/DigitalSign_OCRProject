using System.Globalization;

namespace HauDocumentApp.Services;

/// <summary>API instants remain UTC; display them in Vietnam regardless of the browser time zone.</summary>
public static class VietnamTime
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static string Format(DateTime instant, string format = "dd/MM/yyyy HH:mm")
        => Format((DateTime?)instant, format);

    public static string Format(DateTime? instant, string format = "dd/MM/yyyy HH:mm")
    {
        if (!instant.HasValue) return "—";
        // Legacy API timestamps without a suffix were also recorded as UTC.
        var value = instant.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(instant.Value, DateTimeKind.Utc) : instant.Value;
        return Format(new DateTimeOffset(value), format);
    }

    public static string Format(DateTimeOffset instant, string format = "dd/MM/yyyy HH:mm")
        => instant.ToOffset(Offset).ToString(format, CultureInfo.InvariantCulture);
}
