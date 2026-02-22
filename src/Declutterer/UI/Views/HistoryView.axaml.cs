using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Declutterer.UI.ViewModels;
using Serilog;

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
            // Initial load of history and setting owner window
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
                    // reloading history and setting owner window when the view becomes visible again
                    if (isVisible && !_previouslyVisible && DataContext is HistoryWindowViewModel vm)
                    {
                        if (window != null)
                        {
                            viewModel.SetOwnerWindow(window);
                        }
                        _ = vm.LoadHistoryAsync();
                    }
                    _previouslyVisible = isVisible;
                });
        }
    }
}
