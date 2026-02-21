using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Serilog;
using HistoryWindowViewModel = Declutterer.UI.ViewModels.HistoryWindowViewModel;

namespace Declutterer.UI.Views;

public partial class HistoryView : UserControl
{
    private bool _previouslyVisible;

    public HistoryView()
    {
        InitializeComponent();
    }
    
    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is HistoryWindowViewModel viewModel)
        {
            // Load history when the view is first loaded
            _ = viewModel.LoadHistoryAsync();
            var window = this.GetVisualRoot() as Window;
            if (window != null)
            {
                viewModel.SetOwnerWindow(window);
            }

            // Subscribe to parent Panel's IsVisible changes
            var parent = this.Parent as Panel;
            if(parent is null)
                Log.Error("HistoryView is not inside a Panel. Parent type: {ParentType}", this.Parent?.GetType().FullName);
            
            parent?.GetObservable(IsVisibleProperty)
                .Subscribe(isVisible =>
                {
                    // Reload history each time the parent becomes visible
                    if (isVisible && !_previouslyVisible && DataContext is HistoryWindowViewModel vm)
                    {
                        _ = vm.LoadHistoryAsync();
                    }
                    _previouslyVisible = isVisible;
                });
        }
    }
}
