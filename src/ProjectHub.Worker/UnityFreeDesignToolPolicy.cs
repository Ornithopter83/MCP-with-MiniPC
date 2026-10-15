using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Optional, no-cost graphics tools for a single Windows Unity workstation.
/// Discovery never installs software, starts a renderer, or contacts a server.
/// </summary>
internal static class UnityFreeDesignToolPolicy
{
    internal static string BuildGuidance()
    {
        var inkscape = FindExecutable("inkscape.exe", InkscapeCandidates());
        var blender = FindExecutable("blender.exe", BlenderCandidates());

        return "\n\n노트북 로컬 무료 그래픽 도구 (자동 탐지, 설치 및 실행은 별개):\n" +
            "Inkscape: " + (inkscape ?? "NOT_INSTALLED") + "\n" +
            "Blender (선택): " + (blender ?? "NOT_INSTALLED") + "\n" +
            "Inkscape가 확인되면 SVG 아이콘·패널·9-slice 소스를 제작하고 " +
            "명령행 --export-filename으로 PNG를 만들 수 있다. " +
            "Blender가 확인되고 3D 제작이 꼭 필요하면 --background --python 스크립트로 제작하라. " +
            "각 명령은 설치된 경로와 --help/--version을 확인한 뒤 실행하고, " +
            "명령 실패 시 같은 방식을 반복하지 말고 SVG·Unity 기본 도구로 대체하라.\n" +
            "AI가 임의 설치를 실행하지 마라. 외부 생성 API나 유료 크레딧을 사용하지 마라. " +
            "이번 노트북 설정에서는 ComfyUI, 로컬 생성 모델, GPU 서버를 호출하거나 설치하지 마라. " +
            "무료 CC0 에셋은 ASSET MANAGER에게 맡기고 실제 개별 파일의 라이선스를 확인하라. " +
            "Editor 조작 중에는 다른 AI 작업과 병렬 실행하지 마라. " +
            "외부 도구 결과물은 Unity에 실제 적용한 변경과 구별해 보고하라. " +
            "추가 시각 검증은 새로운 변경·결함에 대한 증거가 있을 때만 수행하라.";
    }

    internal static string? FindExecutable(
        string name, IEnumerable<string>? candidates = null)
    {
        if (candidates is not null)
            foreach (var candidate in candidates)
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return Path.GetFullPath(candidate);

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            try
            {
                var found = Path.Combine(directory.Trim().Trim('"'), name);
                if (File.Exists(found))
                    return Path.GetFullPath(found);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // Skip invalid PATH entries. Design tool absence is not a stop.
            }
        }
        return null;
    }

    private static IEnumerable<string> InkscapeCandidates()
    {
        foreach (var root in ProgramFilesRoots())
        {
            yield return Path.Combine(root, "Inkscape", "bin", "inkscape.exe");
            yield return Path.Combine(root, "Inkscape", "inkscape.exe");
        }
    }

    private static IEnumerable<string> BlenderCandidates()
    {
        foreach (var root in ProgramFilesRoots())
        {
            var vendor = Path.Combine(root, "Blender Foundation");
            if (!Directory.Exists(vendor))
                continue;
            string[] folders;
            try { folders = Directory.GetDirectories(vendor, "Blender*"); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var folder in folders.OrderByDescending(path => path,
                         StringComparer.OrdinalIgnoreCase))
                yield return Path.Combine(folder, "blender.exe");
        }
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        foreach (var kind in new[]
                 { Environment.SpecialFolder.ProgramFiles,
                   Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Environment.GetFolderPath(kind);
            if (!string.IsNullOrWhiteSpace(path))
                yield return path;
        }
    }
}
