using BenchmarkDotNet.Attributes;
using Declutterer.Benchmark.Models;
using Declutterer.Benchmark.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Declutterer.Benchmark;

/// <summary>
/// Benchmark comparing DirectoryEnumerator vs GetDirectories() approaches
/// for scanning directory structures.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class DirectoryScanBenchmark
{
    // Set the root path you want to benchmark
    private const string ROOT_PATH = "C:\\Users\\Kamilos\\Downloads";
    private DirectoryScanService _scanServiceWithEnumerator = null!;
    private DirectoryScanService _scanServiceWithGetDirs = null!;
    private DirectoryScanService _scanServiceWithEnumeratorParallel = null!;
    private DirectoryScanService _scanServiceWithGetDirsParallel = null!;
    private ScanFilterService _scanFilterService = null!;
    private ILogger<DirectoryScanService> _logger = null!;
    private TreeNode _rootNode = null!;
    private string _testPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Configure Serilog with null sink to completely disable logging output
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Fatal() // Only show fatal errors
            .WriteTo.Sink(new NullSink()) // Redirect to null sink
            .CreateLogger();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog();
        });

        _logger = loggerFactory.CreateLogger<DirectoryScanService>();
        _scanFilterService = new ScanFilterService();

        // Create separate instances for each approach (to avoid state pollution)
        _scanServiceWithEnumerator = new DirectoryScanService(_scanFilterService, _logger);
        _scanServiceWithGetDirs = new DirectoryScanService(_scanFilterService, _logger);
        _scanServiceWithEnumeratorParallel = new DirectoryScanService(_scanFilterService, _logger);
        _scanServiceWithGetDirsParallel = new DirectoryScanService(_scanFilterService, _logger);

        _testPath = ROOT_PATH;

        if (!Directory.Exists(_testPath))
        {
            Console.WriteLine($"WARNING: Test path does not exist: {_testPath}");
            Console.WriteLine($"Using current directory instead: {Directory.GetCurrentDirectory()}");
            _testPath = Directory.GetCurrentDirectory();
        }

        _rootNode = DirectoryScanService.CreateRootNode(_testPath);
    }

    [Benchmark(Description = "DirectoryEnumerator Approach")]
    public List<TreeNode> BenchmarkDirectoryEnumerator()
    {
        var allChildren = new List<TreeNode>();
        var scanOptions = CreateFilterOptions();
        var filterFunc = _scanFilterService.CreateFilter(scanOptions);
        
        // Scan all directories sequentially
        foreach (var dirPath in scanOptions.DirectoriesToScan)
        {
            if (!Directory.Exists(dirPath)) continue;
            
            try
            {
                var rootNode = DirectoryScanService.CreateRootNode(dirPath);
                var dirInfo = new DirectoryInfo(dirPath);
                var children = new List<TreeNode>();
                
                _scanServiceWithEnumerator.LoadSubdirectoriesWithEnumerator(rootNode, dirInfo, filterFunc, children);
                allChildren.AddRange(children);
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
                continue;
            }
        }
        
        return allChildren;
    }

    [Benchmark(Description = "GetDirectories() Approach")]
    public List<TreeNode> BenchmarkGetDirectories()
    {
        var allChildren = new List<TreeNode>();
        var scanOptions = CreateFilterOptions();
        var filterFunc = _scanFilterService.CreateFilter(scanOptions);
        
        // Scan all directories sequentially
        foreach (var dirPath in scanOptions.DirectoriesToScan)
        {
            if (!Directory.Exists(dirPath)) continue;
            
            try
            {
                var rootNode = DirectoryScanService.CreateRootNode(dirPath);
                var dirInfo = new DirectoryInfo(dirPath);
                var children = new List<TreeNode>();
                
                _scanServiceWithGetDirs.LoadSubdirectories(rootNode, dirInfo, filterFunc, children);
                allChildren.AddRange(children);
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
                continue;
            }
        }
        
        return allChildren;
    }

    [Benchmark(Description = "DirectoryEnumerator Parallel Approach")]
    public List<TreeNode> BenchmarkDirectoryEnumeratorParallel()
    {
        var allChildren = new List<TreeNode>();
        var scanOptions = CreateFilterOptions();
        var filterFunc = _scanFilterService.CreateFilter(scanOptions);
        var lockObj = new object();
        
        // Scan all directories in parallel
        Parallel.ForEach(scanOptions.DirectoriesToScan, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, dirPath =>
        {
            if (!Directory.Exists(dirPath)) return;
            
            try
            {
                var rootNode = DirectoryScanService.CreateRootNode(dirPath);
                var dirInfo = new DirectoryInfo(dirPath);
                var children = new List<TreeNode>();
                
                _scanServiceWithEnumeratorParallel.LoadSubdirectoriesWithEnumeratorParallel(rootNode, dirInfo, filterFunc, children);
                
                lock (lockObj)
                {
                    allChildren.AddRange(children);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
                return;
            }
        });
        
        return allChildren;
    }

    [Benchmark(Description = "GetDirectories() Parallel Approach")]
    public List<TreeNode> BenchmarkGetDirectoriesParallel()
    {
        var allChildren = new List<TreeNode>();
        var scanOptions = CreateFilterOptions();
        var filterFunc = _scanFilterService.CreateFilter(scanOptions);
        var lockObj = new object();
        
        // Scan all directories in parallel
        Parallel.ForEach(scanOptions.DirectoriesToScan, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, dirPath =>
        {
            if (!Directory.Exists(dirPath)) return;
            
            try
            {
                var rootNode = DirectoryScanService.CreateRootNode(dirPath);
                var dirInfo = new DirectoryInfo(dirPath);
                var children = new List<TreeNode>();
                
                _scanServiceWithGetDirsParallel.LoadSubdirectoriesParallel(rootNode, dirInfo, filterFunc, children);
                
                lock (lockObj)
                {
                    allChildren.AddRange(children);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have access to
                return;
            }
        });
        
        return allChildren;
    }

    private ScanOptions CreateFilterOptions()
    {
        var filter = new ScanOptions
        {
            AgeFilter = new AgeFilter
            {
                UseModifiedDate = true,
                ModifiedBefore = DateTime.Now.AddDays(-1)
            }
        };

        // Add comprehensive list of directories to scan
        filter.DirectoriesToScan.Add(_testPath);
        filter.DirectoriesToScan.Add("C:\\Users\\Kamilos\\Desktop");
        filter.DirectoriesToScan.Add("C:\\Users\\Kamilos\\Documents");
        filter.DirectoriesToScan.Add("C:\\Users\\Kamilos\\Pictures");
        filter.DirectoriesToScan.Add("C:\\Users\\Kamilos\\Downloads");
        filter.DirectoriesToScan.Add("C:\\Users\\Kamilos\\AppData\\Local");
        filter.DirectoriesToScan.Add("C:\\Users\\Kamilos\\Music");
        filter.DirectoriesToScan.Add("C:\\Users\\Kamilos\\Videos");
        filter.DirectoriesToScan.Add("C:\\Program Files");
        filter.DirectoriesToScan.Add("C:\\Program Files (x86)");
        filter.DirectoriesToScan.Add("C:\\Users");
        filter.DirectoriesToScan.Add("C:\\Windows\\Temp");
        filter.DirectoriesToScan.Add("C:\\ProgramData");

        return filter;
    }
}

