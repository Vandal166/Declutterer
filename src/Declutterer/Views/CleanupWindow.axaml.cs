using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Declutterer.ViewModels;

namespace Declutterer.Views;

public partial class CleanupWindow : Window
{
    public CleanupWindow()
    {
        InitializeComponent();
        
        // Set up the ViewModel with the TopLevel for folder picker
        if (DataContext is CleanupWindowViewModel viewModel)
        {
            viewModel.SetTopLevel(this);
        }
    }
}