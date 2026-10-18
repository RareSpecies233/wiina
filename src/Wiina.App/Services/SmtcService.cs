using Microsoft.UI.Xaml;
using Windows.Media;
using Windows.Media.Playback;

namespace Wiina.App.Services;

/// <summary>
/// System media keys / transport controls (play, pause, stop, previous, next).
/// Unpackaged WinUI apps obtain the per-window instance through
/// <c>GetForWindow</c>; every call is guarded because some systems disable it.
/// </summary>
public sealed class SmtcService : IDisposable
{
    private SystemMediaTransportControls? _controls;
    private MediaPlayer? _mediaPlayer;
    private bool _initialized;

    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? StopRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;

    public bool Initialize(Window window)
    {
        if (_initialized)
        {
            return _controls is not null;
        }

        _initialized = true;
        _ = window;
        try
        {
            // A silent MediaPlayer gives us the desktop-friendly SMTC surface
            // (SystemMediaTransportControls.GetForCurrentView is UWP-only).
            _mediaPlayer = new MediaPlayer();
            _controls = _mediaPlayer.SystemMediaTransportControls;
            _controls.IsEnabled = true;
            _controls.IsPlayEnabled = true;
            _controls.IsPauseEnabled = true;
            _controls.IsStopEnabled = true;
            _controls.IsNextEnabled = true;
            _controls.IsPreviousEnabled = true;
            _controls.PlaybackStatus = MediaPlaybackStatus.Stopped;
            _controls.ButtonPressed += OnButtonPressed;
            return true;
        }
        catch
        {
            _controls = null;
            _mediaPlayer?.Dispose();
            _mediaPlayer = null;
            return false;
        }
    }

    public void Update(bool hasMedia, bool isPlaying, string? title, double duration)
    {
        if (_controls is null)
        {
            return;
        }

        try
        {
            _controls.PlaybackStatus = !hasMedia
                ? MediaPlaybackStatus.Stopped
                : isPlaying
                    ? MediaPlaybackStatus.Playing
                    : MediaPlaybackStatus.Paused;
            _controls.IsNextEnabled = hasMedia;
            _controls.IsPreviousEnabled = hasMedia;

            if (!string.IsNullOrWhiteSpace(title))
            {
                _controls.DisplayUpdater.Type = MediaPlaybackType.Video;
                _controls.DisplayUpdater.VideoProperties.Title = title;
                if (duration > 0)
                {
                    _controls.DisplayUpdater.VideoProperties.Subtitle =
                        TimeSpan.FromSeconds(duration).ToString(@"h\:mm\:ss");
                }

                _controls.DisplayUpdater.Update();
            }
            else
            {
                _controls.DisplayUpdater.Update();
            }
        }
        catch
        {
            // Media integration must never affect playback.
        }
    }

    private void OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                PlayRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Pause:
                PauseRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Stop:
                StopRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Next:
                NextRequested?.Invoke();
                break;
            case SystemMediaTransportControlsButton.Previous:
                PreviousRequested?.Invoke();
                break;
        }
    }

    public void Dispose()
    {
        if (_controls is null)
        {
            return;
        }

        try
        {
            _controls.ButtonPressed -= OnButtonPressed;
            _controls.IsEnabled = false;
        }
        catch
        {
            // Window is going away.
        }

        _controls = null;
        _mediaPlayer?.Dispose();
        _mediaPlayer = null;
    }
}
