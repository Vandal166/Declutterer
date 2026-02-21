using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Domain.Models;
using Declutterer.Utilities.Extensions;
using Declutterer.Utilities.Helpers;

namespace Declutterer.UI.ViewModels;

/// <summary>
/// Represents a single deletion history entry in the UI.
/// Provides formatted display properties for the history view.
/// </summary>
public sealed partial class DeletionHistoryEntryViewModel : ViewModelBase
{
    [ObservableProperty]
    private DeletionHistoryEntry _entry;

    private static readonly StreamGeometry FileGeometry = StreamGeometry.Parse("M12.25 4C9.90279 4 8 5.90279 8 8.25V39.75C8 42.0972 9.90279 44 12.25 44H35.75C38.0972 44 40 42.0972 40 39.75V18.4142C40 17.8175 39.7629 17.2452 39.341 16.8232L27.1768 4.65901C26.7548 4.23705 26.1825 4 25.5858 4H12.25ZM10.5 8.25C10.5 7.2835 11.2835 6.5 12.25 6.5H24V15.25C24 17.3211 25.6789 19 27.75 19H37.5V39.75C37.5 40.7165 36.7165 41.5 35.75 41.5H12.25C11.2835 41.5 10.5 40.7165 10.5 39.75V8.25ZM35.4822 16.5H27.75C27.0596 16.5 26.5 15.9404 26.5 15.25V7.51777L35.4822 16.5Z");

    private static readonly StreamGeometry DirectoryGeometry = StreamGeometry.Parse("M17.0606622,9 C17.8933043,9 18.7000032,9.27703406 19.3552116,9.78392956 L19.5300545,9.92783739 L22.116207,12.1907209 C22.306094,12.356872 22.5408581,12.4608817 22.7890575,12.4909364 L22.9393378,12.5 L40.25,12.5 C42.2542592,12.5 43.8912737,14.0723611 43.994802,16.0508414 L44,16.25 L44,35.25 C44,37.2542592 42.4276389,38.8912737 40.4491586,38.994802 L40.25,39 L7.75,39 C5.74574083,39 4.10872626,37.4276389 4.00519801,35.4491586 L4,35.25 L4,12.75 C4,10.7457408 5.57236105,9.10872626 7.55084143,9.00519801 L7.75,9 L17.0606622,9 Z M22.8474156,14.9988741 L20.7205012,17.6147223 C20.0558881,18.4327077 19.0802671,18.9305178 18.0350306,18.993257 L17.8100737,19 L6.5,18.999 L6.5,35.25 C6.5,35.8972087 6.99187466,36.4295339 7.62219476,36.4935464 L7.75,36.5 L40.25,36.5 C40.8972087,36.5 41.4295339,36.0081253 41.4935464,35.3778052 L41.5,35.25 L41.5,16.25 C41.5,15.6027913 41.0081253,15.0704661 40.3778052,15.0064536 L40.25,15 L22.8474156,14.9988741 Z M17.0606622,11.5 L7.75,11.5 C7.10279131,11.5 6.5704661,11.9918747 6.50645361,12.6221948 L6.5,12.75 L6.5,16.499 L17.8100737,16.5 C18.1394331,16.5 18.4534488,16.3701335 18.6858203,16.1419575 L18.7802162,16.0382408 L20.415,14.025 L17.883793,11.8092791 C17.693906,11.643128 17.4591419,11.5391183 17.2109425,11.5090636 L17.0606622,11.5 Z");
    
    public DeletionHistoryEntryViewModel(DeletionHistoryEntry entry)
    {
        Entry = entry;
    }

    public string SizeFormatted => ByteConverter.ToReadableString(Entry.SizeBytes);

    public string TypeBadge => Entry.DeletionType switch
    {
        "RecycleBin" => "Recycle Bin",
        "Permanent" => "Permanent",
        _ => Entry.DeletionType
    };

    public string TypeColor => Entry.DeletionType switch
    {
        "RecycleBin" => "#FF6495ED", // Orange for Recycle Bin
        "Permanent" => "#DC143C",  // Crimson for Permanent
        _ => "#999999"
    };

    public StreamGeometry? ItemTypeIconGeometry => Entry.IsDirectory 
        ? DirectoryGeometry
        : FileGeometry;


    public string DeletionTimeFormatted => Entry.DeletionDateTime.ToString("HH:mm:ss");

    public string DisplayPath => PathExtensions.GetMiddleEllipsis(Entry.Path, 60);
}