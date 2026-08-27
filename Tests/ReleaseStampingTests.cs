using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ReleaseStampingTests
{
    [Fact]
    public void LinuxAppImagePackaging_PinsAndVerifiesToolAndRuntimeBeforeUse()
    {
        var script = ReadRepositoryFile("scripts", "package-linux-appimage.sh");
        var urls = Regex.Matches(
            script,
            "https://[^\\\"]+")
            .Select(match => match.Value)
            .ToArray();
        var checksums = Regex.Matches(script, "[0-9a-f]{64}");

        Assert.Equal(2, urls.Length);
        Assert.Contains("/1.9.1/", urls[0]);
        Assert.Contains("/20251108/", urls[1]);
        Assert.DoesNotContain(urls, url =>
            url.Contains("continuous", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("latest", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, checksums.Count);

        var toolVerification = script.IndexOf(
            "verify_sha256 \"$appimagetool_path\"",
            StringComparison.Ordinal);
        var runtimeVerification = script.IndexOf(
            "verify_sha256 \"$runtime_path\"",
            StringComparison.Ordinal);
        var toolExecution = script.IndexOf(
            "\"$appimagetool_path\" --appimage-extract-and-run",
            StringComparison.Ordinal);
        var runtimeUse = script.IndexOf(
            "--runtime-file \"$runtime_path\"",
            StringComparison.Ordinal);

        Assert.InRange(toolVerification, 0, toolExecution - 1);
        Assert.InRange(runtimeVerification, 0, runtimeUse - 1);
        Assert.StartsWith("set -euo pipefail", script.Split('\n')[2]);
    }

    [Fact]
    public void LinuxAppImagePackaging_UsesPublishOutputAndPublicAssetName()
    {
        var script = ReadRepositoryFile("scripts", "package-linux-appimage.sh");
        var workflow = ReadRepositoryFile(
            ".github",
            "workflows",
            "release.yml");
        var linuxJob = workflow[workflow.IndexOf("  linux:", StringComparison.Ordinal)..
            workflow.IndexOf("  macos:", StringComparison.Ordinal)];

        Assert.Single(Regex.Matches(linuxJob, "dotnet publish").Cast<Match>());
        Assert.Contains("--publish-dir artifacts/release/linux-x64", linuxJob);
        Assert.Contains("cp -a \"$publish_directory/.\" \"$app_dir/usr/bin/\"", script);
        Assert.Contains("happy-photon-${{ needs.prepare.outputs.version }}-x86_64.AppImage", linuxJob);
        Assert.Contains("sha256sum happy-photon-*", workflow);
        Assert.Contains("xvfb-run -a \"$appimage\"", linuxJob);
        Assert.DoesNotContain("--appimage-extract-and-run \"$appimage\"", linuxJob);
    }

    [Fact]
    public void LinuxAppImageAssets_MatchDesktopContract()
    {
        var desktop = ReadRepositoryFile(
            "packaging",
            "linux",
            "happy-photon.desktop");
        var project = XDocument.Load(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "HappyPhoton.csproj"));
        var assemblyName = project.Descendants("AssemblyName").Single().Value;

        Assert.Contains("Name=Happy Photon", desktop);
        Assert.Contains("Categories=Graphics;Photography;", desktop);
        Assert.Contains("Exec=HappyPhoton %F", desktop);
        Assert.Contains("Icon=happy-photon", desktop);
        Assert.Contains($"StartupWMClass={assemblyName}", desktop);

        var icon = File.ReadAllBytes(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "Assets",
            "happy-photon-icon.png"));
        Assert.Equal(256, ReadPngDimension(icon, 16));
        Assert.Equal(256, ReadPngDimension(icon, 20));

        var appRunPath = Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "packaging",
            "linux",
            "AppRun");
        var scriptPath = Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "scripts",
            "package-linux-appimage.sh");
        var appRun = File.ReadAllText(appRunPath);
        var script = File.ReadAllText(scriptPath);
        Assert.StartsWith("#!/bin/sh", appRun);
        Assert.Contains("HERE=\"$(dirname \"$(readlink -f \"$0\")\")\"", appRun);
        Assert.Contains("exec \"$HERE/usr/bin/HappyPhoton\" \"$@\"", appRun);
        Assert.DoesNotContain("$APPDIR", appRun);
        Assert.Contains(
            "cp \"$project_root/Assets/happy-photon-icon.png\" \"$app_dir/happy-photon.png\"",
            script);
        Assert.Contains(
            "cp \"$project_root/Assets/happy-photon-icon.png\" \"$app_dir/.DirIcon\"",
            script);
        Assert.Contains("chmod +x \"$app_dir/AppRun\"", script);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserExecute,
                File.GetUnixFileMode(appRunPath) & UnixFileMode.UserExecute);
            Assert.Equal(
                UnixFileMode.UserExecute,
                File.GetUnixFileMode(scriptPath) & UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void LinuxAppImageDocumentation_RecordsDistributionDecisions()
    {
        var releaseEngineering = ReadRepositoryFile(
            "docs",
            "release-engineering.md");
        var releaseNotes = ReadRepositoryFile(
            "docs",
            "release-notes-preamble.md");

        Assert.Matches("appimagetool\\s+1\\.9\\.1", releaseEngineering);
        Assert.Contains("type-2 runtime 20251108", releaseEngineering);
        Assert.Contains("not claimed to be byte-reproducible", releaseEngineering);
        Assert.Contains("statically linked, so no `libfuse2`", releaseNotes);
        Assert.Contains("--appimage-extract-and-run", releaseNotes);
        Assert.DoesNotContain("needs no FUSE", releaseNotes);
    }

    [Fact]
    public void MacPackaging_ForwardsSharedBuildIdentityProperties()
    {
        var script = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "scripts",
            "package-macos.sh"));

        Assert.Contains("HAPPY_PHOTON_SOURCE_REVISION", script);
        Assert.Contains("HAPPY_PHOTON_BUILD_TIMESTAMP", script);
        Assert.Contains(
            "-p:SourceRevisionId=\"$HAPPY_PHOTON_SOURCE_REVISION\"",
            script);
        Assert.Contains(
            "-p:SourceRevision=\"$HAPPY_PHOTON_SOURCE_REVISION\"",
            script);
        Assert.Contains(
            "-p:BuildTimestampUtc=\"$HAPPY_PHOTON_BUILD_TIMESTAMP\"",
            script);
    }

    [Fact]
    public void MacPackaging_AppliesRequiredJitEntitlement()
    {
        var script = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "scripts",
            "package-macos.sh"));
        var entitlements = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            "Platforms",
            "macOS",
            "HappyPhoton.entitlements"));

        Assert.Contains("--entitlements \"$entitlements_file\"", script);
        Assert.Contains("sign_app_bundle", script);
        Assert.Contains("com.apple.security.cs.allow-jit", entitlements);

        var workflow = File.ReadAllText(Path.Combine(
            GoldenTestPaths.RepositoryRoot,
            ".github",
            "workflows",
            "release.yml"));
        Assert.Contains("Smoke test signed app launch", workflow);
        Assert.Contains("happy-photon-launch.log", workflow);
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(
            new[] { GoldenTestPaths.RepositoryRoot }.Concat(pathParts).ToArray()));

    private static int ReadPngDimension(byte[] png, int offset) =>
        (png[offset] << 24) |
        (png[offset + 1] << 16) |
        (png[offset + 2] << 8) |
        png[offset + 3];
}
