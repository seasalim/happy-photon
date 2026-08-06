using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class BaseLoaderRouter : IBaseImageLoader
{
    private readonly IBaseImageLoader _rawLoader;
    private readonly IBaseImageLoader _standardLoader;
    private readonly Func<bool> _isWindows;
    private readonly Action<string> _logWarning;

    public BaseLoaderRouter(
        IBaseImageLoader rawLoader,
        IBaseImageLoader standardLoader)
        : this(
            rawLoader,
            standardLoader,
            OperatingSystem.IsWindows,
            ImageServiceHelpers.LogError)
    {
    }

    internal BaseLoaderRouter(
        IBaseImageLoader rawLoader,
        IBaseImageLoader standardLoader,
        Func<bool> isWindows,
        Action<string> logWarning)
    {
        _rawLoader = rawLoader ??
            throw new ArgumentNullException(nameof(rawLoader));
        _standardLoader = standardLoader ??
            throw new ArgumentNullException(nameof(standardLoader));
        _isWindows = isWindows ??
            throw new ArgumentNullException(nameof(isWindows));
        _logWarning = logWarning ??
            throw new ArgumentNullException(nameof(logWarning));
    }

    public bool CanLoad(ImageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.IsRaw)
        {
            return _standardLoader.CanLoad(file);
        }

        return _rawLoader.CanLoad(file) ||
            (AllowsRawFallback(file) && _standardLoader.CanLoad(file));
    }

    public BaseImage? LoadPreviewBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        Load(file, decode, cancellationToken, preview: true);

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        Load(file, decode, cancellationToken, preview: false);

    private BaseImage? Load(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken,
        bool preview)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();

        if (!file.IsRaw)
        {
            return LoadFrom(
                _standardLoader,
                file,
                decode,
                cancellationToken,
                preview);
        }

        var raw = LoadFrom(
            _rawLoader,
            file,
            decode,
            cancellationToken,
            preview);
        if (raw != null)
        {
            return raw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!AllowsRawFallback(file))
        {
            return null;
        }

        _logWarning(
            $"LibRaw base decode failed for '{file.FileName}'; trying the standard loader.");
        return LoadFrom(
            _standardLoader,
            file,
            decode,
            cancellationToken,
            preview);
    }

    private static BaseImage? LoadFrom(
        IBaseImageLoader loader,
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken,
        bool preview)
    {
        if (!loader.CanLoad(file))
        {
            return null;
        }

        return preview
            ? loader.LoadPreviewBase(file, decode, cancellationToken)
            : loader.LoadFullBase(file, decode, cancellationToken);
    }

    private bool AllowsRawFallback(ImageFile file) =>
        !(_isWindows() &&
          file.Extension.Equals(".RAF", StringComparison.OrdinalIgnoreCase));
}
