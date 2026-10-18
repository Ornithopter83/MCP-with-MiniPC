using System.IO;

namespace ProjectHub.Worker;

/// <summary>DESIGN produces actual Unity visual changes and evidence.</summary>
internal static class UnityDesignPromptPolicy
{
    internal static string Build(MilestoneDesignDefinition design, string projectRoot)
    {
        return "@@MESSAGE\nDESIGN_ID: 0\nPROJECT_ROOT: " +
            Path.GetFullPath(projectRoot) +
            "\nDESIGN_DOCUMENT: " + design.Path +
            "\nDESIGN_WRITE_PATHS: " +
            (design.WritePaths.Count == 0
                ? "프로젝트 시각 제작 범위(기존 변경 존중)"
                : string.Join(", ", design.WritePaths)) +
            "\n\n디자인 지시:\n" + design.Instructions +
            "\n\n디자인 문서가 없으면 지정 경로에 작성하고 실제 시각 결과물을 제작·적용하라. " +
            "기존 게임 규칙과 다른 역할의 변경을 보존하라.\n" +
            BuildCapturePathGuidance(projectRoot);
    }

    internal static string BuildCapturePathGuidance(string projectRoot) =>
        "Unity 캡처의 --save_path/--output은 PROJECT_ROOT 아래 docs/...의 " +
        "절대 경로로 지정하라. 상대 docs/...는 Assets/docs에 저장될 수 있다. " +
        "실제 저장 위치와 PNG 내용을 확인하고 오출력은 증거로 사용하지 마라.";

    internal static bool IsVerifiedDocument(string root, MilestoneDesignDefinition design) =>
        MilestoneDefinitionContract.IsSafeRelativePath(design.Path) &&
        File.Exists(Path.Combine(Path.GetFullPath(root),
            design.Path.Replace('/', Path.DirectorySeparatorChar)));
}
