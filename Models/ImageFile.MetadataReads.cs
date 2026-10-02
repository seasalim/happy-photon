namespace HappyPhoton.Models;

public partial class ImageFile
{
    // Per-image test instrumentation; unset during normal application use.
    internal Action<string>? MetadataReadObserved { get; set; }

    public long FileSize
    {
        get
        {
            MetadataReadObserved?.Invoke(nameof(FileSize));

            return _fileSize;
        }
        set
        {
            if (_fileSize == value) return;

            OnPropertyChanging(nameof(FileSize));
            _fileSize = value;
            OnFileSizeChanged(value);
            OnPropertyChanged(nameof(FileSize));
        }
    }

    public DateTime? DateTaken
    {
        get
        {
            MetadataReadObserved?.Invoke(nameof(DateTaken));

            return _dateTaken;
        }
        set
        {
            if (_dateTaken == value) return;

            OnPropertyChanging(nameof(DateTaken));
            _dateTaken = value;
            OnDateTakenChanged(value);
            OnPropertyChanged(nameof(DateTaken));
        }
    }
}
