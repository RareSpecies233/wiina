namespace Wiina.Core.Models;

/// <summary>One entry of mpv's <c>chapter-list</c>.</summary>
public sealed record ChapterInfo(int Index, double Time, string? Title)
{
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title;
            }

            return $"章节 {Index + 1}";
        }
    }
}
