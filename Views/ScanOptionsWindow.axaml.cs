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

        DataContext = new ScanOptionsWindowViewModel();
    }
}