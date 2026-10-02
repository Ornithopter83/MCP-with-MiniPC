using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectHub.Worker;

namespace ProjectHub.Worker.Tests;

public sealed class MaterializationLedgerTests
{
    [Fact]
    public async Task ManifestBackedMaterializationVerifiesTargetAndPersistsLedger()
    {
        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "src", "sample.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, "old");

            var ledger = new TargetWorkspaceMaterializationLedger(root, "job");
            var before = ledger.CaptureSnapshot();
            Assert.True(before.Success);
            Assert.NotNull(before.Snapshot);

            var expectedBytes = Encoding.UTF8.GetBytes("new");
            var expectedSha = Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant();
            var manifestPath = await WriteManifestAsync(
                root,
                "job",
                new CommitManifest(
                    "10",
                    "result-10",
                    "base",
                    "tree",
                    new[]
                    {
                        new CommitManifestFile(
                            "src/sample.txt",
                            "MODIFY",
                            null,
                            expectedBytes.LongLength,
                            expectedSha,
                            true,
                            "new")
                    }));

            await File.WriteAllBytesAsync(target, expectedBytes);

            var item = CreateMaterializeItem(createdOrder: 7, dependencies: new[] { "10" });
            var result = await ledger.VerifyAndRecordAsync(
                item,
                new[]
                {
                    new WorkItemDependencyResult(
                        "10",
                        "result-10",
                        "완료",
                        WorkItemResultType.CodeChange,
                        manifestPath)
                },
                before.Snapshot!);

            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(result.LedgerPath));
            Assert.True(ledger.IsResultVerified("result-10"));

            var json = await File.ReadAllTextAsync(result.LedgerPath);
            var entry = JsonSerializer.Deserialize<MaterializationLedgerEntry>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(entry);
            Assert.True(entry!.Success);
            var file = Assert.Single(entry.Files);
            Assert.Equal("src/sample.txt", file.Path);
            Assert.Equal(expectedSha, file.ExpectedSha256);
            Assert.Equal(expectedSha, file.ActualSha256);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task VerifiedLedgerStopsMatchingAfterTargetFileChangesAgain()
    {
        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "src", "sample.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, "old");

            var ledger = new TargetWorkspaceMaterializationLedger(root, "job");
            var before = ledger.CaptureSnapshot();
            Assert.True(before.Success);

            var expectedBytes = Encoding.UTF8.GetBytes("new");
            var expectedSha = Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant();
            var manifestPath = await WriteManifestAsync(
                root,
                "job",
                new CommitManifest(
                    "10",
                    "result-10",
                    "base",
                    "tree",
                    new[]
                    {
                        new CommitManifestFile(
                            "src/sample.txt",
                            "MODIFY",
                            null,
                            expectedBytes.LongLength,
                            expectedSha,
                            true,
                            "new")
                    }));

            await File.WriteAllBytesAsync(target, expectedBytes);
            var verified = await ledger.VerifyAndRecordAsync(
                CreateMaterializeItem(7, new[] { "10" }),
                new[]
                {
                    new WorkItemDependencyResult(
                        "10",
                        "result-10",
                        "완료",
                        WorkItemResultType.CodeChange,
                        manifestPath)
                },
                before.Snapshot!);

            Assert.True(verified.Success);
            Assert.True(ledger.IsResultVerified("result-10"));

            await File.WriteAllTextAsync(target, "changed-later");

            Assert.False(ledger.IsResultVerified("result-10"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task HashMismatchBlocksVerificationAndIsNotAcceptedByFinalizerLookup()
    {
        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "src", "sample.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, "old");

            var ledger = new TargetWorkspaceMaterializationLedger(root, "job");
            var before = ledger.CaptureSnapshot();
            Assert.True(before.Success);

            var expectedBytes = Encoding.UTF8.GetBytes("expected");
            var expectedSha = Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant();
            var manifestPath = await WriteManifestAsync(
                root,
                "job",
                new CommitManifest(
                    "10",
                    "result-10",
                    "base",
                    "tree",
                    new[]
                    {
                        new CommitManifestFile(
                            "src/sample.txt",
                            "MODIFY",
                            null,
                            expectedBytes.LongLength,
                            expectedSha,
                            true,
                            "expected")
                    }));

            await File.WriteAllTextAsync(target, "wrong");

            var result = await ledger.VerifyAndRecordAsync(
                CreateMaterializeItem(8, new[] { "10" }),
                new[]
                {
                    new WorkItemDependencyResult(
                        "10",
                        "result-10",
                        "완료",
                        WorkItemResultType.CodeChange,
                        manifestPath)
                },
                before.Snapshot!);

            Assert.False(result.Success);
            Assert.Equal("MATERIALIZATION_VERIFICATION_FAILED", result.ErrorCode);
            Assert.False(ledger.IsResultVerified("result-10"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ManifestBackedMaterializationRejectsUnexpectedTargetChanges()
    {
        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "src", "sample.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, "old");

            var ledger = new TargetWorkspaceMaterializationLedger(root, "job");
            var before = ledger.CaptureSnapshot();
            Assert.True(before.Success);

            var expectedBytes = Encoding.UTF8.GetBytes("new");
            var expectedSha = Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant();
            var manifestPath = await WriteManifestAsync(
                root,
                "job",
                new CommitManifest(
                    "10",
                    "result-10",
                    "base",
                    "tree",
                    new[]
                    {
                        new CommitManifestFile(
                            "src/sample.txt",
                            "MODIFY",
                            null,
                            expectedBytes.LongLength,
                            expectedSha,
                            true,
                            "new")
                    }));

            await File.WriteAllBytesAsync(target, expectedBytes);
            await File.WriteAllTextAsync(Path.Combine(root, "unexpected.txt"), "unexpected");

            var result = await ledger.VerifyAndRecordAsync(
                CreateMaterializeItem(9, new[] { "10" }),
                new[]
                {
                    new WorkItemDependencyResult(
                        "10",
                        "result-10",
                        "완료",
                        WorkItemResultType.CodeChange,
                        manifestPath)
                },
                before.Snapshot!);

            Assert.False(result.Success);
            Assert.Contains("manifest에 없는", result.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InvalidLedgerEntriesAreIgnoredAndDoNotHideLaterValidVerification()
    {
        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "src", "sample.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, "verified");

            var directory = Path.Combine(
                root,
                ".projecthub",
                "materialization-ledger",
                "job");
            Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(
                Path.Combine(directory, "000000000001-8.json"),
                """
                {
                  "jobId": "job",
                  "workItemId": "8",
                  "invocation": 1,
                  "recordedAtUtc": "2026-10-02T00:00:00Z",
                  "success": true,
                  "errorCode": null,
                  "files": [],
                  "unexpectedChangedPaths": [],
                  "detail": "missing sourceResultRefs"
                }
                """);

            await File.WriteAllTextAsync(
                Path.Combine(directory, "000000000002-8.json"),
                """
                {
                  "jobId": "job",
                  "workItemId": "8",
                  "invocation": 2,
                  "recordedAtUtc": "2026-10-02T00:00:01Z",
                  "success": true,
                  "errorCode": null,
                  "sourceResultRefs": ["result-10"],
                  "unexpectedChangedPaths": [],
                  "detail": "missing files"
                }
                """);

            var ledger = new TargetWorkspaceMaterializationLedger(root, "job");
            Assert.False(ledger.IsResultVerified("result-10"));

            var bytes = Encoding.UTF8.GetBytes("verified");
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var valid = new MaterializationLedgerEntry(
                "job",
                FixedWorkItemSlots.Materialize,
                3,
                DateTimeOffset.UtcNow,
                true,
                null,
                new[] { "result-10" },
                new[]
                {
                    new MaterializationFileRecord(
                        "src/sample.txt",
                        "MODIFY",
                        "10",
                        "result-10",
                        sha,
                        sha,
                        bytes.LongLength,
                        true)
                },
                Array.Empty<string>(),
                "verified");

            await File.WriteAllTextAsync(
                Path.Combine(directory, "000000000003-8.json"),
                JsonSerializer.Serialize(
                    valid,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        WriteIndented = true
                    }));

            Assert.True(ledger.IsResultVerified("result-10"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static WorkItemSnapshot CreateMaterializeItem(
        long createdOrder,
        IReadOnlyList<string> dependencies)
        => new(
            FixedWorkItemSlots.Materialize,
            "완료 결과를 반영한다.",
            dependencies,
            WorkItemKind.Normal,
            WorkItemState.Running,
            createdOrder,
            "base",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

    private static async Task<string> WriteManifestAsync(
        string root,
        string job,
        CommitManifest manifest)
    {
        var directory = Path.Combine(root, ".projecthub", "commit-manifests", job);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, manifest.WorkItemId + ".json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
        return path;
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "projecthub-materialization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
