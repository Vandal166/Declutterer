using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Declutterer.ViewModels;

namespace Declutterer.Views;

public partial class ScanOptionsWindow : Window
{
    public ScanOptionsWindow()
    {
        InitializeComponent();

        // DataContext = new ScanOptionsWindowViewModel();
        // if (DataContext is ScanOptionsWindowViewModel vm)
        // {
        //     vm.RequestClose -= Close; // unsubbing to avoid multiple subscriptions if DataContext is set multiple times, which can lead to multiple Close calls
        //     vm.RequestClose += Close; // this will just call the internal Close method of the Window, passing the ScanOptions as the parameter which will be available to the caller of ShowDialog
        // }
    }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ScanOptionsWindowViewModel viewModel)
        {
            viewModel.SetTopLevel(TopLevel.GetTopLevel(this));
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