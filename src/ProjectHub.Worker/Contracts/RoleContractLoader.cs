using System.IO;
using System.Reflection;

namespace ProjectHub.Worker;

public static class RoleContractLoader
{
    public const string HqContractPath =
        "src/ProjectHub.Worker/Contracts/HQ-ROUTING-CONTRACT.md";
    public const string ManagerContractPath =
        "src/ProjectHub.Worker/Contracts/MANAGER-ROUTING-CONTRACT.md";
    public const string WorkContractPath =
        "src/ProjectHub.Worker/Contracts/WORK-ROUTING-CONTRACT.md";
    public const string QaContractPath =
        "src/ProjectHub.Worker/Contracts/QA-ROUTING-CONTRACT.md";
    public const string HighContractPath =
        "src/ProjectHub.Worker/Contracts/HIGH-ROUTING-CONTRACT.md";

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
        bool readOnly = false,
        string? resourceStagingRoot = null,
        string? workTempRoot = null)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            throw new ArgumentException("WorkItem ID가 비어 있습니다.", nameof(workItemId));
        if (writePaths is null)
            throw new ArgumentNullException(nameof(writePaths));
        if (!readOnly && writePaths.Count == 0)
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
            (writePaths.Count == 0
                ? "- 없음 (읽기 전용)\n"
                : string.Join("\n", writePaths.Select(path => "- " + path)) + "\n");
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

    public static string BuildManagerPrompt(
        string body,
        bool includeFullContract = true) =>
        "역할: #1 중간관리자\n입력 본문:\n" +
        (body ?? string.Empty) +
        "\n\n" +
        (includeFullContract
            ? LoadManagerFooter()
            : BuildContractReference(ManagerContractPath));

    public static string BuildQaPrompt(string body) =>
        "역할: QA\n호출 유형: HQ_SCHEDULED_QA\n입력 본문:\n" +
        (body ?? string.Empty) + "\n\n" + LoadQaFooter();

    public static string BuildHighPrompt(string body) =>
        "역할: HIGH\n호출 유형: MILESTONE_VALIDATION\n입력 본문:\n" +
        (body ?? string.Empty) + "\n\n" + LoadHighFooter();

    public static string BuildContractReference(string repositoryPath) =>
        "계약 참조: ProjectHub Git 저장소의 " +
        repositoryPath +
        " 파일을 현재 역할 계약 원본으로 계속 적용한다. " +
        "이 세션 최초 호출에서 계약 전문을 직접 주입받았으며, " +
        "후속 호출에서는 같은 전문을 반복하지 않는다. " +
        "Worker가 갱신된 전문을 다시 주입하기 전까지 최초 주입된 계약을 유지한다.";

    public static string BuildHistoryPrompt(string prompt)
    {
        var value = prompt ?? string.Empty;
        var contracts = new[]
        {
            (HqContractPath, LoadHqFooter()),
            (ManagerContractPath, LoadManagerFooter()),
            (WorkContractPath, LoadWorkFooter()),
            (QaContractPath, LoadQaFooter()),
            (HighContractPath, LoadHighFooter())
        };

        foreach (var (path, contract) in contracts)
        {
            value = value.Replace(
                contract,
                string.Empty,
                StringComparison.Ordinal);
            value = value.Replace(
                BuildContractReference(path),
                string.Empty,
                StringComparison.Ordinal);
        }

        while (value.Contains(
                   Environment.NewLine + Environment.NewLine + Environment.NewLine,
                   StringComparison.Ordinal))
        {
            value = value.Replace(
                Environment.NewLine + Environment.NewLine + Environment.NewLine,
                Environment.NewLine + Environment.NewLine,
                StringComparison.Ordinal);
        }

        return value.Trim();
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
