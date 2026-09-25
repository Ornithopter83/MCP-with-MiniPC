using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;

namespace ProjectHub.Worker;

public sealed record WorkGraphPromptContext(
    long Revision,
    int MaxConcurrentWork,
    string BaseRef);

public sealed record WorkItemDependencyPromptContext(
    string WorkItemId,
    string? ResultRef,
    string? ResultSummary);

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
    public static string LoadHqFooter(bool parallelWorkGraph = false)
        => ApplyConditionalSection(Load("HQ-ROUTING-CONTRACT.md"), "PARALLEL", parallelWorkGraph);

    public static string LoadWorkFooter(bool judgeAvailable, bool parallelWorkItem = false)
    {
        var value = ApplyConditionalSection(Load("WORK-ROUTING-CONTRACT.md"), "JUDGE", judgeAvailable);
        return ApplyConditionalSection(value, "PARALLEL", parallelWorkItem);
    }

    public static string LoadJudgeFooter() => Load("JUDGE-ROUTING-CONTRACT.md");

    public static string BuildHqPrompt(
        string inboundType,
        string body,
        WorkGraphPromptContext? workGraph = null)
    {
        var graphHeader = workGraph is null
            ? "병렬 WorkGraph 사용: 아니오\n"
            : $"병렬 WorkGraph 사용: 예\nWorkGraph revision: {workGraph.Revision}\n최대 동시 WORK: {workGraph.MaxConcurrentWork}\n기준 ref: {workGraph.BaseRef}\n";
        var header = $"역할: HQ\n입력 유형: {inboundType}\n허용 목적지: WORK\n{graphHeader}\n입력 본문:\n";
        return header + body + "\n\n" + LoadHqFooter(workGraph is not null);
    }

    public static string BuildWorkPrompt(
        string inboundType,
        string body,
        bool judgeAvailable,
        string? observationRequestDirectory = null,
        WorkItemPromptContext? workItem = null)
    {
        var observationHeader = string.IsNullOrWhiteSpace(observationRequestDirectory)
            ? string.Empty
            : $"비동기 계측 요청 폴더: {observationRequestDirectory}\n";
        var workItemHeader = workItem is null
            ? "병렬 WorkItem 사용: 아니오\n"
            : BuildWorkItemHeader(workItem);
        var header = $"역할: WORK\n입력 유형: {inboundType}\n판정 사용 가능: {(judgeAvailable ? "예" : "아니오")}\n리소스 사용 가능: 예\n{workItemHeader}{observationHeader}\n입력 본문:\n";
        return header + body + "\n\n" + LoadWorkFooter(judgeAvailable, workItem is not null);
    }

    private static string BuildWorkItemHeader(WorkItemPromptContext workItem)
    {
        var dependencies = workItem.Dependencies.Count == 0
            ? "없음"
            : string.Join(", ", workItem.Dependencies);
        var previous = string.IsNullOrWhiteSpace(workItem.PreviousReport)
            ? string.Empty
            : $"이전 WorkItem 보고:\n{workItem.PreviousReport}\n";
        var dependencyResults = workItem.DependencyResults is null || workItem.DependencyResults.Count == 0
            ? string.Empty
            : "선행 WorkItem 결과:\n" + string.Join("\n", workItem.DependencyResults.Select(result =>
                $"- {result.WorkItemId} | ref={result.ResultRef ?? "없음"} | report={result.ResultSummary ?? "없음"}")) + "\n";

        return
            "병렬 WorkItem 사용: 예\n" +
            $"workItemId: {workItem.WorkItemId}\n" +
            $"workItemKind: {workItem.Kind.ToString().ToUpperInvariant()}\n" +
            $"WorkItem 목표: {workItem.Goal}\n" +
            $"선행 WorkItem: {dependencies}\n" +
            $"기준 ref: {workItem.BaseRef ?? "없음"}\n" +
            $"branch: {workItem.Branch ?? "미배정"}\n" +
            $"worktree: {workItem.WorktreePath ?? "미배정"}\n" +
            dependencyResults +
            previous;
    }

    private static string ApplyConditionalSection(string value, string key, bool enabled)
    {
        var removePattern = enabled
            ? $@"(?s)\{{\{{{key}_OFF\}}\}}.*?\{{\{{/{key}_OFF\}}\}}"
            : $@"(?s)\{{\{{{key}_ON\}}\}}.*?\{{\{{/{key}_ON\}}\}}";
        value = Regex.Replace(value, removePattern, string.Empty);
        return Regex.Replace(value, $@"\{{\{{/?{key}_(?:ON|OFF)\}}\}}", string.Empty).Trim();
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
