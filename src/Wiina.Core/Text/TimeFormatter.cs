namespace Wiina.Core.Text;

/// <summary>
/// Player time labels. Uses <c>h:mm:ss</c> for one hour and above, otherwise
/// <c>m:ss</c> (IINA uses the same compact form).
/// </summary>
public static class TimeFormatter
{
    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        long total = (long)Math.Floor(seconds);
        long hours = total / 3600;
        long minutes = total % 3600 / 60;
        long secs = total % 60;

        return hours > 0
            ? $"{hours}:{minutes:00}:{secs:00}"
            : $"{minutes}:{secs:00}";
    }
}
