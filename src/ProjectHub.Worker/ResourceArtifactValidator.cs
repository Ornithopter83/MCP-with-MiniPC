using System.Buffers.Binary;
using System.IO;

namespace ProjectHub.Worker;

internal static class ResourceArtifactValidator
{
    private static readonly byte[] PngSignature = {137,80,78,71,13,10,26,10};

    /// <summary>
    /// ChatGPT Web may return multiple image variants and duplicate capture
    /// candidates. Try each file against the HQ specification, deterministically
    /// keep one matching artifact and report why every candidate was rejected
    /// when none matches. Never reject solely because multiple files exist.
    /// </summary>
    public static bool TrySelect(
        MilestoneResourceDefinition resource,
        IReadOnlyList<string> candidates,
        out string selectedPath,
        out string detail)
    {
        selectedPath = string.Empty;
        var failures = new List<string>();
        if (candidates is null || candidates.Count == 0)
        {
            detail = "RESOURCE_FILE_MISSING";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var considered = 0;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !seen.Add(candidate))
                continue;
            considered++;
            if (TryValidate(resource, new[] { candidate }, out var failure))
            {
                selectedPath = candidate;
                detail = "RESOURCE_SELECTED: " + Path.GetFileName(candidate) +
                    Environment.NewLine +
                    $"RESOURCE_CANDIDATES: {candidates.Count} (validated={considered})";
                return true;
            }
            failures.Add(Path.GetFileName(candidate) + ": " + failure);
        }

        detail = "RESOURCE_NO_COMPLIANT_IMAGE" + Environment.NewLine +
            $"RESOURCE_CANDIDATES: {candidates.Count}" + Environment.NewLine +
            string.Join(Environment.NewLine, failures.Take(16));
        return false;
    }

    /// <summary>
    /// Verifies transport output against only the explicitly declared machine
    /// constraints. It does not infer dimensions from file names or prose, nor
    /// pretend to judge animation quality or artistic consistency.
    /// </summary>
    public static bool TryValidate(
        MilestoneResourceDefinition resource,
        IReadOnlyList<string> sourcePaths,
        out string error)
    {
        error = string.Empty;
        if (sourcePaths.Count != 1)
        {
            error = "RESOURCE_FILE_COUNT_INVALID";
            return false;
        }

        var source = sourcePaths[0];
        if (!File.Exists(source) ||
            !string.Equals(Path.GetExtension(source), ".png", StringComparison.OrdinalIgnoreCase))
        {
            error = "RESOURCE_PNG_REQUIRED";
            return false;
        }

        try
        {
            Span<byte> header = stackalloc byte[26];
            using var stream = File.OpenRead(source);
            if (stream.Read(header) != header.Length ||
                !header[..8].SequenceEqual(PngSignature) ||
                !header.Slice(12, 4).SequenceEqual("IHDR"u8))
            {
                error = "RESOURCE_PNG_HEADER_INVALID";
                return false;
            }

            var width = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4));
            var colorType = header[25];
            if ((resource.Width.HasValue && width != resource.Width.Value) ||
                (resource.Height.HasValue && height != resource.Height.Value))
            {
                error = $"RESOURCE_DIMENSIONS_MISMATCH: actual={width}x{height} expected={resource.Width?.ToString() ?? "*"}x{resource.Height?.ToString() ?? "*"}";
                return false;
            }
            if ((resource.Columns.HasValue && width % resource.Columns.Value != 0) ||
                (resource.Rows.HasValue && height % resource.Rows.Value != 0))
            {
                error = "RESOURCE_GRID_SIZE_MISMATCH";
                return false;
            }
            if (resource.RequireAlpha && colorType is not (4 or 6))
            {
                error = "RESOURCE_ALPHA_REQUIRED";
                return false;
            }
            return true;
        }
        catch (IOException e)
        {
            error = "RESOURCE_FILE_READ_FAILED: " + e.Message;
            return false;
        }
    }
}
