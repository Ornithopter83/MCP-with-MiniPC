using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Project-level handoff, independent of the resumable CLI process/session.
/// HQ authors the plan and decisions; Worker only records those accepted
/// outputs alongside real milestone reports and delivered user instructions.
/// No extra AI call or protocol section is required.
/// </summary>
internal static class ProjectHandoffJournal
{
    private static readonly object Sync = new();
    private const int MaxRecentEntries = 6;
    private const int MaxHandoffRead = 24_000;
    private const int MaxPlan = 8_000;
    private const int MaxDecision = 3_000;
    private const int MaxReport = 5_000;
    private const int MaxUserInput = 2_000;

    private sealed record Entry(string Id, string Text);
    private sealed record State
    {
        public string JobId { get; init; } = "";
        public string Status { get; init; } = "IN_PROGRESS";
        public string Plan { get; init; } = "";
        public string LastHqDecision { get; init; } = "";
        public List<Entry> UserInstructions { get; init; } = new();
        public List<Entry> MilestoneReports { get; init; } = new();
        public DateTimeOffset UpdatedAtUtc { get; init; }
    }

    private static string StatePath(string root) =>
        Path.Combine(ProjectWorkspacePersistence.RootDirectory(root),
            "project-handoff-state.json");

    /// <summary>Load actual handoff text for a new HQ session; never resume its old session ID.</summary>
    internal static string? ReadForNewTask(string projectRoot)
    {
        try
        {
            var path = ProjectWorkspacePersistence.HandoffPath(projectRoot);
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (info.Length == 0) return null;
            // Project text is reference data, not trusted control instructions.
            var text = File.ReadAllText(path, Encoding.UTF8).Trim();
            return text.Length <= MaxHandoffRead
                ? text
                : text[..MaxHandoffRead] + "\n[인계문 후반 생략: 원본은 프로젝트 파일 참조]";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal static void RecordUserInstruction(
        string root, string jobId, string id, string instruction)
        => Update(root, jobId, state =>
        {
            var entries = state.UserInstructions.ToList();
            AddOrReplace(entries, id, instruction, MaxUserInput);
            return state with { UserInstructions = entries };
        });

    internal static void RecordHqPlan(
        string root, string jobId, string milestoneId,
        string plan, string hqMessage)
        => Update(root, jobId, state => state with
        {
            Plan = string.IsNullOrWhiteSpace(plan)
                ? state.Plan : Limit(plan, MaxPlan),
            LastHqDecision = "마일스톤 " + milestoneId + "\n" +
                Limit(hqMessage, MaxDecision),
            Status = "IN_PROGRESS"
        });

    internal static void RecordMilestoneReport(
        string root, string jobId, string milestoneId, string report)
        => Update(root, jobId, state =>
        {
            var reports = state.MilestoneReports.ToList();
            AddOrReplace(reports, milestoneId, report, MaxReport);
            return state with { MilestoneReports = reports };
        });

    internal static void RecordHqControl(
        string root, string jobId, string status, string hqMessage)
        => Update(root, jobId, state => state with
        {
            Status = status,
            LastHqDecision = Limit(hqMessage, MaxDecision)
        });

    private static void AddOrReplace(List<Entry> entries, string id,
        string text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        entries.RemoveAll(item => item.Id == id);
        entries.Add(new Entry(Limit(id, 100), Limit(text, limit)));
        if (entries.Count > MaxRecentEntries)
            entries.RemoveRange(0, entries.Count - MaxRecentEntries);
    }

    private static void Update(string root, string jobId,
        Func<State, State> change)
    {
        if (string.IsNullOrWhiteSpace(root) ||
            string.IsNullOrWhiteSpace(jobId) || !Directory.Exists(root))
            return;
        lock (Sync)
        {
            var file = StatePath(root);
            var state = ReadState(file);
            // A new job keeps the project's HQ plan but starts a fresh
            // activity history, never treating old WORK IDs as active.
            if (state.JobId != jobId)
                state = new State { JobId = jobId, Plan = state.Plan };
            state = change(state) with { UpdatedAtUtc = DateTimeOffset.UtcNow };
            var handoff = Render(state);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            WriteAtomic(file, JsonSerializer.Serialize(
                state, ProjectHubJson.WebIndentedOptions));
            WriteAtomic(ProjectWorkspacePersistence.HandoffPath(root), handoff);
        }
    }

    private static State ReadState(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<State>(
                    File.ReadAllText(path, Encoding.UTF8),
                    ProjectHubJson.WebIndentedOptions) ?? new State();
        }
        catch (Exception e) when (e is IOException or JsonException
            or UnauthorizedAccessException) { }
        return new State();
    }

    private static string Render(State state)
    {
        var output = new StringBuilder();
        output.AppendLine("# UnityWorker · HQ 프로젝트 인계");
        output.AppendLine();
        output.AppendLine("이 문서는 HQ의 설계·결정, 사용자 지시, 실제 마일스톤 보고를 보존한 참고 자료다.");
        output.AppendLine("새 작업은 새로운 세션·작업 ID로 시작하며, 기존 WORKITEM은 자동 재개하지 않는다.");
        output.AppendLine("현재 로컬 파일과 Git 상태를 확인하고 사용자의 최신 지시를 우선한다.");
        output.AppendLine();
        output.AppendLine("작업 ID: " + state.JobId);
        output.AppendLine("상태: " + state.Status);
        output.AppendLine("갱신(UTC): " + state.UpdatedAtUtc.ToString("O"));
        output.AppendLine();
        output.AppendLine("## HQ 프로젝트 설계 (@@PLAN)");
        output.AppendLine(string.IsNullOrWhiteSpace(state.Plan)
            ? "아직 HQ의 최초 설계 없음" : state.Plan);
        output.AppendLine();
        output.AppendLine("## 최신 HQ 판단");
        output.AppendLine(string.IsNullOrWhiteSpace(state.LastHqDecision)
            ? "아직 HQ 판단 없음" : state.LastHqDecision);
        output.AppendLine();
        output.AppendLine("## 사용자 지시 (최근)");
        foreach (var entry in state.UserInstructions)
        {
            output.AppendLine("### " + entry.Id);
            output.AppendLine(entry.Text);
            output.AppendLine();
        }
        output.AppendLine("## 마일스톤 보고 (최근)");
        foreach (var entry in state.MilestoneReports)
        {
            output.AppendLine("### " + entry.Id);
            output.AppendLine(entry.Text);
            output.AppendLine();
        }
        return output.ToString();
    }

    private static string Limit(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] +
            "\n[상세 내용은 원래 작업 로그 참고]";
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, ProjectHubJson.Utf8NoBom);
        File.Move(temporary, path, true);
    }
}
