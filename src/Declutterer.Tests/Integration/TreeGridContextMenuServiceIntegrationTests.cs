using System;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Declutterer.Abstractions;
using Declutterer.Models;
using Declutterer.Services;
using NSubstitute;

namespace Declutterer.Tests.Integration;

public class TreeGridContextMenuServiceIntegrationTests
{
    private readonly TreeGridContextMenuService _contextMenuService;
    private readonly IExplorerLauncher _explorerLauncher;
    private readonly IErrorDialogService _errorDialogService;
    private readonly IClipboard _clipboard;
    private readonly IClipboardService _clipboardService;
    
    public TreeGridContextMenuServiceIntegrationTests()
    {
        _explorerLauncher = Substitute.For<IExplorerLauncher>();
        _errorDialogService = Substitute.For<IErrorDialogService>();
        _clipboard = Substitute.For<IClipboard>();
        _clipboardService = Substitute.For<IClipboardService>();
        _contextMenuService = new TreeGridContextMenuService(_explorerLauncher, _errorDialogService);
    }

    [Fact]
    public void ToggleNodeSelection_EnabledNode_TogglesCorrectly()
    {
        var node = new TreeNode 
        { 
            Name = "test", 
            FullPath = "/test",
            IsCheckboxEnabled = true,
            IsCheckboxSelected = false,
            Depth = 1
        };

        _contextMenuService.ToggleNodeSelection(node);

        Assert.True(node.IsCheckboxSelected);
        
        _contextMenuService.ToggleNodeSelection(node);

        Assert.False(node.IsCheckboxSelected);
    }

    [Fact]
    public void ToggleNodeSelection_DisabledNode_DoesNotToggle()
    {
        var node = new TreeNode 
        { 
            Name = "test", 
            FullPath = "/test",
            IsCheckboxEnabled = false,
            IsCheckboxSelected = false,
            Depth = 1
        };

        _contextMenuService.ToggleNodeSelection(node);

        Assert.False(node.IsCheckboxSelected);
    }

    [Fact]
    public void ToggleNodeSelection_RootNode_DoesNotToggle()
    {
        var node = new TreeNode 
        { 
            Name = "root", 
            FullPath = "/root",
            IsCheckboxEnabled = true,
            IsCheckboxSelected = false,
            Depth = 0
        };

        _contextMenuService.ToggleNodeSelection(node);

        Assert.False(node.IsCheckboxSelected);
    }

    [Fact]
    public void ToggleNodeSelection_NullNode_DoesNotThrow()
    {
        var exception = Record.Exception(() => _contextMenuService.ToggleNodeSelection(null));

        Assert.Null(exception);
    }

    [Fact]
    public async Task OpenInExplorerAsync_ValidNode_CallsExplorerLauncher()
    {
        var node = new TreeNode 
        { 
            Name = "test", 
            FullPath = "/test/path"
        };

        await _contextMenuService.OpenInExplorerAsync(node);

        _explorerLauncher.Received(1).OpenInExplorer("/test/path");
        await _errorDialogService.DidNotReceive().ShowErrorAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>());
    }

    [Fact]
    public async Task OpenInExplorerAsync_NullNode_DoesNotCallExplorerLauncher()
    {
        await _contextMenuService.OpenInExplorerAsync(null);

        _explorerLauncher.DidNotReceive().OpenInExplorer(Arg.Any<string>());
    }

    [Fact]
    public async Task OpenInExplorerAsync_ExplorerLauncherThrows_ShowsErrorDialog()
    {
        var node = new TreeNode 
        { 
            Name = "test", 
            FullPath = "/test/path"
        };
        var exception = new InvalidOperationException("Failed to launch");
        _explorerLauncher.When(x => x.OpenInExplorer(Arg.Any<string>()))
            .Do(x => throw exception);

        await _contextMenuService.OpenInExplorerAsync(node);

        await _errorDialogService.Received(1).ShowErrorAsync(
            "Failed to Open in Explorer",
            Arg.Is<string>(s => s.Contains("/test/path")),
            exception);
    }

    [Fact]
    public async Task CopyPathToClipboardAsync_ValidNode_CopiesPath()
    {
        var node = new TreeNode 
        { 
            Name = "test", 
            FullPath = "/test/path"
        };

        await _clipboardService.CopyTextAsync(node.FullPath);

        await _clipboard.Received(1).SetTextAsync("/test/path");
        await _errorDialogService.DidNotReceive().ShowErrorAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Exception>());
    }

    [Fact]
    public async Task CopyPathToClipboardAsync_NullNode_DoesNotCopy()
    {
        await _clipboardService.CopyTextAsync(null);

        await _clipboard.DidNotReceive().SetTextAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task CopyPathToClipboardAsync_NullClipboard_DoesNotThrow()
    {
        var node = new TreeNode 
        { 
            Name = "test", 
            FullPath = "/test/path"
        };

        var exception = await Record.ExceptionAsync(async () => 
            await _clipboardService.CopyTextAsync(node.FullPath));

        Assert.Null(exception);
    }

    [Fact]
    public async Task CopyPathToClipboardAsync_ClipboardThrows_ShowsErrorDialog()
    {
        var node = new TreeNode 
        { 
            Name = "test", 
            FullPath = "/test/path"
        };
        var exception = new InvalidOperationException("Clipboard error");
        _clipboard.SetTextAsync(Arg.Any<string>()).Returns(Task.FromException(exception));

        await _clipboardService.CopyTextAsync(node.FullPath);

        await _errorDialogService.Received(1).ShowErrorAsync(
            "Failed to Copy Path",
            Arg.Is<string>(s => s.Contains("/test/path")),
            Arg.Any<Exception>());
    }

    [Fact]
    public void Integration_MultipleToggleOperations_WorkCorrectly()
    {
        var node1 = new TreeNode { Name = "node1", FullPath = "/node1", IsCheckboxEnabled = true, Depth = 1 };
        var node2 = new TreeNode { Name = "node2", FullPath = "/node2", IsCheckboxEnabled = true, Depth = 1 };
        var node3 = new TreeNode { Name = "node3", FullPath = "/node3", IsCheckboxEnabled = false, Depth = 1 };

        _contextMenuService.ToggleNodeSelection(node1);
        _contextMenuService.ToggleNodeSelection(node2);
        _contextMenuService.ToggleNodeSelection(node3);

        Assert.True(node1.IsCheckboxSelected);
        Assert.True(node2.IsCheckboxSelected);
        Assert.False(node3.IsCheckboxSelected); // Disabled, should not toggle

        _contextMenuService.ToggleNodeSelection(node1);

        Assert.False(node1.IsCheckboxSelected);
        Assert.True(node2.IsCheckboxSelected);
    }

    [Fact]
    public async Task Integration_ErrorHandling_MultipleOperations()
    {
        var node = new TreeNode { Name = "test", FullPath = "/test" };
        var exception1 = new InvalidOperationException("Explorer error");
        var exception2 = new InvalidOperationException("Clipboard error");

        _explorerLauncher.When(x => x.OpenInExplorer(Arg.Any<string>()))
            .Do(x => throw exception1);
        _clipboard.SetTextAsync(Arg.Any<string>()).Returns(Task.FromException(exception2));

        await _contextMenuService.OpenInExplorerAsync(node);
        await _clipboardService.CopyTextAsync(node.FullPath);

        await _errorDialogService.Received(1).ShowErrorAsync(
            "Failed to Open in Explorer", Arg.Any<string>(), exception1);
        await _errorDialogService.Received(1).ShowErrorAsync(
            "Failed to Copy Path", Arg.Any<string>(), Arg.Any<Exception>());
    }

    [Fact]
    public async Task Integration_RealWorldScenario_ContextMenuWorkflow()
    {
        // Simulate user right-clicking on a node and performing various actions
        var fileNode = new TreeNode 
        { 
            Name = "document.pdf", 
            FullPath = "/documents/document.pdf",
            IsCheckboxEnabled = true,
            IsCheckboxSelected = false,
            Depth = 2
        };

        // User toggles selection
        _contextMenuService.ToggleNodeSelection(fileNode);
        Assert.True(fileNode.IsCheckboxSelected);

        // User opens in explorer
        await _contextMenuService.OpenInExplorerAsync(fileNode);
        _explorerLauncher.Received(1).OpenInExplorer("/documents/document.pdf");

        // User copies path to clipboard
        await _clipboardService.CopyTextAsync(fileNode.FullPath);
        await _clipboard.Received(1).SetTextAsync("/documents/document.pdf");

        // User deselects
        _contextMenuService.ToggleNodeSelection(fileNode);
        Assert.False(fileNode.IsCheckboxSelected);
    }

    [Fact]
    public void Integration_HierarchicalNodes_SelectionPropagation()
    {
        var parent = new TreeNode 
        { 
            Name = "parent", 
            FullPath = "/parent",
            IsCheckboxEnabled = true,
            IsCheckboxSelected = false,
            Depth = 1
        };
        var child1 = new TreeNode 
        { 
            Name = "child1", 
            FullPath = "/parent/child1",
            IsCheckboxEnabled = true,
            IsCheckboxSelected = false,
            Depth = 2,
            Parent = parent
        };
        var child2 = new TreeNode 
        { 
            Name = "child2", 
            FullPath = "/parent/child2",
            IsCheckboxEnabled = true,
            IsCheckboxSelected = false,
            Depth = 2,
            Parent = parent
        };

        parent.Children.Add(child1);
        parent.Children.Add(child2);

        // Toggle parent
        _contextMenuService.ToggleNodeSelection(parent);
        Assert.True(parent.IsCheckboxSelected);

        // Children can still be toggled independently (this service doesn't implement cascading)
        _contextMenuService.ToggleNodeSelection(child1);
        Assert.True(child1.IsCheckboxSelected);
    }

    [Fact]
    public async Task Integration_WithMultipleClipboardOperations_AllSucceed()
    {
        var nodes = new[]
        {
            new TreeNode { Name = "file1.txt", FullPath = "/path/file1.txt" },
            new TreeNode { Name = "file2.txt", FullPath = "/path/file2.txt" },
            new TreeNode { Name = "file3.txt", FullPath = "/path/file3.txt" }
        };

        foreach (var node in nodes)
        {
            await _clipboardService.CopyTextAsync(node.FullPath);
        }

        await _clipboard.Received(1).SetTextAsync("/path/file1.txt");
        await _clipboard.Received(1).SetTextAsync("/path/file2.txt");
        await _clipboard.Received(1).SetTextAsync("/path/file3.txt");
    }
}
