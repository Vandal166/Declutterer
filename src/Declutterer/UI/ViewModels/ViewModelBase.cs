using CommunityToolkit.Mvvm.ComponentModel;

namespace Declutterer.UI.ViewModels;

/// <summary>
/// Empty marker base class for all ViewModels so that the ViewLocator can easily identify them and not accidentally match other ObservableObjects (like Models).
/// </summary>
public abstract class ViewModelBase : ObservableObject;