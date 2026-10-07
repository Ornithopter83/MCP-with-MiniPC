using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace ProjectHub.Worker;

internal static class ProjectHubJson
{
    private static readonly JavaScriptEncoder UnicodeEncoder =
        JavaScriptEncoder.Create(UnicodeRanges.All);

    internal static UTF8Encoding Utf8NoBom { get; } =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static JsonSerializerOptions CompactOptions { get; } =
        CreateOptions(JsonSerializerDefaults.General, writeIndented: false);

    internal static JsonSerializerOptions IndentedOptions { get; } =
        CreateOptions(JsonSerializerDefaults.General, writeIndented: true);

    internal static JsonSerializerOptions WebCompactOptions { get; } =
        CreateOptions(JsonSerializerDefaults.Web, writeIndented: false);

    internal static JsonSerializerOptions WebIndentedOptions { get; } =
        CreateOptions(JsonSerializerDefaults.Web, writeIndented: true);

    internal static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, CompactOptions);

    internal static string SerializeIndented<T>(T value) =>
        JsonSerializer.Serialize(value, IndentedOptions);

    private static JsonSerializerOptions CreateOptions(
        JsonSerializerDefaults defaults,
        bool writeIndented) =>
        new(defaults)
        {
            WriteIndented = writeIndented,
            Encoder = UnicodeEncoder
        };
}
