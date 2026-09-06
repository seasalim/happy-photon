namespace HappyPhoton.Tests;

internal static class PlatformRenderGoldens
{
    // Measured from the original frozen commits, never regenerated from release output.
    // Sequence: 73d631ce9fbb24924a066db211fd1075207de692.
    // Linear: 1191d2d1e2186c33823596bc3eb02cb39cf7ad40.
    // Evidence: https://github.com/seasalim/happy-photon/actions/runs/34060709129
    private static readonly Dictionary<string, string> Hashes = new()
    {
        ["macos/sequence/standard-Export-downsize"] = "4759F5EF9C6951A819AFA0CE1073A503A1B0B4255343DD3AB16C0EFDDFA5F69B",
        ["macos/sequence/standard-Export-native"] = "47C848B17C94BBC0F42F26120C8C7BC77CB05453559A42A210CAF48BFB2D8933",
        ["macos/sequence/standard-Preview-downsize"] = "5A0BE918555F4276FCAE8CEA952EC17515672AD14AB7EB050EEEABC840852348",
        ["macos/sequence/standard-Preview-native"] = "E336E81E805833A48454DB22210F556F84E8DAD9F55AEF3DF791E7A02015EEB4",
        ["macos/linear/raw-interactive"] = "3F02B148CC36C04E03E546DD74298350A86CFD348D4FF9FEB11C2B00272AA22D",
        ["macos/linear/raw-resting"] = "17DDF83E1075686CE9D965D4ED01FC8B982B9D669E4185A4FA55433ABC571100",
        ["macos/linear/standard-interactive"] = "489DA50146CD7DA1E2B467E1E8D051C4D40D1C9C909F9A79DA82202FF10CE30A",
        ["macos/linear/standard-resting"] = "F4D16B3F27500D7841AD483DE5D8368A7B0AD868A8B84337FBD9EF4B58AA87A6",
        ["linux/sequence/standard-Export-downsize"] = "4759F5EF9C6951A819AFA0CE1073A503A1B0B4255343DD3AB16C0EFDDFA5F69B",
        ["linux/sequence/standard-Export-native"] = "47C848B17C94BBC0F42F26120C8C7BC77CB05453559A42A210CAF48BFB2D8933",
        ["linux/sequence/standard-Preview-downsize"] = "5A0BE918555F4276FCAE8CEA952EC17515672AD14AB7EB050EEEABC840852348",
        ["linux/sequence/standard-Preview-native"] = "FBDA48AD34D6501AC962CE82CF37EB9B02A0FF33FD9FED2BA3B7CCD967A3A774",
        ["linux/linear/raw-interactive"] = "D06F28C2EB555A8DB8F5865FB357A3C3D4EC00687D8F704AAA0D3FAE2FD9F019",
        ["linux/linear/raw-resting"] = "1A2C4958EDCD450798425E269BEA15C7121CAF853AB33EA72DBE7F697F005976",
        ["linux/linear/standard-interactive"] = "B8503E7A26D1F004BE870E7EC71493DD9B14D71DB8198846422AD9EBD8903A2D",
        ["linux/linear/standard-resting"] = "EF51F44D0B0C5C1D673105D6F0A55A4B46CF3514506BC622598E1E46275306EA",
    };

    internal static string Expected(string family, string name, string windowsHash)
    {
        var platform = OperatingSystem.IsMacOS() ? "macos" :
            OperatingSystem.IsLinux() ? "linux" : "windows";
        return Hashes.GetValueOrDefault($"{platform}/{family}/{name}", windowsHash);
    }
}
