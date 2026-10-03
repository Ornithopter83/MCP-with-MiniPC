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
    IReadOnlyList<WorkItemDependencyPromptContext>? DependencyResults = null,
    IReadOnlyList<string>? Checklist = null);

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
            $"기준 ref: {workGraph.BaseRef}\n" +
            "computerUse: disabled\n입력 본문:\n";
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
        string? resourceStagingRoot = null,
        string? workTempRoot = null,
        string? targetWorkspace = null,
        string? publishOutputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        var observationHeader = string.IsNullOrWhiteSpace(observationRequestDirectory)
            ? string.Empty
            : $"비동기 계측 요청 폴더: {observationRequestDirectory}\n";
        var resourceHeader = string.IsNullOrWhiteSpace(resourceStagingRoot)
            ? string.Empty
            : $"공용 생성 리소스 임시 루트: {resourceStagingRoot}\n" +
              "RESOURCE 타입 하위 폴더: IMAGE=image, AUDIO=audio, VIDEO=video, DOCUMENT=document, FILE=file\n";
        var workTempHeader = string.IsNullOrWhiteSpace(workTempRoot)
            ? string.Empty
            : $"WORK 임시 산출물 루트: {workTempRoot}\n";
        var targetWorkspaceHeader = string.IsNullOrWhiteSpace(targetWorkspace)
            ? string.Empty
            : $"대상 프로젝트 루트: {targetWorkspace}\n";
        var publishOutputHeader = string.IsNullOrWhiteSpace(publishOutputDirectory)
            ? string.Empty
            : $"최종 게시 산출물 루트(게시·export 결과는 이 경로에 저장): {publishOutputDirectory}\n";
        var header =
            $"역할: WORK\n입력 유형: {inboundType}\n" +
            BuildWorkItemHeader(workItem) +
            observationHeader +
            resourceHeader +
            workTempHeader +
            targetWorkspaceHeader +
            publishOutputHeader +
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
        var checklist = workItem.Checklist is null || workItem.Checklist.Count == 0
            ? $"[1] {workItem.Goal}\n"
            : string.Join("\n", workItem.Checklist.Select((item, index) => $"[{index + 1}] {item}")) + "\n";
        var dependencyResults = workItem.DependencyResults is null || workItem.DependencyResults.Count == 0
            ? string.Empty
            : "선행 WorkItem 결과:\n" + string.Join("\n", workItem.DependencyResults.Select(FormatDependencyResult)) + "\n";
        return
            $"workItemId: {workItem.WorkItemId}\n" +
            $"workItemKind: {workItem.Kind.ToString().ToUpperInvariant()}\n" +
            $"WorkItem 목표: {workItem.Goal}\n" +
            "WorkItem 작업 목록:\n" + checklist +
            "computerUse: disabled\n" +
            $"선행 WorkItem: {dependencies}\n" +
            $"기준 ref: {workItem.BaseRef ?? "없음"}\n" +
            $"branch: {workItem.Branch ?? "미배정"}\n" +
            $"worktree: {workItem.WorktreePath ?? "미배정"}\n" +
            dependencyResults + previous;
    }

    private static string FormatDependencyResult(WorkItemDependencyPromptContext result)
    {
        var header =
            $"- workItemId={result.WorkItemId} resultType={WorkItemResultTypeContract.ToToken(result.ResultType)} resultRef={result.ResultRef ?? "없음"} manifest={result.CommitManifestPath ?? "없음"} snapshot={result.IntegrationSnapshotPath ?? "없음"}";

        if (string.IsNullOrWhiteSpace(result.ResultSummary))
            return header;

        return header + Environment.NewLine +
               "  report:" + Environment.NewLine +
               result.ResultSummary.Trim();
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
