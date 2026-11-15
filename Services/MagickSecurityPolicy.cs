using ImageMagick;
using ImageMagick.Configuration;

namespace HappyPhoton.Services;

internal static class MagickSecurityPolicy
{
    internal static void Initialize()
    {
        var configuration = ConfigurationFiles.Default;
        configuration.Policy.Data = configuration.Policy.Data.Replace(
            "</policymap>",
            """<policy domain="system" name="max-profile-count" value="32" /></policymap>""");
        MagickNET.Initialize(configuration);
    }
}
