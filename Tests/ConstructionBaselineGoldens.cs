namespace HappyPhoton.Tests;

// Render hashes and warning source are frozen at base; reader outcomes follow current policy.
// Render table: baseline/state | interactive | clipping | resting | before/after capture | hover.
internal static class ConstructionBaselineGoldens
{
    // Base v21 re-key only; construction settings and assertions are unchanged.
    internal const string render = """
        Standard/fresh|8f403b8bc14e53fcea33dd24b350f49e3414df96a89c4dd9330eea7e3d496776|8f403b8bc14e53fcea33dd24b350f49e3414df96a89c4dd9330eea7e3d496776|8f403b8bc14e53fcea33dd24b350f49e3414df96a89c4dd9330eea7e3d496776|8f403b8bc14e53fcea33dd24b350f49e3414df96a89c4dd9330eea7e3d496776|fcc104535a17ecfa4525ee3e9c122079a58a02b7deabf2d9e95f710752dbd3b9
        Standard/dirty|fe35a29cb64564c0435dadd3a37621f3c568f7e602479150c4112df0c7dedf8f|fe35a29cb64564c0435dadd3a37621f3c568f7e602479150c4112df0c7dedf8f|fe35a29cb64564c0435dadd3a37621f3c568f7e602479150c4112df0c7dedf8f|fe35a29cb64564c0435dadd3a37621f3c568f7e602479150c4112df0c7dedf8f|fcc104535a17ecfa4525ee3e9c122079a58a02b7deabf2d9e95f710752dbd3b9
        Standard/committed-crop|3f29182d7e671bbe12c65cca9c93e4fb8396412ede762135d75960f3898dd127|3f29182d7e671bbe12c65cca9c93e4fb8396412ede762135d75960f3898dd127|3f29182d7e671bbe12c65cca9c93e4fb8396412ede762135d75960f3898dd127|3f29182d7e671bbe12c65cca9c93e4fb8396412ede762135d75960f3898dd127|075f801247533fba522a65d8c9d75fddd9800fe71e25fb3547037dd62f775205
        Standard/draft-crop|6f873aabfaa099020d45f3ca869798c93d9244e5762ff912b7f36acdb639877a|6f873aabfaa099020d45f3ca869798c93d9244e5762ff912b7f36acdb639877a|8a0713254dc904a4a42e8491d31542eccb49020cbe57ed10a0c982d21cff4ddc|8a0713254dc904a4a42e8491d31542eccb49020cbe57ed10a0c982d21cff4ddc|c6b39c3506feae7eeec5e9e1b82581bfa756ee6ec32f4e5d254b4972280f3b4e
        Standard/raw-profile|a11da03082b1ceaa757848a7580c3e6fb472bc9d103713ba3b58e78eda0478c3|a11da03082b1ceaa757848a7580c3e6fb472bc9d103713ba3b58e78eda0478c3|a11da03082b1ceaa757848a7580c3e6fb472bc9d103713ba3b58e78eda0478c3|a11da03082b1ceaa757848a7580c3e6fb472bc9d103713ba3b58e78eda0478c3|c3debd3fa1b59d7bab832337890fe46a58ff8f7861b1cb2061754fab8f93d1ab
        Standard/stored-geometry|3487f0a0086ad24a13515fb0b4d9cae7687c1851cda3a8869a4e34db6e83399a|3487f0a0086ad24a13515fb0b4d9cae7687c1851cda3a8869a4e34db6e83399a|3487f0a0086ad24a13515fb0b4d9cae7687c1851cda3a8869a4e34db6e83399a|3487f0a0086ad24a13515fb0b4d9cae7687c1851cda3a8869a4e34db6e83399a|497be20321c90847ba1a046bc8c3d18a00851971e31d25eee86e156a727203b3
        Standard/live-geometry|d3ac5078565d2623e5c65f1fe91a406c3f0068f93029c76b88bc740f87b9121e|d3ac5078565d2623e5c65f1fe91a406c3f0068f93029c76b88bc740f87b9121e|d3ac5078565d2623e5c65f1fe91a406c3f0068f93029c76b88bc740f87b9121e|d3ac5078565d2623e5c65f1fe91a406c3f0068f93029c76b88bc740f87b9121e|497be20321c90847ba1a046bc8c3d18a00851971e31d25eee86e156a727203b3
        Standard/live-crop-horizon|53ee35fb862cf356eb59ab02f5b3f6cda4b047f62daaedbae25fb454370faf8d|53ee35fb862cf356eb59ab02f5b3f6cda4b047f62daaedbae25fb454370faf8d|7347cd14b9141e91f272d5b01ad73fb3733372018fb5e8a014cbaa23455f24e4|7347cd14b9141e91f272d5b01ad73fb3733372018fb5e8a014cbaa23455f24e4|a990ec958fe8b1e96e5412e94300de8d926dc3443dd068c748cd8112097a83c5
        Standard/active-preset|97b3476637e0c17e9bb40bd162c184618493d84b1786bba1a9330510fa25de6c|97b3476637e0c17e9bb40bd162c184618493d84b1786bba1a9330510fa25de6c|97b3476637e0c17e9bb40bd162c184618493d84b1786bba1a9330510fa25de6c|97b3476637e0c17e9bb40bd162c184618493d84b1786bba1a9330510fa25de6c|fcc104535a17ecfa4525ee3e9c122079a58a02b7deabf2d9e95f710752dbd3b9
        Standard/before-after-split|9d3338d746976ba04be624c2c6a8157561eb304cb82f33cea2a176153f7c2d09|9d3338d746976ba04be624c2c6a8157561eb304cb82f33cea2a176153f7c2d09|9d3338d746976ba04be624c2c6a8157561eb304cb82f33cea2a176153f7c2d09|9d3338d746976ba04be624c2c6a8157561eb304cb82f33cea2a176153f7c2d09|fcc104535a17ecfa4525ee3e9c122079a58a02b7deabf2d9e95f710752dbd3b9
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
