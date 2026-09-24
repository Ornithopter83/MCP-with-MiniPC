using System.Text.Encodings.Web;
using System.Text.Json;

namespace ProjectHub.Worker;

public static class WorkerTranscriptJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
