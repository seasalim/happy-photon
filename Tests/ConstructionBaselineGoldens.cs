namespace HappyPhoton.Tests;

// Render hashes and warning source are frozen at base; reader outcomes follow current policy.
// Render table: baseline/state | interactive | clipping | resting | before/after capture | hover.
internal static class ConstructionBaselineGoldens
{
    // Base v20 re-key only; construction settings and assertions are unchanged.
    internal const string render = """
        Standard/fresh|0f53d2d847602da5f877cdb9a4cf3f0345fdc8a723d4cd6e9d7b78339776e71b|0f53d2d847602da5f877cdb9a4cf3f0345fdc8a723d4cd6e9d7b78339776e71b|0f53d2d847602da5f877cdb9a4cf3f0345fdc8a723d4cd6e9d7b78339776e71b|0f53d2d847602da5f877cdb9a4cf3f0345fdc8a723d4cd6e9d7b78339776e71b|195fa0a09027fced1f9684c8c6243d665c4be789e442f07144aaea5b4994aa93
        Standard/dirty|e89af6da0bdb45510de4ba53bde219b84b97099e0ebc3fe6d338ea0045005ef9|e89af6da0bdb45510de4ba53bde219b84b97099e0ebc3fe6d338ea0045005ef9|e89af6da0bdb45510de4ba53bde219b84b97099e0ebc3fe6d338ea0045005ef9|e89af6da0bdb45510de4ba53bde219b84b97099e0ebc3fe6d338ea0045005ef9|195fa0a09027fced1f9684c8c6243d665c4be789e442f07144aaea5b4994aa93
        Standard/committed-crop|6ec0e8554bc17dd8523d5c731184eba9594a9109e46a13e203fe86fca43aadf1|6ec0e8554bc17dd8523d5c731184eba9594a9109e46a13e203fe86fca43aadf1|6ec0e8554bc17dd8523d5c731184eba9594a9109e46a13e203fe86fca43aadf1|6ec0e8554bc17dd8523d5c731184eba9594a9109e46a13e203fe86fca43aadf1|c0a338038f3276785f1000f2faaad0e7b93540dbd23964a2f45b3c211aac1c7e
        Standard/draft-crop|100fe2a0ead9e3884474a94b05b1e51527cc60867496fd7a9d27db1cac6ef42a|100fe2a0ead9e3884474a94b05b1e51527cc60867496fd7a9d27db1cac6ef42a|be3c9972c96c7c9be48f3a77399331486a497ad6b45dd721e459dee9c7fc4cdd|be3c9972c96c7c9be48f3a77399331486a497ad6b45dd721e459dee9c7fc4cdd|afa13bccbfd9a7ea97e66941d40eff285d8d98c9d0d75cddb7b154c092324c66
        Standard/raw-profile|fbb3ff12c233575deda9b3371f5fd0830ae1fb2e5f1fbe3b8a22f86eb80d4a10|fbb3ff12c233575deda9b3371f5fd0830ae1fb2e5f1fbe3b8a22f86eb80d4a10|fbb3ff12c233575deda9b3371f5fd0830ae1fb2e5f1fbe3b8a22f86eb80d4a10|fbb3ff12c233575deda9b3371f5fd0830ae1fb2e5f1fbe3b8a22f86eb80d4a10|344145839200c55e313c45d73d6e935b671e8f2bc42ff40f2f48ad8be5aea009
        Standard/stored-geometry|09346b7324d117888cf2d1bccec147fb8c91ab5f1b1df127e1ddfd4010c10173|09346b7324d117888cf2d1bccec147fb8c91ab5f1b1df127e1ddfd4010c10173|09346b7324d117888cf2d1bccec147fb8c91ab5f1b1df127e1ddfd4010c10173|09346b7324d117888cf2d1bccec147fb8c91ab5f1b1df127e1ddfd4010c10173|91d53af5deea8715f7a5a41f720ad4c9951642b2873e1899b23677f548514d73
        Standard/live-geometry|b21962333c4e773b29de6414a8a923beaf455628b4f60a0d25f35078274dc9a7|b21962333c4e773b29de6414a8a923beaf455628b4f60a0d25f35078274dc9a7|b21962333c4e773b29de6414a8a923beaf455628b4f60a0d25f35078274dc9a7|b21962333c4e773b29de6414a8a923beaf455628b4f60a0d25f35078274dc9a7|91d53af5deea8715f7a5a41f720ad4c9951642b2873e1899b23677f548514d73
        Standard/live-crop-horizon|fd081bea2ec8f22dd1a1ea0af3fec69921974bcb6cb8bc7433ffe52ad57fb1bb|fd081bea2ec8f22dd1a1ea0af3fec69921974bcb6cb8bc7433ffe52ad57fb1bb|5e872889bdada74154b16969c29df8954579a6f1972b5daa856c0ce3dee54e01|5e872889bdada74154b16969c29df8954579a6f1972b5daa856c0ce3dee54e01|e5d16acab65c49161b113c91db8f0c114be4d6b478be5efb60b48ca618d84a1d
        Standard/active-preset|bff1ff93dc6a0981cdeaca470617fac475a42b6f13add25a56dcc56ca3519b88|bff1ff93dc6a0981cdeaca470617fac475a42b6f13add25a56dcc56ca3519b88|bff1ff93dc6a0981cdeaca470617fac475a42b6f13add25a56dcc56ca3519b88|bff1ff93dc6a0981cdeaca470617fac475a42b6f13add25a56dcc56ca3519b88|195fa0a09027fced1f9684c8c6243d665c4be789e442f07144aaea5b4994aa93
        Standard/before-after-split|ccd32b03557b1b99410c200f1eb26858040d4622ba4d473f925883ce41cf61da|ccd32b03557b1b99410c200f1eb26858040d4622ba4d473f925883ce41cf61da|ccd32b03557b1b99410c200f1eb26858040d4622ba4d473f925883ce41cf61da|ccd32b03557b1b99410c200f1eb26858040d4622ba4d473f925883ce41cf61da|195fa0a09027fced1f9684c8c6243d665c4be789e442f07144aaea5b4994aa93
        """;
    internal const string migration = """
        v2-lens|{"version":4,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        v2-no-lens|{"version":4,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        v3|{"version":4,"exposure":0.75,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":false,"vignetting":true},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        v3-missing-baseline|{"version":4,"exposure":0.75,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":false,"vignetting":true},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        row-0|{"version":4,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        row-1|{"version":4,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        row-4|{"version":4,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        marker-mismatch|{"version":4,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        missing|{"version":4,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        malformed|{"version":4,"exposure":0,"wb":{"mode":"asShot","kelvin":null,"tint":null,"gains":null,"preset":null},"highlights":0,"shadows":0,"brightness":0,"contrast":0,"saturation":0,"vibrance":0,"baseLook":null,"hlReconstruction":"clip","detail":{"captureSharpen":null,"luminanceNr":0,"chromaNr":0},"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false},"rotation":0,"horizon_rotation":0,"crop":null,"curve":{"points":[{"x":0,"y":0},{"x":1,"y":1}]},"applied_preset_id":null}
        """;
    internal const string warnings = """
        private readonly HashSet<long> _editSettingsWarnings = new();
            private EditSettings ReadEditSettings(
                SqliteDataReader reader,
                int offset,
                long catalogId,
                string filePath)
            {
                var editVersion = reader.IsDBNull(offset + 1)
                    ? 0
                    : reader.GetInt32(offset + 1);
                VERSION_POLICY
                {
                    LogEditSettingsWarningOnce(
                        catalogId,
                        $"Unsupported edit settings version {editVersion} for '{filePath}'.");
                    return new EditSettings();
                }
        
                if (reader.IsDBNull(offset))
                {
                    LogEditSettingsWarningOnce(
                        catalogId,
                        $"Missing edit settings for '{filePath}'.");
                    return new EditSettings();
                }
        
                try
                {
                    var json = reader.GetString(offset);
                    using var document = JsonDocument.Parse(json);
                    if (!document.RootElement.TryGetProperty("version", out var versionElement) ||
                        !versionElement.TryGetInt32(out var documentVersion) ||
                        documentVersion != editVersion)
                    {
                        throw new JsonException(
                            "The edit settings document and row version markers do not match.");
                    }
                    var settings = EditSettingsJson.Deserialize(
                        json,
                        out var wasClamped);
                    if (wasClamped)
                    {
                        LogEditSettingsWarningOnce(
                            catalogId,
                            $"Clamped out-of-range edit settings for '{filePath}'.");
                    }
                    return settings;
                }
                catch (JsonException exception)
                {
                    LogEditSettingsWarningOnce(
                        catalogId,
                        $"Invalid edit settings for '{filePath}': {exception.Message}");
                    return new EditSettings();
                }
            }
        
            private void LogEditSettingsWarningOnce(long catalogId, string message)
            {
                if (_editSettingsWarnings.Add(catalogId))
                {
                    Debug.WriteLine($"[HappyPhoton] {message}");
                }
            }
        
        
        """;
}
