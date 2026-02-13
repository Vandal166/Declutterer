using Avalonia.Controls;
using Avalonia.Data;
using Declutterer.Models;

namespace Declutterer.Factories;

public static class NameWithIconBehavior
{
    /// <summary>
    /// Creates a StackPanel with icon and name bindings for a TreeNode.
    /// </summary>
    /// <returns>A StackPanel configured with icon and name bindings</returns>
    public static void AttachToNode(TreeNode node, StackPanel panel, Image image, TextBlock textBlock)
    {
        panel.DataContext = node;
        
        image.Bind(Image.SourceProperty, new Binding("Icon"));
        
        panel.Children.Add(image);
        
        textBlock.Bind(TextBlock.TextProperty, new Binding("Name"));
        panel.Children.Add(textBlock);
    }
}
