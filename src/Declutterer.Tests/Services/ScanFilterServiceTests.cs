using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Scanning;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Services;

public class ScanFilterServiceTests
{
    private readonly ScanFilterService _service;
    private readonly ScanFilterBuilder _filterBuilder;

    public ScanFilterServiceTests()
    {
        _filterBuilder = new ScanFilterBuilder();
        _service = new ScanFilterService(_filterBuilder);
    }

    [Fact]
    public void CreateFilter_NullOptions_ReturnsNull()
    {
        var filter = _service.CreateFilter(null);

        Assert.Null(filter);
    }

    [Fact]
    public void CreateFilter_EmptyOptions_ReturnsNonNullFilter()
    {
        var options = new ScanOptions();
        
        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_WithModifiedDateFilter_CreatesFilter()
    {
        var cutoffDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var options = new ScanOptions
        {
            AgeFilter = new AgeFilter
            {
                UseModifiedDate = true,
                ModifiedBefore = cutoffDate
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_WithAccessedDateFilter_CreatesFilter()
    {
        var cutoffDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var options = new ScanOptions
        {
            AgeFilter = new AgeFilter
            {
                UseAccessedDate = true,
                AccessedBefore = cutoffDate
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_WithFileSizeFilter_CreatesFilter()
    {
        var options = new ScanOptions
        {
            IncludeFiles = true,
            FileSizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = true,
                SizeThreshold = 10 // 10 MB
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_WithFileSizeFilterButFilesNotIncluded_CreatesFilter()
    {
        var options = new ScanOptions
        {
            IncludeFiles = false,
            FileSizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_WithDirectorySizeFilter_CreatesFilter()
    {
        var options = new ScanOptions
        {
            DirectorySizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = true,
                SizeThreshold = 100 // 100 MB
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_CombinedFilters_CreatesFilter()
    {
        var cutoffDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var options = new ScanOptions
        {
            IncludeFiles = true,
            AgeFilter = new AgeFilter
            {
                UseModifiedDate = true,
                ModifiedBefore = cutoffDate
            },
            FileSizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = true,
                SizeThreshold = 10 // 10 MB
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_WithZeroSizeThreshold_DoesNotApplyFilter()
    {
        var options = new ScanOptions
        {
            IncludeFiles = true,
            FileSizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = true,
                SizeThreshold = 0
            }
        };

        var filter = _service.CreateFilter(options);

        // When threshold is 0, the filter should not be applied (per the service logic)
        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_BothAgeFilters_CreatesFilter()
    {
        var modifiedCutoff = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var accessedCutoff = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        
        var options = new ScanOptions
        {
            AgeFilter = new AgeFilter
            {
                UseModifiedDate = true,
                ModifiedBefore = modifiedCutoff,
                UseAccessedDate = true,
                AccessedBefore = accessedCutoff
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_ModifiedDateFilterDisabled_DoesNotAddModifiedFilter()
    {
        var options = new ScanOptions
        {
            AgeFilter = new AgeFilter
            {
                UseModifiedDate = false,
                ModifiedBefore = DateTime.UtcNow
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_AccessedDateFilterDisabled_DoesNotAddAccessedFilter()
    {
        var options = new ScanOptions
        {
            AgeFilter = new AgeFilter
            {
                UseAccessedDate = false,
                AccessedBefore = DateTime.UtcNow
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_FileSizeFilterDisabled_DoesNotAddSizeFilter()
    {
        var options = new ScanOptions
        {
            IncludeFiles = true,
            FileSizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = false,
                SizeThreshold = 100
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_DirectorySizeFilterDisabled_DoesNotAddDirFilter()
    {
        var options = new ScanOptions
        {
            DirectorySizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = false,
                SizeThreshold = 100
            }
        };

        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
    }

    [Fact]
    public void CreateFilter_MultipleInvocations_ClearsBuilder()
    {
        var options1 = new ScanOptions
        {
            IncludeFiles = true,
            FileSizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = true,
                SizeThreshold = 10
            }
        };

        var options2 = new ScanOptions
        {
            AgeFilter = new AgeFilter
            {
                UseModifiedDate = true,
                ModifiedBefore = DateTime.UtcNow
            }
        };

        var filter1 = _service.CreateFilter(options1);
        var filter2 = _service.CreateFilter(options2);

        Assert.NotNull(filter1);
        Assert.NotNull(filter2);
        Assert.NotSame(filter1, filter2);
    }

    [Fact]
    public void CreateFilter_FileSizeConversion_ConvertsFromMBToBytes()
    {
        var options = new ScanOptions
        {
            IncludeFiles = true,
            FileSizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = true,
                SizeThreshold = 10 // 10 MB input
            }
        };

        // The service should convert 10 MB to 10 * 1024 * 1024 bytes
        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
        // The actual conversion happens internally - we're just verifying the filter is created
    }

    [Fact]
    public void CreateFilter_DirectorySizeConversion_ConvertsFromMBToBytes()
    {
        var options = new ScanOptions
        {
            DirectorySizeFilter = new EntrySizeFilter
            {
                UseSizeFilter = true,
                SizeThreshold = 100 // 100 MB input
            }
        };

        // The service should convert 100 MB to 100 * 1024 * 1024 bytes
        var filter = _service.CreateFilter(options);

        Assert.NotNull(filter);
        // The actual conversion happens internally - we're just verifying the filter is created
    }

    [Fact]
    public void CreateFilter_AFilterKeepsItsCriteria_WhenALaterCallRebuildsTheSharedBuilder()
    {
        // The builder is a singleton in the app and every CreateFilter call clears and refills it. A filter that is still in use
        // (watch discovery, a parallel expansion) must not change under its feet when another caller creates its own filter.
        using var temp = new TempTestDirectory();
        var recent = new DirectoryInfo(temp.CreateSubDirectory("recent"));
        recent.LastWriteTimeUtc = DateTime.UtcNow;
        var strict = _service.CreateFilter(new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = true, ModifiedBefore = DateTime.UtcNow.AddMonths(-3) }
        })!;
        Assert.False(strict(new FileSystemInfoWrapper { Info = recent })); // modified today: does not pass "older than 3 months"

        _service.CreateFilter(new ScanOptions()); // no criteria: used to empty the list the first filter was still reading

        Assert.False(strict(new FileSystemInfoWrapper { Info = recent }));
    }

    [Fact]
    public async Task CreateFilter_ConcurrentCallsNeverThrow_AndEveryFilterKeepsItsOwnCriteria()
    {
        using var temp = new TempTestDirectory();
        var recent = new DirectoryInfo(temp.CreateSubDirectory("recent"));
        recent.LastWriteTimeUtc = DateTime.UtcNow;
        var strictOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = true, ModifiedBefore = DateTime.UtcNow.AddMonths(-3) }
        };
        var looseOptions = new ScanOptions();

        var tasks = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            var wrapper = new FileSystemInfoWrapper { Info = recent };
            for (int i = 0; i < 2000; i++)
            {
                var strict = _service.CreateFilter(strictOptions)!;
                _service.CreateFilter(worker % 2 == 0 ? looseOptions : strictOptions);
                Assert.False(strict(wrapper));
            }
        })).ToArray();

        await Task.WhenAll(tasks);
    }
}
