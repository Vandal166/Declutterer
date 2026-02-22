using System;
using Avalonia.Controls;
using Declutterer.UI.ViewModels;
using Declutterer.Utilities.Extensions;

namespace Declutterer.UI.Views;

public partial class ScanOptionsWindow : Window
{
    public ScanOptionsWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FitToScreen(0.30, 0.80);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ScanOptionsWindowViewModel viewModel)
        {
            viewModel.SetTopLevel(GetTopLevel(this));
            viewModel.RequestClose += (result) =>
            {
                Close(result);
            };
        }
    }
    protected override void OnClosed(EventArgs e)
    {
        if(DataContext is ScanOptionsWindowViewModel vm)
        {
            vm.RequestClose -= Close; // Unsubscribe to prevent memory leaks
        }
        base.OnClosed(e);
    }
}