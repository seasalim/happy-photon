namespace HappyPhoton.Tests;

// Render hashes and warning source are frozen at base; reader outcomes follow current policy.
// Render table: baseline/state | interactive | clipping | resting | before/after capture | hover.
internal static class ConstructionBaselineGoldens
{
    internal const string render = """
        Standard/fresh|4ec09d9ceaa0674eb38339e6be99cd007c874b5ec1e919810fc0727069bf6334|4ec09d9ceaa0674eb38339e6be99cd007c874b5ec1e919810fc0727069bf6334|4ec09d9ceaa0674eb38339e6be99cd007c874b5ec1e919810fc0727069bf6334|4ec09d9ceaa0674eb38339e6be99cd007c874b5ec1e919810fc0727069bf6334|840a208b3c1212f82fddd669926202d372ac78ab4b55e8171dc24867b05d9612
        Standard/dirty|56a457b583fc771c3cc2e5eafd2dbdef3d255f8cddf119d579eafb9abe97a912|56a457b583fc771c3cc2e5eafd2dbdef3d255f8cddf119d579eafb9abe97a912|56a457b583fc771c3cc2e5eafd2dbdef3d255f8cddf119d579eafb9abe97a912|56a457b583fc771c3cc2e5eafd2dbdef3d255f8cddf119d579eafb9abe97a912|840a208b3c1212f82fddd669926202d372ac78ab4b55e8171dc24867b05d9612
        Standard/committed-crop|d1193cb80b2c70cb33e7cefd373c05ea899f83456178adaaea40c1e98dc52a30|d1193cb80b2c70cb33e7cefd373c05ea899f83456178adaaea40c1e98dc52a30|d1193cb80b2c70cb33e7cefd373c05ea899f83456178adaaea40c1e98dc52a30|d1193cb80b2c70cb33e7cefd373c05ea899f83456178adaaea40c1e98dc52a30|b800aa4a67849878eb891da4c26720c1ae735414bdf11172ab8849c3219d6d0c
        Standard/draft-crop|b3fdae83c42ec54510642182a00df70692be9158df2d8334b878ea230f43648e|b3fdae83c42ec54510642182a00df70692be9158df2d8334b878ea230f43648e|a9170276f159e3383d59a0654a685a1170d53454b5611b2f862677d9d88ea5a1|a9170276f159e3383d59a0654a685a1170d53454b5611b2f862677d9d88ea5a1|2dfce3fe6ddabd14633c44b28cf1274b0d7a3089e769f983377f3b4b95617fc5
        Standard/raw-profile|495781a7983260f633c9dbb106e89f4f411449a1fe7ae78d5e8a14aff16786cb|495781a7983260f633c9dbb106e89f4f411449a1fe7ae78d5e8a14aff16786cb|495781a7983260f633c9dbb106e89f4f411449a1fe7ae78d5e8a14aff16786cb|495781a7983260f633c9dbb106e89f4f411449a1fe7ae78d5e8a14aff16786cb|4477ef104f816a7912266f0195aefe2b978e23fb9893658d5fb225e9bc47b6ad
        Standard/stored-geometry|931fd00310001d3afe2db14aad5e83235033a45b890c5a27b17cb456066e9b2f|931fd00310001d3afe2db14aad5e83235033a45b890c5a27b17cb456066e9b2f|931fd00310001d3afe2db14aad5e83235033a45b890c5a27b17cb456066e9b2f|931fd00310001d3afe2db14aad5e83235033a45b890c5a27b17cb456066e9b2f|e6b362d25920943d836d2e5df6ba425465e63a1640c2eb83ba19e9fa3c95994a
        Standard/live-geometry|a6a82cccc101afbfa2769d45b8a47d364fb92840a1b889a8b2a599c2dd9b0b1f|a6a82cccc101afbfa2769d45b8a47d364fb92840a1b889a8b2a599c2dd9b0b1f|a6a82cccc101afbfa2769d45b8a47d364fb92840a1b889a8b2a599c2dd9b0b1f|a6a82cccc101afbfa2769d45b8a47d364fb92840a1b889a8b2a599c2dd9b0b1f|e6b362d25920943d836d2e5df6ba425465e63a1640c2eb83ba19e9fa3c95994a
        Standard/live-crop-horizon|0825064fbb6215cbc7ab7a92dabe9eff0532c121ef26c15084b4f4498c3c35fc|0825064fbb6215cbc7ab7a92dabe9eff0532c121ef26c15084b4f4498c3c35fc|9770c761b977f8d8d9528c6c2be20ffbbc18a8f13fc251ed9134fdaf46fc98b6|9770c761b977f8d8d9528c6c2be20ffbbc18a8f13fc251ed9134fdaf46fc98b6|d036dd22d8a936b2f902e98639e0137444c1de9b7ac824667181e7e8d5a80f62
        Standard/active-preset|a3d4f3d4b71aa79c248672b2b9742977ebd2abb7225f0053854f26964b2d7f8b|a3d4f3d4b71aa79c248672b2b9742977ebd2abb7225f0053854f26964b2d7f8b|a3d4f3d4b71aa79c248672b2b9742977ebd2abb7225f0053854f26964b2d7f8b|a3d4f3d4b71aa79c248672b2b9742977ebd2abb7225f0053854f26964b2d7f8b|840a208b3c1212f82fddd669926202d372ac78ab4b55e8171dc24867b05d9612
        Standard/before-after-split|3bacde75c6b2921e6943937268cbe036229e9027e4311557271300b90548a1bf|3bacde75c6b2921e6943937268cbe036229e9027e4311557271300b90548a1bf|3bacde75c6b2921e6943937268cbe036229e9027e4311557271300b90548a1bf|3bacde75c6b2921e6943937268cbe036229e9027e4311557271300b90548a1bf|840a208b3c1212f82fddd669926202d372ac78ab4b55e8171dc24867b05d9612
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
