using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Wiina.App.ViewModels;

namespace Wiina.App.Controls;

public sealed partial class OscToolbar : UserControl
{
    private OscViewModel? _viewModel;

    public OscToolbar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public event EventHandler? MoreClicked;

    public event EventHandler? PipClicked;

    public Microsoft.UI.Xaml.FrameworkElement MoreAnchor => MoreButton;

    private void OnMoreClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        MoreClicked?.Invoke(this, EventArgs.Empty);

    private void OnPipClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        PipClicked?.Invoke(this, EventArgs.Empty);

    private void OnDataContextChanged(object sender, Microsoft.UI.Xaml.DataContextChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnPropertyChanged;
        }

        _viewModel = e.NewValue as OscViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnPropertyChanged;
        }

        UpdateFullscreenGlyph();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OscViewModel.IsFullscreen))
        {
            UpdateFullscreenGlyph();
        }
    }

    private void UpdateFullscreenGlyph()
    {
        FullscreenButton.Content = _viewModel?.FullscreenGlyph ?? "\uE740";
    }
}
