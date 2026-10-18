using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Wiina.Core.Text;

namespace Wiina.App.ViewModels;

/// <summary>
/// State shared by the floating, bottom and top OSC. One instance per player.
/// Player updates use the Set*FromPlayer methods so they never echo back a
/// seek/volume command; user input raises the *Requested events instead.
/// </summary>
public sealed class OscViewModel : INotifyPropertyChanged
{
    private double _position;
    private double _duration;
    private double _volume = 100;
    private double _bufferEnd;
    private double _speed = 1;
    private double? _abLoopA;
    private double? _abLoopB;
    private bool _isPlaying;
    private bool _isMuted;
    private bool _isFullscreen;
    private bool _isScrubbing;
    private bool _suppress;
    private IReadOnlyList<double> _chapters = Array.Empty<double>();

    public event Action<double>? SeekRequested;
    public event Action<double>? VolumeRequested;
    public event Action? PlayPauseRequested;
    public event Action? MuteRequested;
    public event Action? PreviousRequested;
    public event Action? NextRequested;
    public event Action? FullscreenRequested;
    public event Action? SettingsRequested;
    public event Action? PlaylistRequested;
    public event Action? SeekStarted;
    public event Action? SeekCompleted;

    public ICommand PlayPauseCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand FullscreenCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand PlaylistCommand { get; }
    public ICommand SeekCommand { get; }
    public ICommand VolumeCommand { get; }

    public OscViewModel()
    {
        PlayPauseCommand = new RelayCommand(() => PlayPauseRequested?.Invoke());
        MuteCommand = new RelayCommand(() => MuteRequested?.Invoke());
        PreviousCommand = new RelayCommand(() => PreviousRequested?.Invoke());
        NextCommand = new RelayCommand(() => NextRequested?.Invoke());
        FullscreenCommand = new RelayCommand(() => FullscreenRequested?.Invoke());
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
        PlaylistCommand = new RelayCommand(() => PlaylistRequested?.Invoke());
        SeekCommand = new RelayCommand<double>(RequestSeek);
        VolumeCommand = new RelayCommand<double>(RequestVolume);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Position
    {
        get => _position;
        set
        {
            double clamped = Math.Max(0, value);
            if (!Set(ref _position, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(CurrentTimeText));
            OnPropertyChanged(nameof(ProgressRatio));
            if (!_suppress)
            {
                SeekRequested?.Invoke(clamped);
            }
        }
    }

    public double Duration
    {
        get => _duration;
        set
        {
            if (Set(ref _duration, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(DurationText));
                OnPropertyChanged(nameof(ProgressRatio));
                OnPropertyChanged(nameof(BufferRatio));
            }
        }
    }

    public double BufferEnd
    {
        get => _bufferEnd;
        set
        {
            if (Set(ref _bufferEnd, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(BufferRatio));
            }
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            double clamped = Math.Clamp(value, 0, 130);
            if (!Set(ref _volume, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(VolumeGlyph));
            OnPropertyChanged(nameof(VolumeText));
            OnPropertyChanged(nameof(IsOverAmplified));
            if (!_suppress)
            {
                VolumeRequested?.Invoke(clamped);
            }
        }
    }

    public double Speed
    {
        get => _speed;
        set
        {
            if (Set(ref _speed, Math.Max(0.05, value)))
            {
                OnPropertyChanged(nameof(SpeedText));
                OnPropertyChanged(nameof(ShowSpeed));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (Set(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayGlyph));
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (Set(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(VolumeGlyph));
            }
        }
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set
        {
            if (Set(ref _isFullscreen, value))
            {
                OnPropertyChanged(nameof(FullscreenGlyph));
            }
        }
    }

    public bool IsScrubbing
    {
        get => _isScrubbing;
        private set => Set(ref _isScrubbing, value);
    }

    public double? AbLoopA
    {
        get => _abLoopA;
        set
        {
            if (Set(ref _abLoopA, value))
            {
                OnPropertyChanged(nameof(HasAbLoop));
            }
        }
    }

    public double? AbLoopB
    {
        get => _abLoopB;
        set
        {
            if (Set(ref _abLoopB, value))
            {
                OnPropertyChanged(nameof(HasAbLoop));
            }
        }
    }

    public IReadOnlyList<double> Chapters
    {
        get => _chapters;
        set
        {
            if (!Set(ref _chapters, value ?? Array.Empty<double>()))
            {
                return;
            }

            OnPropertyChanged(nameof(HasChapters));
        }
    }

    public bool HasAbLoop => AbLoopA is > 0 || AbLoopB is > 0;

    public bool HasChapters => Chapters.Count > 0;

    public bool IsOverAmplified => Volume > 100;

    public bool ShowSpeed => Math.Abs(Speed - 1.0) > 0.001;

    public string CurrentTimeText => TimeFormatter.Format(Position);

    public string DurationText => TimeFormatter.Format(Duration);

    public string PlayGlyph => IsPlaying ? "\uE769" : "\uE768";

    public string FullscreenGlyph => IsFullscreen ? "\uE73F" : "\uE740";

    public string SpeedText => ShowSpeed
        ? Speed.ToString("0.##", CultureInfo.InvariantCulture) + "x"
        : string.Empty;

    /// <summary>IINA thresholds: muted/0, 1–33, 34–66, 67+.</summary>
    public string VolumeGlyph
    {
        get
        {
            if (IsMuted || Volume < 1)
            {
                return "\uE74F";
            }

            return Volume < 34 ? "\uE993" : Volume < 67 ? "\uE994" : "\uE995";
        }
    }

    public string VolumeText => $"音量 {Math.Round(Volume)}%";

    public double ProgressRatio => Duration <= 0 ? 0 : Math.Clamp(Position / Duration, 0, 1);

    public double BufferRatio => Duration <= 0 ? 0 : Math.Clamp(BufferEnd / Duration, 0, 1);

    public void RequestSeek(double seconds) => SeekRequested?.Invoke(Math.Max(0, seconds));

    public void RequestVolume(double volume) =>
        VolumeRequested?.Invoke(Math.Clamp(volume, 0, 130));

    public void BeginScrub()
    {
        IsScrubbing = true;
        SeekStarted?.Invoke();
    }

    public void EndScrub()
    {
        IsScrubbing = false;
        SeekCompleted?.Invoke();
    }

    public void SetPositionFromPlayer(double value) => SetWithoutEcho(() => Position = value);

    public void SetVolumeFromPlayer(double value) => SetWithoutEcho(() => Volume = value);

    public void SetDurationFromPlayer(double value) => Duration = value;

    public void SetBufferFromPlayer(double value) => BufferEnd = value;

    public void SetPlayingFromPlayer(bool value) => IsPlaying = value;

    public void SetMutedFromPlayer(bool value) => IsMuted = value;

    public void SetSpeedFromPlayer(double value) => Speed = value;

    private void SetWithoutEcho(Action action)
    {
        bool previous = _suppress;
        _suppress = true;
        try
        {
            action();
        }
        finally
        {
            _suppress = previous;
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
