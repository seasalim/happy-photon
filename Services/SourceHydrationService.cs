using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class SourceHydrationService
{
    private readonly ISourceAvailabilityService _availabilityService;

    internal SourceHydrationService(
        ISourceAvailabilityService availabilityService) =>
        _availabilityService = availabilityService ??
            throw new ArgumentNullException(nameof(availabilityService));

    internal async Task<bool> HydrateAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken)
    {
        var availability = _availabilityService.GetAvailability(
            imageFile.FilePath);
        if (!SourceAccessPolicy.CanRead(
            availability,
            SourceReadIntent.UserApprovedHydration))
        {
            return false;
        }

        if (availability == SourceAvailability.RequiresHydration)
        {
            await using var source = new FileStream(
                imageFile.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(Stream.Null, cancellationToken);
        }

        return SourceAccessPolicy.CanRead(
            _availabilityService.GetAvailability(imageFile.FilePath),
            SourceReadIntent.Background);
    }
}
