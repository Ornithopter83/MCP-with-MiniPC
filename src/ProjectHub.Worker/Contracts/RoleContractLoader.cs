using System.IO;
using System.Reflection;

namespace ProjectHub.Worker;

public sealed record WorkGraphPromptContext(long Revision,int MaxConcurrentWork,string BaseRef);
public sealed record WorkItemDependencyPromptContext(string WorkItemId,string? ResultRef,string? ResultSummary);
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
        bool includeContract = true)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        var observationHeader = string.IsNullOrWhiteSpace(observationRequestDirectory)
            ? string.Empty
            : $"비동기 계측 요청 폴더: {observationRequestDirectory}\n";
        var header =
            $"역할: WORK\n입력 유형: {inboundType}\n" +
            BuildWorkItemHeader(workItem) +
            observationHeader +
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
            : "선행 WorkItem 결과:\n" + string.Join("\n", workItem.DependencyResults.Select(result =>
                $"- {result.WorkItemId} | ref={result.ResultRef ?? "없음"} | report={result.ResultSummary ?? "없음"}")) + "\n";
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

    private static string Load(string fileName)
    {
        var name = $"ProjectHub.Worker.Contracts.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"역할 계약 리소스를 찾을 수 없습니다: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
