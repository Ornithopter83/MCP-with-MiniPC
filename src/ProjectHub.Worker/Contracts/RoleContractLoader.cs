using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ProjectHub.Worker;

public sealed record WorkGraphPromptContext(long Revision,int MaxConcurrentWork,string BaseRef);
public sealed record WorkItemDependencyPromptContext(
    string WorkItemId,
    string? ResultRef,
    string? ResultSummary,
    WorkItemResultType ResultType = WorkItemResultType.None,
    string? CommitManifestPath = null,
    string? IntegrationSnapshotPath = null);
public sealed record WorkItemPromptContext(
    string WorkItemId,
    WorkItemKind Kind,
    string Goal,
    IReadOnlyList<string> Dependencies,
    string? BaseRef,
    string? Branch,
    string? WorktreePath,
    string? PreviousReport = null,
    IReadOnlyList<WorkItemDependencyPromptContext>? DependencyResults = null);

public static class RoleContractLoader
{
    public static string LoadHqFooter() => Load("HQ-ROUTING-CONTRACT.md");
    public static string LoadWorkFooter() => Load("WORK-ROUTING-CONTRACT.md");

    public static string BuildHqPrompt(
        string inboundType,
        string body,
        WorkGraphPromptContext workGraph,
        bool includeContract = true)
    {
        ArgumentNullException.ThrowIfNull(workGraph);
        var header =
            $"역할: HQ\n입력 유형: {inboundType}\n" +
            $"WorkGraph revision: {workGraph.Revision}\n" +
            $"최대 동시 WORK: {workGraph.MaxConcurrentWork}\n" +
            $"기준 ref: {workGraph.BaseRef}\n입력 본문:\n";
        var prompt = header + body;
        return includeContract
            ? prompt + "\n\n" + LoadHqFooter()
            : prompt;
    }

    public static string BuildWorkPrompt(
        string inboundType,
        string body,
        WorkItemPromptContext workItem,
        string? observationRequestDirectory = null,
        bool includeContract = true,
        string? resourceStagingRoot = null)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        var observationHeader = string.IsNullOrWhiteSpace(observationRequestDirectory)
            ? string.Empty
            : $"비동기 계측 요청 폴더: {observationRequestDirectory}\n";
        var resourceHeader = string.IsNullOrWhiteSpace(resourceStagingRoot)
            ? string.Empty
            : $"공용 생성 리소스 임시 루트: {resourceStagingRoot}\n" +
              "RESOURCE 타입 하위 폴더: IMAGE=image, AUDIO=audio, VIDEO=video, DOCUMENT=document, FILE=file\n";
        var header =
            $"역할: WORK\n입력 유형: {inboundType}\n" +
            BuildWorkItemHeader(workItem) +
            observationHeader +
            resourceHeader +
            "\n입력 본문:\n";
        var prompt = header + body;
        return includeContract
            ? prompt + "\n\n" + LoadWorkFooter()
            : prompt;
    }

    private static string BuildWorkItemHeader(WorkItemPromptContext workItem)
    {
        var dependencies = workItem.Dependencies.Count == 0 ? "없음" : string.Join(", ", workItem.Dependencies);
        var previous = string.IsNullOrWhiteSpace(workItem.PreviousReport) ? string.Empty : $"이전 WorkItem 보고:\n{workItem.PreviousReport}\n";
        var dependencyResults = workItem.DependencyResults is null || workItem.DependencyResults.Count == 0
            ? string.Empty
            : "선행 WorkItem 결과:\n" + string.Join("\n", workItem.DependencyResults.Select(FormatDependencyResult)) + "\n";
        return
            $"workItemId: {workItem.WorkItemId}\n" +
            $"workItemKind: {workItem.Kind.ToString().ToUpperInvariant()}\n" +
            $"WorkItem 목표: {workItem.Goal}\n" +
            $"선행 WorkItem: {dependencies}\n" +
            $"기준 ref: {workItem.BaseRef ?? "없음"}\n" +
            $"branch: {workItem.Branch ?? "미배정"}\n" +
            $"worktree: {workItem.WorktreePath ?? "미배정"}\n" +
            dependencyResults + previous;
    }

    private static string FormatDependencyResult(WorkItemDependencyPromptContext result)
    {
        var header =
            $"- {result.WorkItemId} | resultType={WorkItemResultTypeContract.ToToken(result.ResultType)} | ref={result.ResultRef ?? "없음"} | commitManifest={result.CommitManifestPath ?? "없음"} | snapshot={result.IntegrationSnapshotPath ?? "없음"} | report={result.ResultSummary ?? "없음"}";

        if (string.IsNullOrWhiteSpace(result.CommitManifestPath))
            return header;

        try
        {
            if (!File.Exists(result.CommitManifestPath))
                return header + "\n  commitManifestSummary: [파일 없음]";

            using var stream = File.OpenRead(result.CommitManifestPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var commit = root.TryGetProperty("commit", out var commitElement)
                ? LimitManifestValue(commitElement.GetString())
                : "없음";
            if (!root.TryGetProperty("changedFiles", out var changedFiles) ||
                changedFiles.ValueKind != JsonValueKind.Array)
                return header + $"\n  commitManifestSummary: commit={commit} changedFiles=알 수 없음";

            const int maxFiles = 40;
            var count = changedFiles.GetArrayLength();
            var summaryLines = new List<string>
            {
                $"  commitManifestSummary: commit={commit} changedFiles={count}"
            };

            for (var index = 0; index < Math.Min(count, maxFiles); index++)
            {
                var file = changedFiles[index];
                var changeType = file.TryGetProperty("changeType", out var changeTypeElement)
                    ? LimitManifestValue(changeTypeElement.GetString(), 32)
                    : "UNKNOWN";
                var path = file.TryGetProperty("path", out var pathElement)
                    ? LimitManifestValue(pathElement.GetString())
                    : "경로 없음";
                summaryLines.Add($"    - {changeType} {path}");
            }

            if (count > maxFiles)
                summaryLines.Add($"    - ... +{count - maxFiles} files");

            return header + "\n" + string.Join("\n", summaryLines);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return header + "\n  commitManifestSummary: [읽기 실패: " + exception.Message + "]";
        }
    }

    private static string LimitManifestValue(string? value, int maxLength = 180)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "없음"
            : value.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "…";
    }

    private static string Load(string fileName)
    {
        var name = $"ProjectHub.Worker.Contracts.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"역할 계약 리소스를 찾을 수 없습니다: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
