using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class RawBaseLoader
{
    private static LensPrescriptionReadResult ReadLensPrescription(
        ImageFile file,
        string? lensName)
    {
        var result = file.Extension.Equals(".dng", StringComparison.OrdinalIgnoreCase)
            ? new DngLensPrescriptionReader().Read(file.FilePath)
            : file.Extension.Equals(".raf", StringComparison.OrdinalIgnoreCase)
                ? new FujiLensPrescriptionReader().Read(file.FilePath, lensName)
                : LensPrescriptionReadResult.None;
        if (result.Status is LensPrescriptionStatus.Invalid or
            LensPrescriptionStatus.Unsupported)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawBaseLoader),
                $"Lens prescription rejected: {result.Message}",
                file.FilePath);
        }
        return result;
    }
}
