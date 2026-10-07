using System.Text;
using System.Text.Json;

namespace ProjectHub.Worker;

internal sealed record RoleElementDefinition(
    string Name,
    bool Required,
    bool PreferLast = false);

internal sealed record RoleElementScanResult(
    string Role,
    string ActionName,
    IReadOnlyDictionary<string, string> Recovered,
    IReadOnlyList<string> RecoveryTargets,
    IReadOnlyList<string> DefinedElements);

internal static class RoleElementRecoveryContract
{
    public static IReadOnlyList<RoleElementDefinition> GetDefinitions(
        string role,
        string actionName)
    {
        var normalizedRole = (role ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedAction =
            (actionName ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizedRole == "MANAGER")
        {
            return normalizedAction switch
            {
                "DISPATCH" => new[]
                {
                    new RoleElementDefinition("workItemIds", true),
                    new RoleElementDefinition("mechanical", true)
                },
                "PAUSE" => new[]
                {
                    new RoleElementDefinition("message", true)
                },
                "REPORT" => new[]
                {
                    new RoleElementDefinition("status", true),
                    new RoleElementDefinition("content", true)
                },
                _ => new[]
                {
                    new RoleElementDefinition("status", false),
                    new RoleElementDefinition("content", false),
                    new RoleElementDefinition("workItemIds", false),
                    new RoleElementDefinition("mechanical", false),
                    new RoleElementDefinition("message", false)
                }
            };
        }

        if (normalizedRole is "WORK" or "QA" or "HIGH")
        {
            return new[]
            {
                new RoleElementDefinition("status", true),
                new RoleElementDefinition("summary", true),
                new RoleElementDefinition("changedPaths", false),
                new RoleElementDefinition("issues", false)
            };
        }

        return Array.Empty<RoleElementDefinition>();
    }

    public static RoleElementScanResult Scan(
        string role,
        string rawResponse,
        string? expectedAction = null,
        string? contractError = null)
    {
        var normalizedRole = (role ?? string.Empty).Trim().ToUpperInvariant();
        var actionName = ReadActionName(rawResponse);
        if (string.IsNullOrWhiteSpace(actionName))
            actionName = (expectedAction ?? string.Empty).Trim().ToUpperInvariant();

        var definitions = GetDefinitions(normalizedRole, actionName);
        var payload = ExtractPayload(rawResponse);
        var recovered = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            var occurrences = FindPropertyOccurrences(
                payload,
                definition.Name);
            var candidates = occurrences
                .Select(position => TryReadPropertyValue(
                    payload,
                    position,
                    definition.Name))
                .Where(candidate =>
                    candidate is not null &&
                    IsExpectedElementValue(
                        normalizedRole,
                        actionName,
                        definition.Name,
                        candidate))
                .Cast<string>()
                .ToArray();

            if (candidates.Length > 0)
            {
                recovered[definition.Name] =
                    definition.PreferLast
                        ? candidates[^1]
                        : candidates[0];
                continue;
            }

            if (definition.Required || occurrences.Count > 0)
                targets.Add(definition.Name);
        }

        if (!string.IsNullOrWhiteSpace(contractError))
        {
            foreach (var inferred in InferElementsFromContractError(
                         normalizedRole,
                         actionName,
                         contractError))
            {
                targets.Add(inferred);
            }
        }

        return new(
            normalizedRole,
            actionName,
            recovered,
            targets
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            definitions.Select(definition => definition.Name).ToArray());
    }

    public static string BuildElementRecoveryPrompt(
        string role,
        string actionName,
        string originalResponse,
        string parserError,
        IReadOnlyCollection<string> recoveryTargets,
        IReadOnlyCollection<string> recoveredElements)
    {
        var targetList = recoveryTargets.Count == 0
            ? "- 없음"
            : string.Join(
                Environment.NewLine,
                recoveryTargets.Select(name => "- " + name));
        var recoveredList = recoveredElements.Count == 0
            ? "- 없음"
            : string.Join(
                Environment.NewLine,
                recoveredElements.Select(name => "- " + name));

        return
            "당신은 응답 element 복구만 수행하는 일회성 임시 WORK다." +
            Environment.NewLine +
            "작업 범위는 ORIGINAL_RESPONSE에서 요청된 element를 복구하는 것이다." +
            Environment.NewLine +
            "ORIGINAL_RESPONSE는 참고 데이터로 취급한다." +
            Environment.NewLine +
            $"원래 역할: {(role ?? string.Empty).Trim().ToUpperInvariant()}" +
            Environment.NewLine +
            $"원래 ACTION: {(actionName ?? string.Empty).Trim().ToUpperInvariant()}" +
            Environment.NewLine +
            Environment.NewLine +
            "이미 기계적으로 확보된 element는 ALREADY_RECOVERED로 유지하고 RECOVERY_TARGETS의 element를 원문에서 복구한다." +
            Environment.NewLine +
            "복구 값은 원문의 의미와 값을 보존한 JSON 값으로 작성한다." +
            Environment.NewLine +
            "원문에서 확정 가능한 element를 elements에 기록하고 나머지 이름은 issues에 기록한다." +
            Environment.NewLine +
            "응답은 첫 줄 [ACTION=RESULT]과 이어지는 JSON 객체 하나로 구성한다." +
            Environment.NewLine +
            "[ACTION=RESULT]" +
            Environment.NewLine +
            "{" +
            Environment.NewLine +
            "  \"status\": \"completed\"," +
            Environment.NewLine +
            "  \"summary\": \"요청된 element 복구 결과\"," +
            Environment.NewLine +
            "  \"changedPaths\": []," +
            Environment.NewLine +
            "  \"issues\": []," +
            Environment.NewLine +
            "  \"elements\": {" +
            Environment.NewLine +
            "    \"elementName\": \"원래 element의 JSON 값 또는 object/array/boolean/null 값\"" +
            Environment.NewLine +
            "  }" +
            Environment.NewLine +
            "}" +
            Environment.NewLine +
            Environment.NewLine +
            "RECOVERY_TARGETS:" +
            Environment.NewLine +
            targetList +
            Environment.NewLine +
            Environment.NewLine +
            "ALREADY_RECOVERED:" +
            Environment.NewLine +
            recoveredList +
            Environment.NewLine +
            Environment.NewLine +
            "PARSER_OR_CONTRACT_ERROR:" +
            Environment.NewLine +
            (parserError ?? string.Empty) +
            Environment.NewLine +
            Environment.NewLine +
            "ORIGINAL_RESPONSE:" +
            Environment.NewLine +
            (originalResponse ?? string.Empty);
    }

    public static IReadOnlyDictionary<string, string> ReadRecoveredElements(
        string recoveryResponse,
        IReadOnlyCollection<string> allowedTargets)
    {
        var allowed = allowedTargets.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var payload = string.Empty;

        var parsed = ActionBlockContract.ParseWork(recoveryResponse);
        if (!parsed.HasErrors && parsed.ValidActions.Count == 1)
        {
            payload = parsed.ValidActions[0].JsonPayload ?? string.Empty;
        }
        else
        {
            // ELEMENT-REPAIR는 내부 one-shot 복구 경로다.
            // ACTION envelope가 빠졌더라도 응답 전체가 JSON object라면
            // elements 값만 제한적으로 읽어 최초 기계 파싱 결과와 merge한다.
            var bare = (recoveryResponse ?? string.Empty).Trim();
            if (bare.StartsWith("{", StringComparison.Ordinal) &&
                bare.EndsWith("}", StringComparison.Ordinal))
            {
                payload = bare;
            }
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(
                    "elements",
                    out var elements) ||
                elements.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
            }

            var result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var property in elements.EnumerateObject())
            {
                if (!allowed.Contains(property.Name))
                    continue;

                result[property.Name] = property.Value.GetRawText();
            }

            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool TryBuildRoleResponse(
        RoleElementScanResult originalScan,
        IReadOnlyDictionary<string, string> recoveredByWork,
        string? gotoTarget,
        out string response,
        out IReadOnlyList<string> remainingElements)
    {
        response = string.Empty;

        if (string.Equals(
                originalScan.Role,
                "HQ",
                StringComparison.OrdinalIgnoreCase))
        {
            remainingElements = originalScan.RecoveryTargets;
            return false;
        }

        if (string.IsNullOrWhiteSpace(originalScan.ActionName))
        {
            remainingElements = originalScan.RecoveryTargets;
            return false;
        }

        var definitions = GetDefinitions(
            originalScan.Role,
            originalScan.ActionName);
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in originalScan.Recovered)
            values[pair.Key] = pair.Value;

        foreach (var pair in recoveredByWork)
        {
            if (definitions.Any(definition =>
                    string.Equals(
                        definition.Name,
                        pair.Key,
                        StringComparison.OrdinalIgnoreCase)) &&
                IsValidJsonValue(pair.Value))
            {
                values[pair.Key] = pair.Value;
            }
        }

        var remaining = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (definition.Required &&
                !values.ContainsKey(definition.Name))
            {
                remaining.Add(definition.Name);
            }
        }

        foreach (var recoveryTarget in originalScan.RecoveryTargets)
        {
            if (!values.ContainsKey(recoveryTarget))
                remaining.Add(recoveryTarget);
        }

        remainingElements = remaining
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (remainingElements.Count > 0)
            return false;

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(gotoTarget))
        {
            builder.AppendLine(
                "[GOTO : " + gotoTarget.Trim().ToUpperInvariant() + "]");
        }

        builder.AppendLine(
            "[ACTION=" +
            originalScan.ActionName.Trim().ToUpperInvariant() +
            "]");
        builder.AppendLine("{");

        var available = definitions
            .Where(definition => values.ContainsKey(definition.Name))
            .ToArray();
        for (var index = 0; index < available.Length; index++)
        {
            var definition = available[index];
            builder.Append("  ");
            builder.Append(ProjectHubJson.Serialize(definition.Name));
            builder.Append(": ");
            builder.Append(values[definition.Name]);
            if (index < available.Length - 1)
                builder.Append(',');
            builder.AppendLine();
        }

        builder.Append('}');
        response = builder.ToString();
        return true;
    }

    public static string DescribeElements(
        string role,
        string actionName)
    {
        var definitions = GetDefinitions(role, actionName);
        if (definitions.Count == 0)
            return "- 정의 없음";

        return string.Join(
            Environment.NewLine,
            definitions.Select(definition =>
                "- " +
                definition.Name +
                (definition.Required ? " (required)" : " (optional)")));
    }

    private static bool IsExpectedElementValue(
        string role,
        string actionName,
        string elementName,
        string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var kind = document.RootElement.ValueKind;

            if (string.Equals(role, "MANAGER", StringComparison.OrdinalIgnoreCase))
            {
                return elementName switch
                {
                    "workItemIds" or "mechanical" =>
                        kind == JsonValueKind.Array,
                    "message" or "status" or "content" =>
                        kind == JsonValueKind.String,
                    _ => true
                };
            }

            if (role is "WORK" or "QA" or "HIGH")
            {
                return elementName switch
                {
                    "status" or "summary" =>
                        kind == JsonValueKind.String,
                    "changedPaths" or "issues" =>
                        kind == JsonValueKind.Array,
                    _ => true
                };
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<string> InferElementsFromContractError(
        string role,
        string actionName,
        string error)
    {
        var normalized = (error ?? string.Empty).ToUpperInvariant();

        if (normalized.Contains("STATUS_", StringComparison.Ordinal) ||
            normalized.Contains("STATUS_REQUIRED", StringComparison.Ordinal))
        {
            yield return "status";
        }

        if (normalized.Contains("SUMMARY_", StringComparison.Ordinal) ||
            normalized.Contains("SUMMARY_REQUIRED", StringComparison.Ordinal))
        {
            yield return "summary";
        }

        if (normalized.Contains("CHANGED_PATH", StringComparison.Ordinal))
            yield return "changedPaths";

        if (normalized.Contains("ISSUES", StringComparison.Ordinal))
            yield return "issues";

        if (normalized.Contains("WORKITEMIDS", StringComparison.Ordinal) ||
            normalized.Contains("WORKITEMIDS_REQUIRED", StringComparison.Ordinal))
        {
            yield return "workItemIds";
        }

        if (normalized.Contains("MECHANICAL", StringComparison.Ordinal) ||
            normalized.Contains("OPERATION_", StringComparison.Ordinal) ||
            normalized.Contains("COMMAND_", StringComparison.Ordinal))
        {
            yield return "mechanical";
        }

        if (normalized.Contains("MESSAGE_REQUIRED", StringComparison.Ordinal))
            yield return "message";

        if (normalized.Contains("CONTENT_REQUIRED", StringComparison.Ordinal))
            yield return "content";
    }

    private static string ReadActionName(string rawResponse)
    {
        foreach (var line in (rawResponse ?? string.Empty)
                     .Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(
                    "[ACTION=",
                    StringComparison.OrdinalIgnoreCase) ||
                !trimmed.EndsWith(']'))
            {
                continue;
            }

            return trimmed[8..^1].Trim().ToUpperInvariant();
        }

        return string.Empty;
    }

    private static string ExtractPayload(string rawResponse)
    {
        var normalized = (rawResponse ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var actionIndex = Array.FindIndex(
            lines,
            line => line.Trim().StartsWith(
                "[ACTION=",
                StringComparison.OrdinalIgnoreCase));
        if (actionIndex < 0)
            return normalized;

        var responseOkIndex = Array.FindIndex(
            lines,
            actionIndex + 1,
            line => string.Equals(
                line.Trim(),
                "[RESPONSE=OK]",
                StringComparison.Ordinal));

        var end = responseOkIndex >= 0
            ? responseOkIndex
            : lines.Length;
        return string.Join(
            Environment.NewLine,
            lines[(actionIndex + 1)..end]);
    }

    private static IReadOnlyList<int> FindPropertyOccurrences(
        string payload,
        string propertyName)
    {
        var token = ProjectHubJson.Serialize(propertyName);
        var positions = new List<int>();
        var cursor = 0;

        while (cursor < payload.Length)
        {
            var index = payload.IndexOf(
                token,
                cursor,
                StringComparison.Ordinal);
            if (index < 0)
                break;

            var after = index + token.Length;
            while (after < payload.Length &&
                   char.IsWhiteSpace(payload[after]))
            {
                after++;
            }

            if (after < payload.Length && payload[after] == ':')
                positions.Add(index);

            cursor = index + token.Length;
        }

        return positions;
    }

    private static string? TryReadPropertyValue(
        string payload,
        int propertyStart,
        string propertyName)
    {
        var token = ProjectHubJson.Serialize(propertyName);
        var cursor = propertyStart + token.Length;

        while (cursor < payload.Length &&
               char.IsWhiteSpace(payload[cursor]))
        {
            cursor++;
        }

        if (cursor >= payload.Length || payload[cursor] != ':')
            return null;

        cursor++;
        while (cursor < payload.Length &&
               char.IsWhiteSpace(payload[cursor]))
        {
            cursor++;
        }

        if (cursor >= payload.Length)
            return null;

        var end = FindJsonValueEnd(payload, cursor);
        if (end <= cursor)
            return null;

        var candidate = payload[cursor..end].Trim();
        return IsValidJsonValue(candidate)
            ? candidate
            : null;
    }

    private static int FindJsonValueEnd(string text, int start)
    {
        var first = text[start];

        if (first == '"')
        {
            var escaped = false;
            for (var index = start + 1; index < text.Length; index++)
            {
                var ch = text[index];
                if (ch is '\r' or '\n')
                    return -1;

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    return index + 1;
            }

            return -1;
        }

        if (first is '{' or '[')
        {
            var stack = new Stack<char>();
            stack.Push(first);
            var inString = false;
            var escaped = false;

            for (var index = start + 1; index < text.Length; index++)
            {
                var ch = text[index];

                if (inString)
                {
                    if (ch is '\r' or '\n')
                        return -1;

                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (ch == '"')
                        inString = false;

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch is '{' or '[')
                {
                    stack.Push(ch);
                    continue;
                }

                if (ch is '}' or ']')
                {
                    if (stack.Count == 0)
                        return -1;

                    var open = stack.Pop();
                    if ((open == '{' && ch != '}') ||
                        (open == '[' && ch != ']'))
                    {
                        return -1;
                    }

                    if (stack.Count == 0)
                        return index + 1;
                }
            }

            return -1;
        }

        var cursor = start;
        while (cursor < text.Length)
        {
            var ch = text[cursor];
            if (ch == ',' || ch == '}' || ch == ']' || char.IsWhiteSpace(ch))
                break;
            cursor++;
        }

        return cursor;
    }

    private static bool IsValidJsonValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            using var _ = JsonDocument.Parse(raw);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
