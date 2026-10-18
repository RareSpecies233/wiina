using System.Globalization;
using Wiina.Core.Settings;

namespace Wiina.Core.Filters;

/// <summary>Built-in video filter kinds exposed by the filter editor.</summary>
public enum VideoFilterKind
{
    ColorAdjust,
    Sharpen,
    Denoise,
    Deband,
    Crop,
    Rotate,
    FlipHorizontal,
    FlipVertical,
    Deinterlace,
    Delogo,
    Format,
    Scale,
}

/// <summary>
/// One persisted video filter. A single settings class keeps the JSON stable
/// while every kind only reads the fields that the catalog exposes for it.
/// </summary>
public sealed class VideoFilterSetting
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public VideoFilterKind Kind { get; set; }

    public bool Enabled { get; set; } = true;

    public double Brightness { get; set; }

    public double Contrast { get; set; } = 1;

    public double Saturation { get; set; } = 1;

    public double Gamma { get; set; } = 1;

    public double Hue { get; set; }

    public double Amount { get; set; } = 0.5;

    public double Strength { get; set; } = 4;

    public double Threshold { get; set; } = 0.02;

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int Rotation { get; set; } = 90;

    public string PixelFormat { get; set; } = "yuv420p";

    public int ScaleWidth { get; set; } = -1;

    public int ScaleHeight { get; set; } = -1;

    public VideoFilterSetting Clone() => (VideoFilterSetting)MemberwiseClone();
}

/// <summary>Named, user-saved video filter chain.</summary>
public sealed class VideoFilterPreset
{
    public string Name { get; set; } = string.Empty;

    public List<VideoFilterSetting> Filters { get; set; } = new();
}

/// <summary>One editable numeric parameter of a filter kind.</summary>
public sealed record FilterParameter(
    string Key,
    string Label,
    double Minimum,
    double Maximum,
    double Step,
    string Suffix = "");

/// <summary>Catalog metadata and parameter mapping for the video filter editor.</summary>
public static class VideoFilterCatalog
{
    public sealed record KindInfo(
        VideoFilterKind Kind,
        string Name,
        string Description,
        string Glyph);

    public static readonly IReadOnlyList<KindInfo> All = new[]
    {
        new KindInfo(VideoFilterKind.ColorAdjust, "色彩调整 (eq)",
            "亮度、对比度、饱和度、伽马与色相。", "\uE790"),
        new KindInfo(VideoFilterKind.Sharpen, "锐化 (unsharp)",
            "提升边缘锐度；过大会产生光晕。", "\uE8B9"),
        new KindInfo(VideoFilterKind.Denoise, "降噪 (hqdn3d)",
            "时域/空域降噪，适合高 ISO 或旧片源。", "\uE9D9"),
        new KindInfo(VideoFilterKind.Deband, "去色带 (deband)",
            "平滑渐变区域的色带。", "\uE7C4"),
        new KindInfo(VideoFilterKind.Crop, "裁剪 (crop)",
            "按像素裁剪画面；与快速设置的比例裁剪互斥。", "\uE7A8"),
        new KindInfo(VideoFilterKind.Rotate, "旋转 (transpose)",
            "顺时针 90/180/270 度；与快速设置旋转互斥。", "\uE7AD"),
        new KindInfo(VideoFilterKind.FlipHorizontal, "水平翻转 (hflip)",
            "左右镜像；与快速设置水平翻转互斥。", "\uE8AB"),
        new KindInfo(VideoFilterKind.FlipVertical, "垂直翻转 (vflip)",
            "上下镜像；与快速设置垂直翻转互斥。", "\uE8AC"),
        new KindInfo(VideoFilterKind.Deinterlace, "去隔行 (yadif)",
            "逐帧去隔行；与快速设置去隔行互斥。", "\uE7C4"),
        new KindInfo(VideoFilterKind.Delogo, "去 Logo (delogo)",
            "用周围像素覆盖指定矩形区域。", "\uE71E"),
        new KindInfo(VideoFilterKind.Format, "像素格式 (format)",
            "强制输出像素格式，通常用于排查渲染问题。", "\uE8B7"),
        new KindInfo(VideoFilterKind.Scale, "缩放 (scale)",
            "在 GPU 渲染前重采样；-1 表示按比例。", "\uE740"),
    };

    public static readonly IReadOnlyList<string> PixelFormats = new[]
    {
        "yuv420p", "yuv422p", "yuv444p", "yuv420p10le", "yuv420p12le",
        "nv12", "p010le", "rgb24", "bgra",
    };

    public static KindInfo Info(VideoFilterKind kind) =>
        All.First(info => info.Kind == kind);

    public static VideoFilterSetting Create(VideoFilterKind kind)
    {
        var filter = new VideoFilterSetting { Kind = kind };
        switch (kind)
        {
            case VideoFilterKind.Delogo:
                filter.Width = 320;
                filter.Height = 80;
                break;
            case VideoFilterKind.Crop:
                filter.Width = 1280;
                filter.Height = 720;
                break;
            case VideoFilterKind.Scale:
                filter.ScaleWidth = 1280;
                filter.ScaleHeight = -1;
                break;
        }

        return filter;
    }

    /// <summary>Parameters the editor exposes for one kind, in display order.</summary>
    public static IReadOnlyList<FilterParameter> Parameters(VideoFilterKind kind) => kind switch
    {
        VideoFilterKind.ColorAdjust => new[]
        {
            new FilterParameter("brightness", "亮度", -1, 1, 0.01),
            new FilterParameter("contrast", "对比度", 0, 2, 0.01),
            new FilterParameter("saturation", "饱和度", 0, 3, 0.01),
            new FilterParameter("gamma", "伽马", 0.1, 10, 0.05),
            new FilterParameter("hue", "色相", -180, 180, 1, "°"),
        },
        VideoFilterKind.Sharpen => new[]
        {
            new FilterParameter("amount", "强度", 0, 2, 0.05),
        },
        VideoFilterKind.Denoise => new[]
        {
            new FilterParameter("strength", "强度", 0, 10, 0.5),
        },
        VideoFilterKind.Deband => new[]
        {
            new FilterParameter("threshold", "阈值", 0.001, 0.2, 0.001),
        },
        VideoFilterKind.Crop => new[]
        {
            new FilterParameter("x", "X", 0, 16384, 1, " px"),
            new FilterParameter("y", "Y", 0, 16384, 1, " px"),
            new FilterParameter("width", "宽", 2, 16384, 2, " px"),
            new FilterParameter("height", "高", 2, 16384, 2, " px"),
        },
        VideoFilterKind.Rotate => new[]
        {
            new FilterParameter("rotation", "角度", 0, 270, 90, "°"),
        },
        VideoFilterKind.Delogo => new[]
        {
            new FilterParameter("x", "X", 0, 16384, 1, " px"),
            new FilterParameter("y", "Y", 0, 16384, 1, " px"),
            new FilterParameter("width", "宽", 2, 16384, 2, " px"),
            new FilterParameter("height", "高", 2, 16384, 2, " px"),
        },
        VideoFilterKind.Scale => new[]
        {
            new FilterParameter("scaleWidth", "宽", -1, 16384, 2, " px"),
            new FilterParameter("scaleHeight", "高", -1, 16384, 2, " px"),
        },
        VideoFilterKind.Format => Array.Empty<FilterParameter>(),
        _ => Array.Empty<FilterParameter>(),
    };

    public static double GetParameter(VideoFilterSetting filter, string key) => key switch
    {
        "brightness" => filter.Brightness,
        "contrast" => filter.Contrast,
        "saturation" => filter.Saturation,
        "gamma" => filter.Gamma,
        "hue" => filter.Hue,
        "amount" => filter.Amount,
        "strength" => filter.Strength,
        "threshold" => filter.Threshold,
        "x" => filter.X,
        "y" => filter.Y,
        "width" => filter.Width,
        "height" => filter.Height,
        "rotation" => filter.Rotation,
        "scaleWidth" => filter.ScaleWidth,
        "scaleHeight" => filter.ScaleHeight,
        _ => 0,
    };

    public static void SetParameter(VideoFilterSetting filter, string key, double value)
    {
        switch (key)
        {
            case "brightness":
                filter.Brightness = value;
                break;
            case "contrast":
                filter.Contrast = value;
                break;
            case "saturation":
                filter.Saturation = value;
                break;
            case "gamma":
                filter.Gamma = value;
                break;
            case "hue":
                filter.Hue = value;
                break;
            case "amount":
                filter.Amount = value;
                break;
            case "strength":
                filter.Strength = value;
                break;
            case "threshold":
                filter.Threshold = value;
                break;
            case "x":
                filter.X = (int)Math.Round(value);
                break;
            case "y":
                filter.Y = (int)Math.Round(value);
                break;
            case "width":
                filter.Width = (int)Math.Round(value);
                break;
            case "height":
                filter.Height = (int)Math.Round(value);
                break;
            case "rotation":
                filter.Rotation = QuickSettings.NormalizeRotation((int)Math.Round(value));
                break;
            case "scaleWidth":
                filter.ScaleWidth = (int)Math.Round(value);
                break;
            case "scaleHeight":
                filter.ScaleHeight = (int)Math.Round(value);
                break;
        }
    }

    /// <summary>Short one-line summary shown in the active-filter list.</summary>
    public static string Describe(VideoFilterSetting filter) => filter.Kind switch
    {
        VideoFilterKind.ColorAdjust =>
            $"亮度 {filter.Brightness:+0.00;-0.00;0.00} · 对比 {filter.Contrast:0.00} · 饱和 {filter.Saturation:0.00} · 色相 {filter.Hue:0}°",
        VideoFilterKind.Sharpen => $"强度 {filter.Amount:0.00}",
        VideoFilterKind.Denoise => $"强度 {filter.Strength:0.0}",
        VideoFilterKind.Deband => $"阈值 {filter.Threshold:0.000}",
        VideoFilterKind.Crop => $"{filter.Width}×{filter.Height} @ {filter.X},{filter.Y}",
        VideoFilterKind.Rotate => $"{QuickSettings.NormalizeRotation(filter.Rotation)}°",
        VideoFilterKind.Delogo => $"{filter.Width}×{filter.Height} @ {filter.X},{filter.Y}",
        VideoFilterKind.Format => filter.PixelFormat,
        VideoFilterKind.Scale => $"{filter.ScaleWidth}×{filter.ScaleHeight}",
        _ => string.Empty,
    };
}

/// <summary>Stage 2 picture settings that still feed the same vf chain.</summary>
public sealed record VideoFilterLegacy(
    bool FlipHorizontal,
    bool FlipVertical,
    double CropAspect,
    int Rotation,
    bool Deinterlace,
    bool DelogoEnabled,
    int DelogoX,
    int DelogoY,
    int DelogoWidth,
    int DelogoHeight)
{
    public static VideoFilterLegacy None { get; } =
        new(false, false, 0, 0, false, false, 0, 0, 0, 0);
}

public sealed record VideoFilterGraphResult(
    string Graph,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Applied);

/// <summary>
/// Builds the mpv <c>vf</c> graph for the editor list plus the Stage 2 picture
/// toggles. The builder never throws: invalid entries are skipped and reported
/// so the caller can keep playback alive and show an OSD message.
/// </summary>
public static class VideoFilterGraph
{
    public static VideoFilterGraphResult Build(
        IReadOnlyList<VideoFilterSetting>? filters,
        int videoWidth,
        int videoHeight,
        VideoFilterLegacy? legacy = null)
    {
        legacy ??= VideoFilterLegacy.None;
        var graph = new List<string>();
        var errors = new List<string>();
        var applied = new List<string>();
        var seenKinds = new HashSet<VideoFilterKind>();

        // Legacy crop is prepended, matching the pre-Stage-8 chain order.
        if (legacy.CropAspect > 0 && videoWidth > 0 && videoHeight > 0)
        {
            string crop = QuickSettings.ComputeCrop(legacy.CropAspect, videoWidth, videoHeight);
            if (crop != "no" && crop.Contains('x'))
            {
                string[] parts = crop.Split('x');
                graph.Add($"crop={parts[0]}:{parts[1]}:{(videoWidth - int.Parse(parts[0], CultureInfo.InvariantCulture)) / 2}:{(videoHeight - int.Parse(parts[1], CultureInfo.InvariantCulture)) / 2}");
                applied.Add("快速设置裁剪");
            }
        }

        if (legacy.DelogoEnabled)
        {
            if (legacy.DelogoWidth > 1 && legacy.DelogoHeight > 1 &&
                legacy.DelogoX >= 0 && legacy.DelogoY >= 0)
            {
                graph.Add($"delogo=x={legacy.DelogoX}:y={legacy.DelogoY}:w={legacy.DelogoWidth}:h={legacy.DelogoHeight}");
                applied.Add("快速设置去 Logo");
                seenKinds.Add(VideoFilterKind.Delogo);
            }
            else
            {
                errors.Add("去 Logo 的宽高必须大于 1 像素");
            }
        }

        foreach (VideoFilterSetting filter in filters ?? Array.Empty<VideoFilterSetting>())
        {
            if (!filter.Enabled)
            {
                continue;
            }

            string name = VideoFilterCatalog.Info(filter.Kind).Name;
            string? conflict = Conflict(filter.Kind, legacy);
            if (conflict is not null)
            {
                errors.Add($"{name}：{conflict}");
                continue;
            }

            if (!seenKinds.Add(filter.Kind))
            {
                errors.Add($"{name}：同一滤镜只能添加一次");
                continue;
            }

            string? expression = BuildOne(filter, videoWidth, videoHeight, out string? error);
            if (error is not null)
            {
                errors.Add($"{name}：{error}");
                continue;
            }

            if (!string.IsNullOrEmpty(expression))
            {
                graph.Add(expression);
                applied.Add(name);
            }
        }

        // Legacy flips stay at the tail so Stage 2 behaviour is unchanged.
        if (legacy.FlipHorizontal)
        {
            graph.Add("hflip");
            applied.Add("快速设置水平翻转");
        }

        if (legacy.FlipVertical)
        {
            graph.Add("vflip");
            applied.Add("快速设置垂直翻转");
        }

        return new VideoFilterGraphResult(string.Join(',', graph), errors, applied);
    }

    private static string? Conflict(VideoFilterKind kind, VideoFilterLegacy legacy) => kind switch
    {
        VideoFilterKind.Crop when legacy.CropAspect > 0 =>
            "快速设置里已启用比例裁剪，请先设为“无”",
        VideoFilterKind.Delogo when legacy.DelogoEnabled =>
            "快速设置里已启用去 Logo",
        VideoFilterKind.Rotate when legacy.Rotation != 0 =>
            "快速设置里已设置旋转，请先设为 0°",
        VideoFilterKind.Deinterlace when legacy.Deinterlace =>
            "快速设置里已启用去隔行",
        VideoFilterKind.FlipHorizontal when legacy.FlipHorizontal =>
            "快速设置里已启用水平翻转",
        VideoFilterKind.FlipVertical when legacy.FlipVertical =>
            "快速设置里已启用垂直翻转",
        _ => null,
    };

    private static string? BuildOne(
        VideoFilterSetting filter,
        int videoWidth,
        int videoHeight,
        out string? error)
    {
        error = null;
        switch (filter.Kind)
        {
            case VideoFilterKind.ColorAdjust:
            {
                string eq = "eq=" + string.Join(
                    ':',
                    "brightness=" + N(filter.Brightness),
                    "contrast=" + N(filter.Contrast),
                    "saturation=" + N(filter.Saturation),
                    "gamma=" + N(filter.Gamma));
                if (Math.Abs(filter.Hue) < 0.0001)
                {
                    return eq;
                }

                return eq + $",hue=h={N(filter.Hue)}";
            }

            case VideoFilterKind.Sharpen:
                if (filter.Amount < 0 || filter.Amount > 2)
                {
                    error = "强度必须在 0–2 之间";
                    return null;
                }

                return $"unsharp=5:5:{N(filter.Amount)}";

            case VideoFilterKind.Denoise:
                if (filter.Strength < 0 || filter.Strength > 10)
                {
                    error = "强度必须在 0–10 之间";
                    return null;
                }

                return $"hqdn3d={N(filter.Strength)}:{N(filter.Strength * 0.75)}:" +
                    $"{N(filter.Strength * 1.5)}:{N(filter.Strength * 1.125)}";

            case VideoFilterKind.Deband:
                if (filter.Threshold <= 0 || filter.Threshold > 0.2)
                {
                    error = "阈值必须在 0.001–0.2 之间";
                    return null;
                }

                return $"deband=1thr={N(filter.Threshold)}:2thr={N(filter.Threshold)}:" +
                    $"3thr={N(filter.Threshold)}:4thr={N(filter.Threshold)}";

            case VideoFilterKind.Crop:
                if (filter.Width < 2 || filter.Height < 2 ||
                    filter.X < 0 || filter.Y < 0)
                {
                    error = "裁剪宽高至少为 2，X/Y 不能为负";
                    return null;
                }

                if (videoWidth > 0 && videoHeight > 0 &&
                    (filter.X + filter.Width > videoWidth ||
                     filter.Y + filter.Height > videoHeight))
                {
                    error = $"裁剪矩形超出 {videoWidth}×{videoHeight} 画面";
                    return null;
                }

                return $"crop={filter.Width}:{filter.Height}:{filter.X}:{filter.Y}";

            case VideoFilterKind.Rotate:
            {
                int rotation = QuickSettings.NormalizeRotation(filter.Rotation);
                return rotation switch
                {
                    90 => "transpose=1",
                    180 => "hflip,vflip",
                    270 => "transpose=2",
                    _ => string.Empty,
                };
            }

            case VideoFilterKind.FlipHorizontal:
                return "hflip";
            case VideoFilterKind.FlipVertical:
                return "vflip";
            case VideoFilterKind.Deinterlace:
                return "yadif=1";
            case VideoFilterKind.Delogo:
                if (filter.Width < 2 || filter.Height < 2 ||
                    filter.X < 0 || filter.Y < 0)
                {
                    error = "去 Logo 宽高至少为 2，X/Y 不能为负";
                    return null;
                }

                if (videoWidth > 0 && videoHeight > 0 &&
                    (filter.X + filter.Width > videoWidth ||
                     filter.Y + filter.Height > videoHeight))
                {
                    error = $"去 Logo 矩形超出 {videoWidth}×{videoHeight} 画面";
                    return null;
                }

                return $"delogo=x={filter.X}:y={filter.Y}:w={filter.Width}:h={filter.Height}";

            case VideoFilterKind.Format:
            {
                string format = (filter.PixelFormat ?? string.Empty).Trim();
                if (!VideoFilterCatalog.PixelFormats.Contains(format, StringComparer.Ordinal))
                {
                    error = "不支持的像素格式";
                    return null;
                }

                return $"format={format}";
            }

            case VideoFilterKind.Scale:
                if ((filter.ScaleWidth < -1 || filter.ScaleWidth == 0) ||
                    (filter.ScaleHeight < -1 || filter.ScaleHeight == 0) ||
                    (filter.ScaleWidth == -1 && filter.ScaleHeight == -1))
                {
                    error = "宽高必须为正数，或其中一项为 -1";
                    return null;
                }

                return $"scale={filter.ScaleWidth}:{filter.ScaleHeight}";

            default:
                error = "未知滤镜";
                return null;
        }
    }

    private static string N(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
