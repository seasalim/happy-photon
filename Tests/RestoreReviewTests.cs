using System.Security.AccessControl;
using System.Security.Principal;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestoreReviewTests
{
    [Fact]
    public async Task ReadOnlyBackupDirectory_RestoresWithoutSidecar()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Uses Windows directory ACLs."); return; }
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var external = Directory.CreateDirectory(Path.Combine(directory.Path, "read-only"));
        var path = Path.Combine(external.FullName, "copied.zip");
        File.Copy(f.Backup, path);
        using var identity = WindowsIdentity.GetCurrent();
        var originalAcl = external.GetAccessControl();
        var deniedAcl = external.GetAccessControl();
        deniedAcl.AddAccessRule(new FileSystemAccessRule(identity.User!,
            FileSystemRights.CreateFiles, AccessControlType.Deny));
        external.SetAccessControl(deniedAcl);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(path + ".probe", "denied"));
            var journal = new CatalogLocationMigrator(f.Service);
            await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, path);
            await journal.ExecutePendingAsync();
            Assert.Equal(RestoreTestSupport.Payload(path), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
            Assert.Equal([path], Directory.GetFiles(external.FullName));
        }
        finally
        {
            originalAcl.SetSecurityDescriptorBinaryForm(originalAcl.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            external.SetAccessControl(originalAcl);
        }
    }

    [Fact]
    public async Task AccessDenied_DoesNotMarkArchiveDamaged()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Uses Windows file ACLs."); return; }
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        var service = new CatalogBackupService(catalog);
        var file = new FileInfo(f.Backup);
        using var identity = WindowsIdentity.GetCurrent();
        var originalAcl = file.GetAccessControl();
        var deniedAcl = file.GetAccessControl();
        deniedAcl.AddAccessRule(new FileSystemAccessRule(identity.User!,
            FileSystemRights.ReadData, AccessControlType.Deny));
        file.SetAccessControl(deniedAcl);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => service.CheckForRestore(f.Backup));
            Assert.Equal("verified when created", Assert.Single(service.ListForRestore()).State);
        }
        finally
        {
            originalAcl.SetSecurityDescriptorBinaryForm(originalAcl.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            file.SetAccessControl(originalAcl);
        }
        using var check = service.CheckForRestore(f.Backup);
        Assert.True(Assert.Single(service.ListForRestore()).CanRestore);
    }

    [Fact]
    public async Task SharingViolation_DoesNotMarkArchiveDamaged()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        var service = new CatalogBackupService(catalog);
        using (var locked = new FileStream(f.Backup, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => service.CheckForRestore(f.Backup));
        Assert.Equal("verified when created", Assert.Single(service.ListForRestore()).State);
    }

    [Fact]
    public async Task Preservation_CleansOnlyItsOwnPartials_BeforeCreatingTheArchive()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        var state = (await journal.ReadJournalAsync()).Restore!;
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        var folder = new CatalogBackupService(catalog).Folder;
        var stem = Path.Combine(folder, state.BeforeStem);
        File.WriteAllText(stem + ".partial.zip", "unfinished zip");
        File.WriteAllText(stem + ".partial.json", "unfinished manifest");
        var unrelated = Path.Combine(folder, $"hp-backup-{Guid.NewGuid():N}.partial.zip");
        File.WriteAllText(unrelated, "other operation");
        await journal.ExecutePendingAsync();
        Assert.False(File.Exists(stem + ".partial.zip"));
        Assert.False(File.Exists(stem + ".partial.json"));
        Assert.Equal("other operation", File.ReadAllText(unrelated));
        Assert.Equal(RestoreTestSupport.Payload(f.Backup), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
    }

    [Theory]
    [InlineData("matching")]
    [InlineData("missing")]
    [InlineData("different")]
    [InlineData("invalid")]
    public async Task Sidecar_IsOnlyRewrittenWhenNecessary(string state)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var sidecar = Path.ChangeExtension(f.Backup, ".manifest.json");
        if (state == "missing") File.Delete(sidecar);
        if (state == "different") File.WriteAllText(sidecar, "null");
        if (state == "invalid") File.WriteAllText(sidecar, "invalid JSON");
        var writes = new List<string>();
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal) { Step = writes.Add }.StageAsync(f.Locations, f.Backup);
        Assert.Equal(state != "matching", writes.Contains("before:sidecar-write"));
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        Assert.Equal("verified when created", Assert.Single(new CatalogBackupService(catalog).ListForRestore()).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SidecarPublicationFailure_RemovesPartial_AndStillRestores(bool accessDenied)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var sidecar = Path.ChangeExtension(f.Backup, ".manifest.json");
        File.Delete(sidecar);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal)
        {
            Step = step =>
            {
                if (step != "after:sidecar-write") return;
                if (accessDenied) throw new UnauthorizedAccessException("Sidecar publication denied");
                throw new IOException("Sidecar publication unavailable");
            }
        }.StageAsync(f.Locations, f.Backup);
        Assert.False(File.Exists(sidecar + ".partial"));
        await journal.ExecutePendingAsync();
        Assert.Equal(RestoreTestSupport.Payload(f.Backup), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
    }
}
