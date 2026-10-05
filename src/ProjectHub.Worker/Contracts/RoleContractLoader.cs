using System.IO;
using System.Reflection;

namespace ProjectHub.Worker;

public sealed record WorkGraphPromptContext(long Revision,int MaxConcurrentWork,string BaseRef);
public sealed record WorkItemDependencyPromptContext(
    string WorkItemId,
    string? ResultRef,
    string? ResultSummary,
    WorkItemResultType ResultType = WorkItemResultType.None,
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
    public static string LoadManagerFooter() => Load("MANAGER-ROUTING-CONTRACT.md");
    public static string LoadWorkFooter() => Load("WORK-ROUTING-CONTRACT.md");
    public static string LoadQaFooter() => Load("QA-ROUTING-CONTRACT.md");
    public static string LoadHighFooter() => Load("HIGH-ROUTING-CONTRACT.md");

    public static string BuildHqPrompt(
        string inboundType,
        string body,
        WorkGraphPromptContext workGraph,
        bool includeContract = true)
    {
        ArgumentNullException.ThrowIfNull(workGraph);
        var header =
            $"역할: HQ\n입력 유형: {inboundType}\n" +
            $"상태 revision: {workGraph.Revision}\n" +
            $"최대 동시 WORK: {workGraph.MaxConcurrentWork}\n" +
            $"현재 Git 기준: {workGraph.BaseRef}\n" +
            "입력 본문:\n";
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
            : $"RESOURCE 임시 루트: {resourceStagingRoot}\n" +
              "RESOURCE 최종 반영은 HQ 지정 경로로 move하며 일반 WORK가 임의 처리하지 않는다.\n";
        var workTempHeader = string.IsNullOrWhiteSpace(workTempRoot)
            ? string.Empty
            : $"WORK 임시 산출물 루트: {workTempRoot}\n";
        var targetWorkspaceHeader = string.IsNullOrWhiteSpace(targetWorkspace)
            ? string.Empty
            : $"대상 프로젝트 루트: {targetWorkspace}\n" +
              "실제 프로젝트 폴더·파일은 이 루트에서 직접 작업한다. WorkItem별 clone/worktree/별도 branch를 만들지 않고 Git commit·push는 수행하지 않는다.\n";
        _ = publishOutputDirectory;
        var header =
            $"역할: WORK\n입력 유형: {inboundType}\n" +
            BuildWorkItemHeader(workItem) +
            observationHeader +
            resourceHeader +
            workTempHeader +
            targetWorkspaceHeader +
            "\n입력 본문:\n";
        var prompt = header + body;
        return includeContract
            ? prompt + "\n\n" + LoadWorkFooter()
            : prompt;
    }

    public static string BuildManagerPrompt(string body) =>
        "역할: #1 중간관리자\n입력 본문:\n" +
        (body ?? string.Empty) + "\n\n" + LoadManagerFooter();

    public static string BuildQaPrompt(string body) =>
        "역할: QA\n호출 유형: HQ_SCHEDULED_QA\n입력 본문:\n" +
        (body ?? string.Empty) + "\n\n" + LoadQaFooter();

    public static string BuildHighPrompt(string body) =>
        "역할: HIGH\n호출 유형: MILESTONE_VALIDATION\n입력 본문:\n" +
        (body ?? string.Empty) + "\n\n" + LoadHighFooter();

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
            $"선행 WorkItem: {dependencies}\n" +
            dependencyResults + previous;
    }

    private static string FormatDependencyResult(WorkItemDependencyPromptContext result)
    {
        var header =
            $"- workItemId={result.WorkItemId} resultType={WorkItemResultTypeContract.ToToken(result.ResultType)} resultRef={result.ResultRef ?? "없음"}";

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
