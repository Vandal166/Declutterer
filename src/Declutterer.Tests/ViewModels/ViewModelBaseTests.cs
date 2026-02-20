using Declutterer.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Declutterer.Tests.ViewModels;

public class ViewModelBaseTests
{
    [Fact]
    public void ViewModelBase_InheritsFromObservableObject()
    {
        var testViewModel = new TestViewModel();
        
        Assert.IsAssignableFrom<ObservableObject>(testViewModel);
    }

    [Fact]
    public void ViewModelBase_SupportsPropertyChangeNotification()
    {
        var testViewModel = new TestViewModel();
        var propertyChangedRaised = false;
        
        testViewModel.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(TestViewModel.TestProperty))
            {
                propertyChangedRaised = true;
            }
        };
        
        testViewModel.TestProperty = "NewValue";
        
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void ViewModelBase_CanBeInstantiatedThroughDerivedClass()
    {
        var testViewModel = new TestViewModel();
        
        Assert.NotNull(testViewModel);
        Assert.IsType<TestViewModel>(testViewModel);
    }

    // Test implementation of ViewModelBase for testing purposes
    private class TestViewModel : ViewModelBase
    {
        private string _testProperty = string.Empty;

        public string TestProperty
        {
            get => _testProperty;
            set => SetProperty(ref _testProperty, value);
        }
    }
}
