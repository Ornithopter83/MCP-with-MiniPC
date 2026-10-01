using System.IO;

namespace ProjectHub.Worker;

public static class FinalResultPathNormalizer
{
    public static string NormalizeLandedPaths(
        string? message,
        string targetWorkspace,
        WorkGraphSnapshot graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return NormalizeLandedPaths(
            message,
            targetWorkspace,
            graph.Items.Select(item => item.WorktreePath));
    }

    public static string NormalizeLandedPaths(
        string? message,
        string targetWorkspace,
        IEnumerable<string?> worktreeRoots)
    {
        if (string.IsNullOrEmpty(message) ||
            string.IsNullOrWhiteSpace(targetWorkspace))
            return message ?? string.Empty;

        var workspace = Path.GetFullPath(targetWorkspace)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var result = message;

        foreach (var rootValue in worktreeRoots
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string root;
            try
            {
                root = Path.GetFullPath(rootValue!)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                continue;
            }

            result = RewriteRoot(result, root, workspace);
            result = RewriteRoot(
                result,
                root.Replace('\\', '/'),
                workspace);
        }

        return result;
    }

    private static string RewriteRoot(
        string text,
        string sourceRoot,
        string targetWorkspace)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            return text;

        var searchStart = 0;
        while (searchStart < text.Length)
        {
            var index = text.IndexOf(
                sourceRoot,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            var suffixStart = index + sourceRoot.Length;
            if (suffixStart >= text.Length ||
                (text[suffixStart] != '\\' && text[suffixStart] != '/'))
            {
                searchStart = suffixStart;
                continue;
            }

            var hardEnd = FindHardPathEnd(text, suffixStart);
            var rawPath = text[index..hardEnd];
            if (!TryMapExistingTarget(
                    rawPath,
                    sourceRoot,
                    targetWorkspace,
                    out var mappedPath,
                    out var matchedLength))
            {
                searchStart = suffixStart;
                continue;
            }

            text =
                text[..index] +
                mappedPath +
                text[(index + matchedLength)..];
            searchStart = index + mappedPath.Length;
        }

        return text;
    }

    private static int FindHardPathEnd(string text, int start)
    {
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n' or ')' or ']' or '>' or '"' or '\'')
                return index;
        }

        return text.Length;
    }

    private static bool TryMapExistingTarget(
        string rawPath,
        string sourceRoot,
        string targetWorkspace,
        out string mappedPath,
        out int matchedLength)
    {
        mappedPath = string.Empty;
        matchedLength = 0;

        var candidate = rawPath.TrimEnd();
        while (candidate.Length > sourceRoot.Length)
        {
            var trimmedCandidate = candidate.TrimEnd('.', ',', ';', ':');
            if (TryMapCandidate(
                    trimmedCandidate,
                    sourceRoot,
                    targetWorkspace,
                    out mappedPath))
            {
                matchedLength = trimmedCandidate.Length;
                return true;
            }

            var split = candidate.LastIndexOfAny(new[] { ' ', '\t' });
            if (split <= sourceRoot.Length)
                break;

            candidate = candidate[..split].TrimEnd();
        }

        return false;
    }

    private static bool TryMapCandidate(
        string sourcePath,
        string sourceRoot,
        string targetWorkspace,
        out string mappedPath)
    {
        mappedPath = string.Empty;
        if (!sourcePath.StartsWith(
                sourceRoot,
                StringComparison.OrdinalIgnoreCase))
            return false;

        var relative = sourcePath[sourceRoot.Length..]
            .TrimStart('\\', '/');
        if (relative.Length == 0)
            return false;

        try
        {
            var normalizedRelative = relative
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(
                Path.Combine(targetWorkspace, normalizedRelative));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var workspacePrefix =
                targetWorkspace +
                Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(workspacePrefix, comparison) ||
                (!File.Exists(candidate) && !Directory.Exists(candidate)))
                return false;

            mappedPath = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
