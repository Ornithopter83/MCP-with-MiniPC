using System.IO;

namespace ProjectHub.Worker;

public static class GitRemoteAddressPolicy
{
    public static string? SanitizeForDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var remote = value.Trim();
        if (Uri.TryCreate(remote, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        var at = remote.IndexOf('@');
        return at >= 0 && at < remote.Length - 1
            ? remote[(at + 1)..]
            : remote;
    }

    public static bool IsNetworkRemote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var remote = value.Trim();

        if (Uri.TryCreate(remote, UriKind.Absolute, out var uri))
        {
            if (string.IsNullOrWhiteSpace(uri.Host))
                return false;

            return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals("git", StringComparison.OrdinalIgnoreCase);
        }

        // Git's SCP-like SSH syntax: [user@]host:path
        var colon = remote.IndexOf(':');
        if (colon <= 0 || colon == remote.Length - 1)
            return false;

        var hostPart = remote[..colon];
        var pathPart = remote[(colon + 1)..];
        if (pathPart.StartsWith("/", StringComparison.Ordinal) &&
            OperatingSystem.IsWindows() &&
            hostPart.Length == 1 &&
            char.IsAsciiLetter(hostPart[0]))
        {
            return false;
        }

        var at = hostPart.LastIndexOf('@');
        var host = at >= 0 ? hostPart[(at + 1)..] : hostPart;
        if (string.IsNullOrWhiteSpace(host) ||
            host is "." or "..")
        {
            return false;
        }

        return !host.Contains(Path.DirectorySeparatorChar) &&
               !host.Contains(Path.AltDirectorySeparatorChar);
    }
}
