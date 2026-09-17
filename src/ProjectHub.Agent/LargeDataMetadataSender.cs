using System.Net.Http.Json;
using ProjectHub.Core;

namespace ProjectHub.Agent;

public sealed class LargeDataMetadataSender(HttpClient client, string workstationId)
{
    public async Task SendInventoryAsync(ProjectRegistration project, IReadOnlyList<ProjectLargeFile> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0) return;
        await client.PostAsJsonAsync($"api/large-data/reconciliation/{Uri.EscapeDataString(project.ProjectId)}", new { workstationId, files }, cancellationToken);
    }
}
