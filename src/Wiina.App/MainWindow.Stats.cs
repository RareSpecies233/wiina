using System.Globalization;
using System.IO;
using Microsoft.UI.Xaml;
using Wiina.App.Controls;
using Wiina.Mpv;

namespace Wiina.App;

/// <summary>
/// Issue 8: the old implementation pushed a four-field string through mpv's
/// show-text OSD once per second. That competed with every other OSD message,
/// only became visible after the first frame, and could not show more than a
/// single line. This partial owns the native WinUI statistics card instead.
/// </summary>
public sealed partial class MainWindow
{
    private long _lastStatsDroppedFrames;
    private bool _statsPanelWired;

    private void InitializeStatsPanel()
    {
        if (_statsPanelWired)
        {
            return;
        }

        _statsPanelWired = true;
        StatsPanel.CloseRequested += (_, _) =>
        {
            _settings.ShowMpvStats = false;
            ApplyStatsSettings();
            SyncQuickSettingsValues();
            SaveSettings();
        };
    }

    /// <summary>Shows/hides the card and fills it immediately, without waiting
    /// for the next one-second tick (the old OSD could take seconds to appear).</summary>
    private void ApplyStatsSettings()
    {
        InitializeStatsPanel();
        if (!_settings.ShowMpvStats)
        {
            StatsPanel.Hide();
            return;
        }

        StatsPanel.Show();
        UpdateStatsPanel();
    }

    private void UpdateStatsPanel()
    {
        if (!_settings.ShowMpvStats)
        {
            StatsPanel.Hide();
            return;
        }

        if (_player is not { IsRunning: true } player)
        {
            StatsPanel.Show("等待播放");
            StatsPanel.SetRows(new[]
            {
                new PlaybackStatRow("状态", "播放器", "正在启动…"),
            });
            return;
        }

        var rows = new List<PlaybackStatRow>(24);
        string file = Str(player, "filename")
            ?? Path.GetFileName(_currentPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(file))
        {
            file = "—";
        }
        string title = Str(player, "media-title") ?? _currentTitle ?? file;
        StatsPanel.Show(string.Equals(title, file, StringComparison.Ordinal) ? file : title);

        Add(rows, "文件", "格式", Str(player, "file-format"));
        Add(rows, "文件", "大小", Size(Int(player, "file-size")));
        Add(rows, "文件", "文件", file);

        long width = Int(player, "width");
        long height = Int(player, "height");
        bool hasVideo = width > 0 && height > 0;
        if (hasVideo)
        {
            Add(rows, "视频", "编码", Str(player, "video-codec"));
            Add(rows, "视频", "分辨率",
                $"{width}×{height}" + PixelAspect(player));
            Add(rows, "视频", "帧率", Fps(player));
            Add(rows, "视频", "码率", Bitrate(Number(player, "video-bitrate")));
            Add(rows, "视频", "像素格式", Str(player, "video-params/pixelformat"));
            Add(rows, "视频", "色彩", ColorInfo(player));
            string? hwdec = Str(player, "hwdec-current");
            Add(rows, "视频", "硬件解码", string.IsNullOrWhiteSpace(hwdec) ? "软解（未启用）" : hwdec);
            Add(rows, "视频", "旋转", Rotate(player));
        }
        else
        {
            Add(rows, "视频", "画面", "无视频轨（音频播放）");
        }

        Add(rows, "音频", "编码", Str(player, "audio-codec"));
        long channels = Int(player, "audio-params/channel-count");
        Add(rows, "音频", "声道", channels > 0 ? $"{channels} 声道" : null);
        long sampleRate = Int(player, "audio-params/samplerate");
        Add(rows, "音频", "采样率", sampleRate > 0 ? $"{sampleRate} Hz" : null);
        Add(rows, "音频", "码率", Bitrate(Number(player, "audio-bitrate")));

        double position = Number(player, "time-pos");
        double duration = Number(player, "duration");
        Add(rows, "播放", "时间",
            duration > 0 ? $"{Clock(position)} / {Clock(duration)}" : null);
        double speed = Number(player, "speed");
        Add(rows, "播放", "速度", speed > 0 && Math.Abs(speed - 1) > 0.001
            ? $"{speed:0.##}×"
            : "1×");

        long dropped = Int(player, "vo-drop-frame-count");
        _lastStatsDroppedFrames = dropped;
        Add(rows, "播放", "丢帧", $"{dropped}");
        long decoderDropped = Int(player, "decoder-dropped");
        if (decoderDropped > 0)
        {
            Add(rows, "播放", "解码丢帧", decoderDropped.ToString(CultureInfo.InvariantCulture));
        }

        long mistimed = Int(player, "mistimed-frame-count");
        if (mistimed > 0)
        {
            Add(rows, "播放", "晚到帧", mistimed.ToString(CultureInfo.InvariantCulture));
        }

        double avsync = Number(player, "avsync");
        Add(rows, "播放", "音画同步", $"{avsync * 1000:0} ms");
        double cache = Number(player, "demuxer-cache-duration");
        Add(rows, "播放", "缓冲", cache > 0 ? $"{cache:0.0} 秒" : null);

        Add(rows, "渲染", "GPU API", Str(player, "gpu-api"));
        Add(rows, "渲染", "GPU 上下文", Str(player, "gpu-context"));
        double displayFps = Number(player, "display-fps");
        Add(rows, "渲染", "显示器刷新率", displayFps > 0 ? $"{displayFps:0.##} Hz" : null);
        Add(rows, "渲染", "视频输出", Str(player, "current-vo"));
        Add(rows, "渲染", "色彩矩阵", Str(player, "video-params/colormatrix"));
        Add(rows, "渲染", "色彩范围", Str(player, "video-params/colorlevels"));

        StatsPanel.SetRows(rows);
    }

    private static void Add(
        List<PlaybackStatRow> rows,
        string group,
        string label,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "—")
        {
            return;
        }

        rows.Add(new PlaybackStatRow(group, label, value));
    }

    private static string? Str(MpvPlayer player, string property)
    {
        try
        {
            string? value = player.GetPropertyString(property);
            return string.IsNullOrWhiteSpace(value) || value == "no" ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static long Int(MpvPlayer player, string property)
    {
        try
        {
            return player.GetPropertyInt64(property);
        }
        catch
        {
            return 0;
        }
    }

    private static double Number(MpvPlayer player, string property)
    {
        try
        {
            double value = player.GetPropertyDouble(property);
            return double.IsFinite(value) ? value : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string Size(long bytes) => bytes switch
    {
        <= 0 => "—",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    private static string Bitrate(double bitsPerSecond) => bitsPerSecond switch
    {
        <= 0 => "—",
        >= 1_000_000 => $"{bitsPerSecond / 1_000_000:0.00} Mbps",
        >= 1_000 => $"{bitsPerSecond / 1_000:0.0} kbps",
        _ => $"{bitsPerSecond:0} bps",
    };

    private static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            return "—";
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static string Fps(MpvPlayer player)
    {
        double container = Number(player, "container-fps");
        double estimated = Number(player, "estimated-vf-fps");
        double declared = Number(player, "fps");
        var parts = new List<string>(3);
        if (container > 0)
        {
            parts.Add($"容器 {container:0.###}");
        }

        if (declared > 0 && Math.Abs(declared - container) > 0.001)
        {
            parts.Add($"标称 {declared:0.###}");
        }

        if (estimated > 0)
        {
            parts.Add($"渲染 {estimated:0.##}");
        }

        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    private static string PixelAspect(MpvPlayer player)
    {
        double aspect = Number(player, "video-params/aspect");
        return aspect > 0 ? $" · {aspect:0.###}:1" : string.Empty;
    }

    private static string ColorInfo(MpvPlayer player)
    {
        string? primaries = Str(player, "video-params/primaries");
        string? gamma = Str(player, "video-params/gamma");
        string? levels = Str(player, "video-params/colorlevels");
        var parts = new[] { primaries, gamma, levels }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? "—" : string.Join(" · ", parts);
    }

    private static string Rotate(MpvPlayer player)
    {
        long rotate = Int(player, "video-rotate");
        return rotate == 0 ? "—" : $"{rotate}°";
    }
}
