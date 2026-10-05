using System.IO;
using System.Reflection;

namespace ProjectHub.Worker;

public static class RoleContractLoader
{
    public static string LoadHqFooter() => Load("HQ-ROUTING-CONTRACT.md");
    public static string LoadManagerFooter() => Load("MANAGER-ROUTING-CONTRACT.md");
    public static string LoadWorkFooter() => Load("WORK-ROUTING-CONTRACT.md");
    public static string LoadQaFooter() => Load("QA-ROUTING-CONTRACT.md");
    public static string LoadHighFooter() => Load("HIGH-ROUTING-CONTRACT.md");

    public static string BuildDirectWorkPrompt(
        string workItemId,
        string body,
        IReadOnlyList<string> writePaths,
        string targetWorkspace,
        string? resourceStagingRoot = null,
        string? workTempRoot = null)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            throw new ArgumentException("WorkItem ID가 비어 있습니다.", nameof(workItemId));
        if (writePaths is null || writePaths.Count == 0)
            throw new ArgumentException("WRITE_PATH가 비어 있습니다.", nameof(writePaths));

        var resourceHeader = string.IsNullOrWhiteSpace(resourceStagingRoot)
            ? string.Empty
            : $"RESOURCE 임시 루트: {resourceStagingRoot}\n" +
              "RESOURCE 최종 반영은 Worker가 HQ 지정 경로로 move하며 GENERAL WORK가 임의 처리하지 않는다.\n";
        var workTempHeader = string.IsNullOrWhiteSpace(workTempRoot)
            ? string.Empty
            : $"WORK 임시 산출물 루트: {workTempRoot}\n";
        var writePathHeader =
            "허용 WRITE_PATH:\n" +
            string.Join("\n", writePaths.Select(path => "- " + path)) +
            "\n";
        var header =
            "역할: WORK\n" +
            "입력 유형: MILESTONE_WORK\n" +
            $"workItemId: {workItemId}\n" +
            $"대상 프로젝트 루트: {targetWorkspace}\n" +
            "실제 프로젝트 폴더·파일은 이 루트에서 직접 작업한다. clone/worktree/별도 branch를 만들지 않고 Git commit·push는 수행하지 않는다.\n" +
            writePathHeader +
            resourceHeader +
            workTempHeader +
            "\n입력 본문:\n";

        return header +
               (body ?? string.Empty) +
               "\n\n" +
               LoadWorkFooter();
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

    private static string Load(string fileName)
    {
        var name = $"ProjectHub.Worker.Contracts.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"역할 계약 리소스를 찾을 수 없습니다: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
