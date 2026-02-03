using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Models;

namespace Declutterer.ViewModels;

public partial class ScanOptionsWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private ScanOptions scanOptions = new();
}