using System;
using System.Collections.Generic;
using System.IO;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>
/// Immutable copy of the parts of a <see cref="TreeNode"/> the engine needs. Taking it on the UI thread lets the
/// (long running) analysis run on a background thread without racing against lazy loading / expanding of the tree.
/// </summary>
public sealed class NodeSnapshot
{
    public TreeNode? Source { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime? LastWriteUtc { get; init; }
    public DateTime? LastAccessUtc { get; init; }
    public int Depth { get; init; }

    /// <summary>The directory has content that is not loaded into the tree yet (lazy loading).</summary>
    public bool HasUnloadedChildren { get; init; }
    public NodeSnapshot? Parent { get; init; }
    public List<NodeSnapshot> Children { get; } = new();

    public bool IsRoot => Parent is null;

    /// <summary>Recursively snapshots a node and all of its currently loaded children. Call on the UI thread.</summary>
    public static NodeSnapshot FromTreeNode(TreeNode node, NodeSnapshot? parent = null)
    {
        var name = string.IsNullOrEmpty(node.Name)
            ? System.IO.Path.GetFileName(PathKey.Normalize(node.FullPath))
            : node.Name;

        var snap = new NodeSnapshot
        {
            Source = node,
            Path = node.FullPath,
            Name = name,
            IsDirectory = node.IsDirectory,
            Size = node.Size,
            LastWriteUtc = node.LastModified?.ToUniversalTime(),
            LastAccessUtc = node.LastAccessed?.ToUniversalTime(),
            Depth = parent is null ? 0 : parent.Depth + 1,
            Parent = parent,
            HasUnloadedChildren = node is { IsDirectory: true, HasChildren: true, Children.Count: 0 },
        };

        foreach (var child in node.Children)
            snap.Children.Add(FromTreeNode(child, snap));

        return snap;
    }

    /// <summary>Depth-first enumeration of this node and all descendants.</summary>
    public IEnumerable<NodeSnapshot> SelfAndDescendants()
    {
        var stack = new Stack<NodeSnapshot>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            for (int i = n.Children.Count - 1; i >= 0; i--)
                stack.Push(n.Children[i]);
        }
    }
}
