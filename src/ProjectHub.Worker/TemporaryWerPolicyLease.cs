using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ProjectHub.Worker;

internal sealed class TemporaryWerPolicyLease : IDisposable
{
    internal const string RegistryPath = @"Software\Microsoft\Windows\Windows Error Reporting";
    internal const string DontShowUiValueName = "DontShowUI";
    internal const string DisabledValueName = "Disabled";
    internal const int ForcedValue = 1;

    private const int JournalVersion = 1;
    private static readonly object Gate = new();
    private static ActiveLeaseState? _active;

    private readonly string _leaseId;
    private bool _disposed;

    private TemporaryWerPolicyLease(string leaseId)
    {
        _leaseId = leaseId;
    }

    internal static string JournalPath =>
        Path.Combine(WorkerPaths.State, "wer-policy-lease.json");

    public static IDisposable Acquire(string owner)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WER policy lease is supported only on Windows.");

        lock (Gate)
        {
            if (_active is not null)
            {
                _active.ReferenceCount++;
                WriteAudit("ACQUIRE_NESTED", _active.Journal.LeaseId, owner);
                return new TemporaryWerPolicyLease(_active.Journal.LeaseId);
            }

            RecoverStaleLeaseCore();

            using var existingKey = Registry.CurrentUser.OpenSubKey(
                RegistryPath,
                writable: false);
            var journal = new WerPolicyJournal(
                JournalVersion,
                Guid.NewGuid().ToString("N"),
                string.IsNullOrWhiteSpace(owner) ? "build-publish" : owner.Trim(),
                DateTimeOffset.UtcNow,
                existingKey is not null,
                CaptureValue(existingKey, DontShowUiValueName),
                CaptureValue(existingKey, DisabledValueName));

            WriteJournal(journal);

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(
                    RegistryPath,
                    writable: true)
                    ?? throw new InvalidOperationException(
                        "Windows Error Reporting 사용자 정책 키를 열지 못했습니다.");

                key.SetValue(
                    DontShowUiValueName,
                    ForcedValue,
                    RegistryValueKind.DWord);
                key.SetValue(
                    DisabledValueName,
                    ForcedValue,
                    RegistryValueKind.DWord);
                key.Flush();
            }
            catch
            {
                try
                {
                    RestoreJournal(journal);
                    DeleteJournal();
                }
                catch
                {
                    // 복원에 실패하면 journal을 남겨 다음 Worker 시작에서 재시도한다.
                }

                throw;
            }

            _active = new ActiveLeaseState(journal, 1);
            WriteAudit("ACQUIRE", journal.LeaseId, journal.Owner);
            return new TemporaryWerPolicyLease(journal.LeaseId);
        }
    }

    internal static void RecoverStaleLease()
    {
        if (!OperatingSystem.IsWindows())
            return;

        lock (Gate)
        {
            if (_active is not null)
                return;

            RecoverStaleLeaseCore();
        }
    }

    internal static void RestoreActiveLeaseForShutdown()
    {
        if (!OperatingSystem.IsWindows())
            return;

        lock (Gate)
        {
            if (_active is null)
                return;

            var journal = _active.Journal;
            RestoreJournal(journal);
            DeleteJournal();
            _active = null;
            WriteAudit("RESTORE_SHUTDOWN", journal.LeaseId, journal.Owner);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (Gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_active is null ||
                !string.Equals(
                    _active.Journal.LeaseId,
                    _leaseId,
                    StringComparison.Ordinal))
                return;

            _active.ReferenceCount--;
            if (_active.ReferenceCount > 0)
            {
                WriteAudit(
                    "RELEASE_NESTED",
                    _active.Journal.LeaseId,
                    _active.Journal.Owner);
                return;
            }

            var journal = _active.Journal;
            RestoreJournal(journal);
            DeleteJournal();
            _active = null;
            WriteAudit("RESTORE", journal.LeaseId, journal.Owner);
        }
    }

    private static void RecoverStaleLeaseCore()
    {
        CleanupStaleJournalTemps();

        if (!File.Exists(JournalPath))
            return;

        var journal = ReadJournal();
        RestoreJournal(journal);
        DeleteJournal();
        WriteAudit("RECOVER_STALE", journal.LeaseId, journal.Owner);
    }

    private static WerPolicyJournal ReadJournal()
    {
        var json = File.ReadAllText(JournalPath, Encoding.UTF8);
        var journal = JsonSerializer.Deserialize<WerPolicyJournal>(json)
            ?? throw new InvalidDataException("WER policy journal을 읽지 못했습니다.");

        if (journal.Version != JournalVersion ||
            string.IsNullOrWhiteSpace(journal.LeaseId))
        {
            throw new InvalidDataException(
                "WER policy journal 버전 또는 lease ID가 올바르지 않습니다.");
        }

        return journal;
    }

    private static void WriteJournal(WerPolicyJournal journal)
    {
        Directory.CreateDirectory(WorkerPaths.State);
        CleanupStaleJournalTemps();

        var json = ProjectHubJson.SerializeIndented(journal);
        var tempPath = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, json, ProjectHubJson.Utf8NoBom);
            File.Move(tempPath, JournalPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static void CleanupStaleJournalTemps()
    {
        try
        {
            if (!Directory.Exists(WorkerPaths.State))
                return;

            foreach (var path in Directory.EnumerateFiles(
                         WorkerPaths.State,
                         "wer-policy-lease.json.*.tmp",
                         SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(path); }
                catch { }
            }
        }
        catch
        {
        }
    }

    private static void DeleteJournal()
    {
        if (File.Exists(JournalPath))
            File.Delete(JournalPath);
    }

    private static RegistryValueBackup CaptureValue(
        RegistryKey? key,
        string valueName)
    {
        if (key is null ||
            !key.GetValueNames().Contains(
                valueName,
                StringComparer.OrdinalIgnoreCase))
        {
            return new RegistryValueBackup(
                Exists: false,
                RegistryValueKind.None,
                Value: null);
        }

        var kind = key.GetValueKind(valueName);
        var value = key.GetValue(
            valueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);

        if (value is null)
        {
            return new RegistryValueBackup(
                Exists: true,
                kind,
                JsonSerializer.SerializeToElement(
                    string.Empty,
                    ProjectHubJson.CompactOptions));
        }

        return new RegistryValueBackup(
            Exists: true,
            kind,
            JsonSerializer.SerializeToElement(
                value,
                value.GetType(),
                ProjectHubJson.CompactOptions));
    }

    private static void RestoreJournal(WerPolicyJournal journal)
    {
        RegistryKey? key = null;
        try
        {
            key = Registry.CurrentUser.OpenSubKey(
                RegistryPath,
                writable: true);

            if (key is null &&
                (journal.KeyExisted ||
                 journal.DontShowUi.Exists ||
                 journal.Disabled.Exists))
            {
                key = Registry.CurrentUser.CreateSubKey(
                    RegistryPath,
                    writable: true);
            }

            if (key is null)
                return;

            RestoreValue(key, DontShowUiValueName, journal.DontShowUi);
            RestoreValue(key, DisabledValueName, journal.Disabled);
            key.Flush();

            var removeCreatedKey =
                !journal.KeyExisted &&
                key.GetValueNames().Length == 0 &&
                key.GetSubKeyNames().Length == 0;

            key.Dispose();
            key = null;

            if (removeCreatedKey)
            {
                Registry.CurrentUser.DeleteSubKey(
                    RegistryPath,
                    throwOnMissingSubKey: false);
            }
        }
        finally
        {
            key?.Dispose();
        }
    }

    private static void RestoreValue(
        RegistryKey key,
        string valueName,
        RegistryValueBackup backup)
    {
        if (!backup.Exists)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        if (backup.Value is null)
            throw new InvalidDataException(
                $"WER policy journal에 {valueName} 값이 없습니다.");

        key.SetValue(
            valueName,
            DeserializeRegistryValue(backup.Kind, backup.Value.Value),
            backup.Kind);
    }

    private static object DeserializeRegistryValue(
        RegistryValueKind kind,
        JsonElement value)
        => kind switch
        {
            RegistryValueKind.String or
            RegistryValueKind.ExpandString =>
                value.GetString() ?? string.Empty,
            RegistryValueKind.DWord =>
                value.GetInt32(),
            RegistryValueKind.QWord =>
                value.GetInt64(),
            RegistryValueKind.MultiString =>
                value.Deserialize<string[]>() ?? Array.Empty<string>(),
            RegistryValueKind.Binary or
            RegistryValueKind.None =>
                value.Deserialize<byte[]>() ?? Array.Empty<byte>(),
            _ => throw new InvalidDataException(
                $"지원하지 않는 registry value kind입니다: {kind}")
        };

    private static void WriteAudit(
        string action,
        string leaseId,
        string owner)
    {
        try
        {
            Directory.CreateDirectory(WorkerPaths.Logs);
            var line =
                $"{DateTimeOffset.UtcNow:O}\t{action}" +
                $"\tlease={leaseId}" +
                $"\towner={Sanitize(owner)}";
            File.AppendAllText(
                Path.Combine(WorkerPaths.Logs, "wer-policy-audit.log"),
                line + Environment.NewLine,
                ProjectHubJson.Utf8NoBom);
        }
        catch
        {
        }
    }

    private static string Sanitize(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        return normalized.Length <= 160
            ? normalized
            : normalized[..160];
    }

    private sealed class ActiveLeaseState
    {
        public ActiveLeaseState(
            WerPolicyJournal journal,
            int referenceCount)
        {
            Journal = journal;
            ReferenceCount = referenceCount;
        }

        public WerPolicyJournal Journal { get; }
        public int ReferenceCount { get; set; }
    }

    internal sealed record RegistryValueBackup(
        bool Exists,
        RegistryValueKind Kind,
        JsonElement? Value);

    internal sealed record WerPolicyJournal(
        int Version,
        string LeaseId,
        string Owner,
        DateTimeOffset CreatedAt,
        bool KeyExisted,
        RegistryValueBackup DontShowUi,
        RegistryValueBackup Disabled);
}
