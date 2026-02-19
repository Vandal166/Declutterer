using Avalonia.Controls;
using Avalonia.VisualTree;
using Declutterer.Abstractions;
using Declutterer.ViewModels;

namespace Declutterer.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is HistoryWindowViewModel viewModel)
        {
            // Load history when the view is shown
            _ = viewModel.LoadHistoryAsync();
            var window = this.GetVisualRoot() as Window;
            if (window != null)
            {
                viewModel.SetOwnerWindow(window);
            }
        }
    }
}
