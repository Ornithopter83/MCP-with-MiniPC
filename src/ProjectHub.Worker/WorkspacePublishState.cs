using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record WorkspacePublishStateSnapshot(
    long CodeGeneration,
    long PublishedCodeGeneration,
    bool HasSuccessfulPublish,
    string? LastCodeResultRef,
    long? LastPublishInvocation,
    DateTimeOffset? LastPublishedAtUtc)
{
    public bool IsStale
        => HasSuccessfulPublish && PublishedCodeGeneration < CodeGeneration;
}

public sealed class WorkspacePublishState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WorkspacePublishState(string workspace, string jobId)
    {
        if (string.IsNullOrWhiteSpace(workspace))
            throw new ArgumentException("사용자 작업 폴더가 비어 있습니다.", nameof(workspace));
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID가 비어 있습니다.", nameof(jobId));

        var directory = Path.Combine(
            Path.GetFullPath(workspace),
            ".projecthub",
            "publish-state");
        _path = Path.Combine(directory, SafePathComponent(jobId.Trim()) + ".json");
    }

    public async Task<WorkspacePublishStateSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspacePublishStateSnapshot> MarkCodeMaterializedAsync(
        string resultRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resultRef))
            throw new ArgumentException("CODE_CHANGE resultRef가 비어 있습니다.", nameof(resultRef));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var normalized = resultRef.Trim();
            if (string.Equals(
                    current.LastCodeResultRef,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
                return current;

            var next = current with
            {
                CodeGeneration = checked(current.CodeGeneration + 1),
                LastCodeResultRef = normalized
            };
            await WriteCoreAsync(next, cancellationToken).ConfigureAwait(false);
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspacePublishStateSnapshot> MarkPublishedAsync(
        long invocation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            var next = current with
            {
                PublishedCodeGeneration = current.CodeGeneration,
                HasSuccessfulPublish = true,
                LastPublishInvocation = invocation,
                LastPublishedAtUtc = DateTimeOffset.UtcNow
            };
            await WriteCoreAsync(next, cancellationToken).ConfigureAwait(false);
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkspacePublishStateSnapshot> ReadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return Empty();

        try
        {
            var json = await File.ReadAllTextAsync(
                _path,
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WorkspacePublishStateSnapshot>(
                       json,
                       JsonOptions)
                   ?? Empty();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "publish state JSON이 손상되었습니다.",
                exception);
        }
    }

    private async Task WriteCoreAsync(
        WorkspacePublishStateSnapshot state,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("publish state 폴더를 계산하지 못했습니다.");
        Directory.CreateDirectory(directory);

        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(
                temporary,
                json,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (IOException)
            {
            }
        }
    }

    private static WorkspacePublishStateSnapshot Empty()
        => new(
            CodeGeneration: 0,
            PublishedCodeGeneration: 0,
            HasSuccessfulPublish: false,
            LastCodeResultRef: null,
            LastPublishInvocation: null,
            LastPublishedAtUtc: null);

    private static string SafePathComponent(string value)
    {
        var cleaned = new string(
            value
                .Select(character =>
                    char.IsLetterOrDigit(character) || character is '-' or '_'
                        ? character
                        : '-')
                .ToArray())
            .Trim('-');

        return string.IsNullOrWhiteSpace(cleaned)
            ? "job"
            : cleaned;
    }
}
