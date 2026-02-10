using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Declutterer.Models;

namespace Declutterer.Common;

public static class TreeNodeHelper
{
    /// <summary>
    /// Filters out items whose paths are nested within other items in the list.
    /// This prevents double-counting when both a parent directory and its children are selected.
    /// For example, if both A/ and A/B/ are in the list, only A/ is returned since deleting A/ will also delete A/B/.
    /// </summary>
    public static List<TreeNode> GetTopLevelItems(IEnumerable<TreeNode> items)
    {
        var itemsList = items.ToList();
        
        if (itemsList.Count <= 1)
            return itemsList;
        
        var result = new List<TreeNode>();
        
        // Sort by path length (shortest first) so parents come before children
        var sortedByPath = itemsList.OrderBy(i => i.FullPath.Length).ToList();
        var includedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var item in sortedByPath)
        {
            var normalizedPath = item.FullPath.NormalizePath();
            
            // Check if this item's path is nested within any already-included path
            bool isNested = includedPaths.Any(existingPath => 
                normalizedPath.StartsWith(existingPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            
            if (!isNested)
            {
                result.Add(item);
                includedPaths.Add(normalizedPath);
            }
        }
        
        return result;
    }
}
