using System.IO;

namespace ProjectHub.Worker;

/// <summary>DESIGN produces actual Unity visual changes and evidence.</summary>
internal static class UnityDesignPromptPolicy
{
    internal static string Build(MilestoneDesignDefinition design, string projectRoot)
    {
        return "[ROLE=DESIGN]\nDESIGN_ID: 0\nPROJECT_ROOT: " +
            Path.GetFullPath(projectRoot) +
            "\nDESIGN_DOCUMENT: " + design.Path +
            "\nDESIGN_WRITE_PATHS: " +
            (design.WritePaths.Count == 0 ? "프로젝트 시각 제작 범위(기존 변경 존중)" :
                string.Join(", ", design.WritePaths)) +
            "\n@@DESIGN_GOAL\n" + design.Instructions +
            "\n\n@@DESIGN_METHOD\n" +
            "1. 계획·문서 작성에 머무르지 말고 기존 UI/Scene/Canvas/Prefab 등을 분석하여 " +
            "실제 Sprite, Material, Shader, Animator, Particle 및 시각 전용 C#을 제작·수정·저장하라. " +
            "게임 규칙·점수·기존 체크포인트와 다른 작업의 변경을 훼손하지 마라.\n" +
            "2. 확인된 Unity CLI/MCP/Editor API 명령과 실제 파라미터만 사용하라. " +
            "연결되지 않은 명령이나 캡처 성공을 추측하지 마라. " +
            "GUI 전면화와 사용자 입력·포커스 탈취는 금지한다. " +
            "Application.runInBackground, batchmode, -nographics는 " +
            "게임 뷰 렌더 캡처와 포커스 보장의 증거가 아니다.\n" +
            "3. 기존 캡처·연속 프레임이 있다면 실제 내용과 변경 전후를 비교하고, " +
            "없다면 해당 검증을 미검증으로 표시하라. " +
            "capture_game_view 등 카탈로그에 없는 명령은 호출하지 마라. " +
            "데스크톱 폴백, 동일 프레임 반복, 파일 존재만으로 시각 검증을 통과 처리하지 마라.\n" +
            "4. HIGH에서 인계된 목표·증거·수정 경로가 있으면 그 범위에서 수정하고 " +
            "재검증 증거를 남겨라. 실제 시각 파일 변경 없이 완료라고 보고하지 마라.\n" +
            "5. 변경 파일, 실제 실행한 명령과 관찰 결과, 실패·미검증 사항을 명시하라.\n\n" +
            "@@RESULT_CONTRACT\n" +
            "[ACTION=RESULT] @@REPORT <STATUS>completed|in_progress|blocked</> " +
            "<SUMMARY>실제 변경과 검증 근거</> <CHANGED_PATH>실제 수정 파일(반복)</> " +
            "<ISSUES>실패·미검증</> [RESPONSE=OK].";
    }

    internal static bool IsVisualArtifact(string path) =>
        new[] { ".unity", ".prefab", ".mat", ".shader", ".shadergraph",
            ".png", ".jpg", ".jpeg", ".svg", ".spriteatlas",
            ".anim", ".controller", ".uxml", ".uss", ".vfx" }
            .Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ||
        (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
         new[] { "/UI/", "/Visual", "/Editor/", "/Presentation/",
                 "Sprite", "Effect", "Theme", "Vfx", "Procedural" }
            .Any(fragment => ("/" + path.Replace('\\', '/')).Contains(fragment,
                StringComparison.OrdinalIgnoreCase)));

    internal static bool IsVerifiedDocument(string root, MilestoneDesignDefinition design) =>
        MilestoneDefinitionContract.IsSafeRelativePath(design.Path) &&
        File.Exists(Path.Combine(Path.GetFullPath(root),
            design.Path.Replace('/', Path.DirectorySeparatorChar)));
}
