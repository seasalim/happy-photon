namespace HappyPhoton.Tests;

// Render hashes and warning source are frozen at base; reader outcomes follow current policy.
// Render table: baseline/state | interactive | clipping | resting | before/after capture | hover.
internal static class ConstructionBaselineGoldens
{
    // Base v22 re-key only; construction settings and assertions are unchanged.
    internal const string render = """
        Standard/fresh|27fb8ed70f907ac5519a2051cd20c8d5a6e189efd07a0d598aaf082e5742a7d5|27fb8ed70f907ac5519a2051cd20c8d5a6e189efd07a0d598aaf082e5742a7d5|27fb8ed70f907ac5519a2051cd20c8d5a6e189efd07a0d598aaf082e5742a7d5|27fb8ed70f907ac5519a2051cd20c8d5a6e189efd07a0d598aaf082e5742a7d5|977a50a0d3e0e7d81738d1d7de797fbdf8ae926cd90bba17b71baaac9999e34d
        Standard/dirty|329e23cfde988ded49d54105c780ba38702d137bc17e554e9d7166d385dae35b|329e23cfde988ded49d54105c780ba38702d137bc17e554e9d7166d385dae35b|329e23cfde988ded49d54105c780ba38702d137bc17e554e9d7166d385dae35b|329e23cfde988ded49d54105c780ba38702d137bc17e554e9d7166d385dae35b|977a50a0d3e0e7d81738d1d7de797fbdf8ae926cd90bba17b71baaac9999e34d
        Standard/committed-crop|f89962b3c76a5c358d93458fb8ec89a62eb0f8dd8e9bd65d92441fc0b51e4652|f89962b3c76a5c358d93458fb8ec89a62eb0f8dd8e9bd65d92441fc0b51e4652|f89962b3c76a5c358d93458fb8ec89a62eb0f8dd8e9bd65d92441fc0b51e4652|f89962b3c76a5c358d93458fb8ec89a62eb0f8dd8e9bd65d92441fc0b51e4652|5f8c51e2d45299a6c44e4213290d9818ed9c7a7b88717a66f1cfb20cab21a3a5
        Standard/draft-crop|8210475c9a85503130012f9bd0f2f791078fba6a9fffb9e75a6b3220231ce9fa|8210475c9a85503130012f9bd0f2f791078fba6a9fffb9e75a6b3220231ce9fa|1d2bd96c485a72f37cfb143551ee478abc5dd656be45ef23a820094caa64e00f|1d2bd96c485a72f37cfb143551ee478abc5dd656be45ef23a820094caa64e00f|615c2a45cf9b9bf5b65645d3f29e9ff1b0d9b69f66d012086205398f81514b37
        Standard/raw-profile|e931c1d176884167255c7545040c3ea9563df1c40b6aeddb2af7d4ec49d27f6f|e931c1d176884167255c7545040c3ea9563df1c40b6aeddb2af7d4ec49d27f6f|e931c1d176884167255c7545040c3ea9563df1c40b6aeddb2af7d4ec49d27f6f|e931c1d176884167255c7545040c3ea9563df1c40b6aeddb2af7d4ec49d27f6f|93ac6e865039fd2a388e7b5b4beae171798fa59a764321f93582ccc841af1706
        Standard/stored-geometry|9b1f4d97eadb6bc9685333b74270ebd3e83f89ff707392f1db2bd035e824d16a|9b1f4d97eadb6bc9685333b74270ebd3e83f89ff707392f1db2bd035e824d16a|9b1f4d97eadb6bc9685333b74270ebd3e83f89ff707392f1db2bd035e824d16a|9b1f4d97eadb6bc9685333b74270ebd3e83f89ff707392f1db2bd035e824d16a|d100a20b0e099aa8ecf099e2ef8ff7e5b7b00fe360c815cdc90ae2544dbaf360
        Standard/live-geometry|b1aee8124490ec9aed91b8a0155358d3887592f91661460e3580809e90f92737|b1aee8124490ec9aed91b8a0155358d3887592f91661460e3580809e90f92737|b1aee8124490ec9aed91b8a0155358d3887592f91661460e3580809e90f92737|b1aee8124490ec9aed91b8a0155358d3887592f91661460e3580809e90f92737|d100a20b0e099aa8ecf099e2ef8ff7e5b7b00fe360c815cdc90ae2544dbaf360
        Standard/live-crop-horizon|13aa99b5103af14fc8e9a17a0bc73dae44ff259c138f426a82b14735401c7cac|13aa99b5103af14fc8e9a17a0bc73dae44ff259c138f426a82b14735401c7cac|6c13c3889ff59b96bf831055f0655ed72e24fcff91ea93384354b1392eb94d2b|6c13c3889ff59b96bf831055f0655ed72e24fcff91ea93384354b1392eb94d2b|6ae0d1d9400a4fa2f66b82943bea23aaff08a7cb4aa906e02435bb5d5e97e1c5
        Standard/active-preset|d258e091df1f9f7e0e59fd8632a87009244ce66f807fa36989b3950195db3119|d258e091df1f9f7e0e59fd8632a87009244ce66f807fa36989b3950195db3119|d258e091df1f9f7e0e59fd8632a87009244ce66f807fa36989b3950195db3119|d258e091df1f9f7e0e59fd8632a87009244ce66f807fa36989b3950195db3119|977a50a0d3e0e7d81738d1d7de797fbdf8ae926cd90bba17b71baaac9999e34d
        Standard/before-after-split|6f714faec7732d8950c024348f8d14861b6484717d2676cec21eda0c2abb9acd|6f714faec7732d8950c024348f8d14861b6484717d2676cec21eda0c2abb9acd|6f714faec7732d8950c024348f8d14861b6484717d2676cec21eda0c2abb9acd|6f714faec7732d8950c024348f8d14861b6484717d2676cec21eda0c2abb9acd|977a50a0d3e0e7d81738d1d7de797fbdf8ae926cd90bba17b71baaac9999e34d
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
