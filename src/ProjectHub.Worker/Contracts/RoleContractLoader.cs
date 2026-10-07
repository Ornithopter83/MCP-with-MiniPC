using System.IO;
using System.Reflection;

namespace ProjectHub.Worker;

public static class RoleContractLoader
{
    private const string EncodingGuidance =
        "문자 인코딩: 텍스트는 UTF-8을 사용하고 한글을 실제 Unicode 문자로 유지한다. " +
        "Windows PowerShell 5.1에서 텍스트를 읽거나 출력할 때는 UTF-8 출력 인코딩과 Get-Content -Encoding UTF8을 명시한다.\n";
    public const string HqContractPath =
        "src/ProjectHub.Worker/Contracts/HQ-ROUTING-CONTRACT.md";
    public const string WorkContractPath =
        "src/ProjectHub.Worker/Contracts/WORK-ROUTING-CONTRACT.md";
    public const string QaContractPath =
        "src/ProjectHub.Worker/Contracts/QA-ROUTING-CONTRACT.md";
    public const string HighContractPath =
        "src/ProjectHub.Worker/Contracts/HIGH-ROUTING-CONTRACT.md";

    public static string LoadHqFooter() => Load("HQ-ROUTING-CONTRACT.md");
    public static string LoadWorkFooter() => Load("WORK-ROUTING-CONTRACT.md");
    public static string LoadQaFooter() => Load("QA-ROUTING-CONTRACT.md");
    public static string LoadHighFooter() => Load("HIGH-ROUTING-CONTRACT.md");

    public static string BuildDirectWorkPrompt(
        string workItemId,
        string body,
        IReadOnlyList<string> writePaths,
        string targetWorkspace,
        bool readOnly = false)
    {
        if (string.IsNullOrWhiteSpace(workItemId))
            throw new ArgumentException("WorkItem ID가 비어 있습니다.", nameof(workItemId));
        if (writePaths is null)
            throw new ArgumentNullException(nameof(writePaths));
        if (!readOnly && writePaths.Count == 0)
            throw new ArgumentException("WRITE_PATH가 비어 있습니다.", nameof(writePaths));

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("[ACTION=WORK]");
        builder.AppendLine("WORK_ID: " + workItemId);
        builder.AppendLine("PROJECT_ROOT: " + targetWorkspace);
        builder.AppendLine();
        builder.AppendLine("@@WRITE_PATH");
        if (writePaths.Count == 0)
            builder.AppendLine("없음");
        else
            foreach (var path in writePaths)
                builder.AppendLine("- " + path);
        builder.AppendLine();
        builder.AppendLine(body ?? string.Empty);
        builder.AppendLine();
        builder.AppendLine(EncodingGuidance.TrimEnd());
        builder.AppendLine();
        builder.Append(LoadWorkFooter());
        return builder.ToString();
    }

    public static string BuildQaPrompt(string body) =>
        "[ACTION=QA]\n" +
        (body ?? string.Empty) +
        "\n\n" +
        EncodingGuidance +
        "\n" +
        LoadQaFooter();

    public static string BuildHighPrompt(string body) =>
        "[ACTION=HIGH]\n" +
        (body ?? string.Empty) +
        "\n\n" +
        EncodingGuidance +
        "\n" +
        LoadHighFooter();

    public static string BuildContractReference(string repositoryPath) =>
        "계약 참조: " +
        repositoryPath +
        " 역할 계약을 계속 적용한다. " +
        "이 세션 최초 호출에서 계약 전문을 직접 주입받았으며, " +
        "후속 호출에서는 같은 전문을 반복하지 않는다. " +
        "Worker가 갱신된 전문을 다시 주입하기 전까지 최초 주입된 계약을 유지한다.";

    public static string BuildHistoryPrompt(string prompt)
    {
        var value = prompt ?? string.Empty;
        var contracts = new[]
        {
            (HqContractPath, LoadHqFooter()),
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

        const string mechanicalSupplementMarker =
            "Worker 기계 실행 보충 계약:";
        var mechanicalSupplementIndex = value.IndexOf(
            mechanicalSupplementMarker,
            StringComparison.Ordinal);
        if (mechanicalSupplementIndex >= 0)
            value = value[..mechanicalSupplementIndex];

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
