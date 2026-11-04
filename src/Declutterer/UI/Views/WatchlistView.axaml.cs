using Avalonia.Controls;
using Declutterer.UI.ViewModels;

namespace Declutterer.UI.Views;

public partial class WatchlistView : UserControl
{
    public WatchlistView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is WatchlistViewModel viewModel)
        {
            // initial load; the main window reloads the page every time it is shown
            _ = viewModel.LoadAsync();
        }
    }
}
