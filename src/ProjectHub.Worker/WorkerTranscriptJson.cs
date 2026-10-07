namespace ProjectHub.Worker;

public static class WorkerTranscriptJson
{
    public static string Serialize<T>(T value) =>
        ProjectHubJson.SerializeIndented(value);
}
