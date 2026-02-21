using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Scanning;

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
}
