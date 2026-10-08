using System.Buffers.Binary;
using System.IO;

namespace ProjectHub.Worker;

internal static class ResourceArtifactValidator
{
    private static readonly byte[] PngSignature = {137,80,78,71,13,10,26,10};

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
