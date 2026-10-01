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
        string? targetWorkspace = null)
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
        var fixedMissionHeader = BuildFixedMissionHeader(
            workItem.WorkItemId,
            targetWorkspace,
            workTempRoot);
        var header =
            $"역할: WORK\n입력 유형: {inboundType}\n" +
            BuildWorkItemHeader(workItem) +
            observationHeader +
            resourceHeader +
            workTempHeader +
            fixedMissionHeader +
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

    private static string BuildFixedMissionHeader(
        string workItemId,
        string? targetWorkspace,
        string? workTempRoot)
    {
        if (string.Equals(workItemId, FixedWorkItemSlots.Materialize, StringComparison.Ordinal))
        {
            return
                "고정 임무: #8 MATERIALIZE / COPY\n" +
                $"대상 프로젝트 루트: {targetWorkspace ?? "미지정"}\n" +
                "이 WorkItem은 완료된 선행 WorkItem의 결과를 대상 프로젝트 루트에 반영하는 단발 작업이다.\n" +
                "Commit Manifest가 있으면 변경 상대경로를 기준으로 부모 폴더를 만들고 같은 상대경로를 그대로 유지해 복사한다. 경로를 평탄화하거나 임의로 이름을 바꾸지 않는다.\n" +
                "DELETE 항목은 같은 상대경로의 대상 파일만 제거한다. 선행 결과에서 명시된 게시 산출물도 HQ가 지정한 상대경로 그대로 옮긴다.\n" +
                "파일 내용의 의미 수정, 기능 구현, 빌드, .git 또는 .projecthub 조작은 하지 않는다.\n";
        }

        if (string.Equals(workItemId, FixedWorkItemSlots.BuildPublish, StringComparison.Ordinal))
        {
            return
                "고정 임무: #9 BUILD / PUBLISH\n" +
                $"대상 프로젝트 루트: {targetWorkspace ?? "미지정"}\n" +
                $"게시 임시 루트: {(string.IsNullOrWhiteSpace(workTempRoot) ? "미지정" : Path.Combine(workTempRoot, "publish"))}\n" +
                "이 WorkItem은 대상 프로젝트 루트에 현재 반영된 파일을 기준으로 빌드·export·publish를 수행하는 단발 작업이다.\n" +
                "빌드 도구가 요구하는 cache와 생성 파일은 대상 프로젝트 루트에 만들 수 있지만 기능 구현이나 소스 의미 변경은 하지 않는다. .git은 수정하지 않는다.\n" +
                "배포 산출물은 작업 목록에서 다른 위치를 명시하지 않았다면 게시 임시 루트에 두고, 정확한 경로·크기·가능한 경우 SHA-256을 보고해 후속 검증과 #8 이동에 사용한다.\n";
        }

        return string.Empty;
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
