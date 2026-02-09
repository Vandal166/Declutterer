using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Models;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Declutterer.Services;

public class ScorerOptions
{
    public double ScoreThreshold = 0.6;    // select nodes with combined score >= this

    // scoring config
    public double WeightAge { get; set; } = 0.5;
    public double WeightSize { get; set; } = 0.5;
    public ScoringMode AgeScoringMode { get; set; } = ScoringMode.Linear;       // Linear | Exponential
    public ScoringMode SizeScoringMode { get; set; } = ScoringMode.Linear;      // (not required but flexible)
    public double AgeExponentialDecay { get; set; } = 0.05;                     // for exponential mode
    public double SizeMaxObservedBytes { get; set; } = 0;                       // optional override; if zero, compute from tree

    public SelectionStrategy Strategy { get; set; } = SelectionStrategy.TopPercentage; // Threshold | TopN | TopPercentage
    public int TopN { get; set; } = 50;                                        // if using TopN
    public double TopPercentage { get; set; } = 0.1;                           // if using TopPercentage (10%)
}

public enum ScoringMode { Linear, Exponential }
public enum SelectionStrategy { Threshold, TopN, TopPercentage }

public class NodeSelectionScore 
{
    public required TreeNode Node { get; set; }
    public double AgeScore { get; set; }        // 0.0 - 1.0
    public double SizeScore { get; set; }       // 0.0 - 1.0
    public double CombinedScore { get; set; }   // weighted average 0.0 - 1.0
}

public sealed class SmartSelectionScorer
{
   // Public entry: compute scores for every node in treeRoot
   public List<NodeSelectionScore> ComputeScores(TreeNode root, ScanOptions scanOptions, ScorerOptions scorerOptions) 
   {
        // 1) gather normalization data (two-pass model)
        var stats = GatherTreeStats(root);
        if (scorerOptions.SizeMaxObservedBytes > 0) {
            stats.MaxSize = (long)Math.Max(stats.MaxSize, scorerOptions.SizeMaxObservedBytes);
        }

        // 2) traverse again and compute a score per node
        var results = new List<NodeSelectionScore>();
        Traverse(root, node => {
            var score = ScoreNode(node, scanOptions, scorerOptions, stats);
            results.Add(score);
        });

        return results;
    }

    // collect observed metrics necessary for normalization
    (long MaxSize, DateTime? OldestDate, DateTime? NewestDate) GatherTreeStats(TreeNode root) 
    {
        long maxSize = 0;
        DateTime? oldest = null;
        DateTime? newest = null;

        Traverse(root, node => {
            maxSize = Math.Max(maxSize, node.Size);
            var lm = node.LastModified;
            if (oldest == null || lm < oldest) oldest = lm;
            if (newest == null || lm > newest) newest = lm;
        });

        return (maxSize, oldest, newest);
    }

    // a simple tree traversal helper (depth-first)
    void Traverse(TreeNode node, Action<TreeNode> action) {
        action(node);
        if (node.Children != null) {
            foreach (var child in node.Children) Traverse(child, action);
        }
    }

    NodeSelectionScore ScoreNode(TreeNode node, ScanOptions scanOptions, ScorerOptions scorerOptions, (long MaxSize, DateTime? OldestDate, DateTime? NewestDate) stats) 
    {
        var ageScore = ComputeAgeScore(node, scanOptions, scorerOptions, stats);
        var sizeScore = ComputeSizeScore(node, scanOptions, scorerOptions, stats);

        // weighted average (normalize weights)
        double totalWeight = scorerOptions.WeightAge + scorerOptions.WeightSize;
        totalWeight = totalWeight <= 0 ? 1 : totalWeight;
        var combined = (ageScore * scorerOptions.WeightAge + sizeScore * scorerOptions.WeightSize) / totalWeight;

        return new NodeSelectionScore {
            Node = node,
            AgeScore = Clamp01(ageScore),
            SizeScore = Clamp01(sizeScore),
            CombinedScore = Clamp01(combined)
        };
    }

    static double ComputeAgeScore(TreeNode node, ScanOptions scanOptions, ScorerOptions scorerOptions, (long MaxSize, DateTime? OldestDate, DateTime? NewestDate) stats) {
        // If age filter is not enabled or node has no LastModified date, return neutral score
        if (!scanOptions.AgeFilter.UseModifiedDate || !node.LastModified.HasValue) 
            return 0.5;

        // Determine cutoff date from filter settings
        DateTime? cutoff = scanOptions.AgeFilter.ModifiedBefore;
        if (!cutoff.HasValue && scanOptions.AgeFilter.MonthsModifiedValue > 0) {
            cutoff = DateTime.UtcNow.AddMonths(-scanOptions.AgeFilter.MonthsModifiedValue);
        }
        if (!cutoff.HasValue) return 0.5; // neutral

        // compute how far node is beyond cutoff in days
        var deltaDays = (cutoff.Value - node.LastModified.Value).TotalDays; 
        // positive deltaDays => node is older than cutoff (worse/more eligible depending semantics)
        // choose semantic: we want higher score for nodes that are older than the threshold (i.e., match criteria)
        // So normalize based on deltaDays relative to a reasonable 'maxAgeSpan' (use stats.NewestDate - stats.OldestDate or fixed window)
        double maxSpanDays = (stats.NewestDate.HasValue && stats.OldestDate.HasValue)
            ? Math.Max((stats.NewestDate.Value - stats.OldestDate.Value).TotalDays, 1)
            : 365; // fallback

        // normalizedDistance = clamp(deltaDays / maxSpanDays) -> 0..1
        double normalized = Clamp01(deltaDays / maxSpanDays);

        if (scorerOptions.AgeScoringMode == ScoringMode.Linear) {
            // linear: older => higher score
            return normalized;
        } else {
            // exponential decay: stronger boost for very old items
            // formula: 1 - exp(-k * normalized * scale)
            double k = scorerOptions.AgeExponentialDecay; // small positive value; tuneable
            return 1.0 - Math.Exp(-k * normalized * 10.0); // scale *10 to make effect noticeable
        }
    }

    static double ComputeSizeScore(TreeNode node, ScanOptions scanOptions, ScorerOptions scorerOptions, (long MaxSize, DateTime? OldestDate, DateTime? NewestDate) stats) {
        // If size filter is not enabled, return neutral score
        if (!scanOptions.EntrySizeFilter.UseSizeFilter || scanOptions.EntrySizeFilter.SizeThreshold <= 0 || stats.MaxSize <= 0) 
            return 0.5;

        // Convert threshold from MB to bytes (same as ScanFilterService does)
        long thresholdBytes = scanOptions.EntrySizeFilter.SizeThreshold * 1024 * 1024;
        var maxSize = Math.Max(stats.MaxSize, thresholdBytes); // avoid division by zero
        double sizeValue = node.Size;

        // linear interpolation from threshold -> maxSize
        if (sizeValue <= thresholdBytes) return 0.0; // below threshold -> no match
        double normalized = (sizeValue - thresholdBytes) / (maxSize - thresholdBytes);
        normalized = Clamp01(normalized);

        if (scorerOptions.SizeScoringMode == ScoringMode.Linear) {
            return normalized;
        } else {
            // exponential: larger files get even higher score faster
            double k = 0.08; // configurable
            return 1.0 - Math.Exp(-k * normalized * 10.0);
        }
    }

    static double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));
}
public sealed class SmartSelectionService
{
    private readonly SmartSelectionScorer _scorer;

    public SmartSelectionService(SmartSelectionScorer scorer)
    {
        _scorer = scorer;
    }
    public List<TreeNode> Select(TreeNode root, ScanOptions scanOptions, ScorerOptions scorerOptions) 
    {
        var scored = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        // Filter out the root node - it should never be selected for deletion
        scored = scored.Where(s => s.Node != root).ToList();

        // sort by CombinedScore descending for strategy decisions
        var sorted = scored.OrderByDescending(s => s.CombinedScore).ToList();

        switch (scorerOptions.Strategy) {
            case SelectionStrategy.Threshold:
                return sorted
                    .Where(s => s.CombinedScore >= scorerOptions.ScoreThreshold)
                    .Select(s => s.Node)
                    .ToList();

            case SelectionStrategy.TopN:
                return sorted
                    .Take(scorerOptions.TopN)
                    .Select(s => s.Node)
                    .ToList();

            case SelectionStrategy.TopPercentage:
                int take = Math.Max(1, (int)(sorted.Count * scorerOptions.TopPercentage));
                return sorted
                    .Take(take)
                    .Select(s => s.Node)
                    .ToList();

            default:
                // fallback: threshold
                return sorted
                    .Where(s => s.CombinedScore >= scorerOptions.ScoreThreshold)
                    .Select(s => s.Node)
                    .ToList();
        }
    }

    // optional: expose scored results for UI diagnostics
    public List<NodeSelectionScore> ScoreWithDiagnostics(TreeNode root, ScanOptions scanOptions, ScorerOptions scorerOptions) {
        return _scorer.ComputeScores(root, scanOptions, scorerOptions);
    }
}
public sealed class DirectoryScanService
{
    private readonly ScanFilterService _scanFilterService;
    private readonly ILogger<DirectoryScanService> _logger;
    private readonly Lock _scanLock = new();
    
    public DirectoryScanService(ScanFilterService scanFilterService, ILogger<DirectoryScanService> logger)
    {
        _scanFilterService = scanFilterService;
        _logger = logger;
    }

    /// <summary>
    /// Creates the root TreeNode for the specified directory path.
    /// </summary>
    /// <param name="directoryPath">The path of the directory to create the root node for.</param>
    /// <returns>The root TreeNode representing the directory.</returns>
    public static TreeNode CreateRootNode(string directoryPath)
    {
        var dirInfo = new DirectoryInfo(directoryPath);
        var rootNode = new TreeNode
        {
            Name = dirInfo.Name,
            FullPath = dirInfo.FullName,
            IsDirectory = true,
            Size = CalculateDirectorySize(dirInfo),
            LastModified = dirInfo.LastWriteTime,
            Depth = 0, // we start at depth 0 for root nodes
            Parent = null, // w/o parent since this is a root node
            HasChildren = true
        };
        
        Log.Information("Created root node for directory: {DirectoryPath}", directoryPath);
        return rootNode;
    }
    
    public async Task<List<TreeNode>> LoadChildrenAsync(TreeNode node, ScanOptions? scanOptions)
    {
        var children = new List<TreeNode>();
        
        _logger.LogInformation("Loading children for node: {NodePath}", node.FullPath);
        
        try
        {
             // Run the directory scanning on a background thread to avoid blocking the UI
            await Task.Run(() =>
            {
                var filter = _scanFilterService.CreateFilter(scanOptions);
                
                var dirInfo = new DirectoryInfo(node.FullPath);
                
                LoadSubdirectories(node, dirInfo, filter, children);
                
                LoadFiles(node, dirInfo, filter, children);
            });
            
            _logger.LogInformation("Loaded {ChildrenCount} children for node: {NodePath}", children.Count, node.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading children for node: {NodePath}", node.FullPath);
        }
        
        return children;
    }
    
    private void LoadSubdirectories(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        // Get subdirectories
        try
        {
            foreach (var dir in dirInfo.GetDirectories())
            {
                try
                {
                    // Check if directory has any subdirectories or files but only if IncludeFiles is enabled
                    bool hasSubDirs = dir.GetDirectories().Length > 0 || (dir.GetFiles().Length > 0 /*&& (_currentScanOptions?.IncludeFiles == true)*/); //TODO this will be added later, for now we dont include files

                    //TODO:
                    // we coudl just change the filter so that it accepts only the necessary info instead of creating TreeNode first and then filtering,
                    // this way we can filter out more efficiently without creating TreeNode and calculating directory shit
                    var childNode = new TreeNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Size = CalculateDirectorySize(dir),
                        LastModified = dir.LastWriteTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        HasChildren = hasSubDirs,
                        IsSelected = parentNode.IsSelected // inherit selection state from parent
                    };
                            
                    // Apply filter(filtering out nodes that dont match the criteria)
                    if (filter != null && !filter(childNode))
                        continue;

                    children.Add(childNode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not access subdirectory: {DirectoryName}", dir.FullName);
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subdirectories from: {DirectoryPath}", dirInfo.FullName);
        }
        
        _logger.LogInformation("Loaded {ChildrenCount} subdirectories for node: {NodePath}", children.Count, parentNode.FullPath);
    }
    
    private void LoadFiles(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        // Get files if IncludeFiles is enabled
        //if (_currentScanOptions?.IncludeFiles == true) //TODO this will be added later, for now we dont include files
        try
        {
            foreach (var file in dirInfo.GetFiles()) // getting files in the current directory we are in(only the current one, not recursive so we dont get too many files)
            {
                try
                {
                    var childNode = new TreeNode
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        IsSelected = parentNode.IsSelected // inherit selection state from parent
                    };

                    if (filter != null && !filter(childNode))
                        continue;

                    children.Add(childNode);
                }
                catch(Exception ex)
                {
                    _logger.LogWarning(ex, "Could not access file: {FileName}", file.FullName);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Error reading files from: {DirectoryPath}", dirInfo.FullName);
        }
    }
    

    /// <summary>
    /// Parallelized approach for loading subdirectories from a single directory.
    /// Processes directory entries concurrently across CPU cores.
    /// </summary>
    private void LoadSubdirectoriesParallel(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        try
        {
            var directories = dirInfo.GetDirectories();
            var childNodes = new List<TreeNode>();
            
            Parallel.ForEach(directories, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, dir =>
            {
                try
                {
                    bool hasSubDirs = dir.GetDirectories().Length > 0 || (dir.GetFiles().Length > 0);

                    var childNode = new TreeNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Size = CalculateDirectorySize(dir),
                        LastModified = dir.LastWriteTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        HasChildren = hasSubDirs,
                        IsSelected = parentNode.IsSelected // inherit selection state from parent
                    };
                    
                    if (filter != null && !filter(childNode))
                        return;

                    using (_scanLock.EnterScope())
                    {
                        childNodes.Add(childNode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not access subdirectory: {DirectoryName}", dir.FullName);
                }
            });
            
            children.AddRange(childNodes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subdirectories from: {DirectoryPath}", dirInfo.FullName);
        }
        
        _logger.LogInformation("Loaded {ChildrenCount} subdirectories for node: {NodePath}", children.Count, parentNode.FullPath);
    }

    /// <summary>
    /// Asynchronously loads children for multiple root directories in parallel.
    /// This is optimized for the initial scan where multiple directories are processed concurrently.
    /// Returns a dictionary mapping each root node to its children.
    /// </summary>
    public async Task<Dictionary<TreeNode, List<TreeNode>>> LoadChildrenForMultipleRootsAsync(IEnumerable<TreeNode> rootNodes, ScanOptions? scanOptions)
    {
        var childrenByRoot = new Dictionary<TreeNode, List<TreeNode>>();
        
        return await Task.Run(() =>
        {
            var filter = _scanFilterService.CreateFilter(scanOptions);
            
            Parallel.ForEach(rootNodes, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, rootNode =>
            {
                try
                {
                    var dirInfo = new DirectoryInfo(rootNode.FullPath);
                    var children = new List<TreeNode>();
                    
                    LoadSubdirectoriesParallel(rootNode, dirInfo, filter, children);
                    LoadFiles(rootNode, dirInfo, filter, children);
                    
                    using (_scanLock.EnterScope())
                    {
                        childrenByRoot[rootNode] = children;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we don't have access to
                    using (_scanLock.EnterScope())
                    {
                        childrenByRoot[rootNode] = new List<TreeNode>(); // set empty list for roots we cant access
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading children for root: {NodePath}", rootNode.FullPath);
                    using (_scanLock.EnterScope())
                    {
                        childrenByRoot[rootNode] = new List<TreeNode>();
                    }
                }
            });
            
            return childrenByRoot;
        });
    }
    
    
    // Recursively calculates the size of a directory and returns it in bytes
    private static long CalculateDirectorySize(DirectoryInfo dir) 
    {    
        long size = 0;    
        // Add file sizes.
        FileInfo[] fis = dir.GetFiles();
        foreach (FileInfo fi in fis) 
        {      
            size += fi.Length;    
        }
        // Add subdirectory sizes.
        DirectoryInfo[] dis = dir.GetDirectories();
        foreach (DirectoryInfo di in dis) 
        {
            size += CalculateDirectorySize(di);   
        }
        return size;  
    }
}