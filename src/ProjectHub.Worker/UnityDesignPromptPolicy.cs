using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// #0 produces a reusable design specification and grounded visual review.
/// It never dispatches image extraction or claims that an uninspected frame passed.
/// </summary>
internal static class UnityDesignPromptPolicy
{
    internal static string Build(MilestoneDesignDefinition design, string projectRoot)
    {
        return "[ROLE=DESIGN]\nDESIGN_ID: 0\nPROJECT_ROOT: " +
            Path.GetFullPath(projectRoot) +
            "\nDESIGN_DOCUMENT: " + design.Path +
            "\nDESIGN_WRITE_PATHS: " +
            (design.WritePaths.Count == 0 ? "프로젝트 그래픽 범위(기존 WORK 소유권 존중)" :
                string.Join(", ", design.WritePaths)) +
            "\n@@DESIGN_GOAL\n" + design.Instructions +
            "\n\n@@DESIGN_METHOD\n" +
            "1. 실제 Unity 프로젝트의 UI/Scene/Canvas/URP/패키지를 읽고 아트 방향, " +
            "레이아웃, 색상·타이포·스프라이트, 피드백 타임라인, 테마 단계별 차이를 " +
            "구체적인 수치·구성으로 문서화하라. 기본 uGUI 구조는 보존한다.\n" +
            "2. 기존 게임 캡처·연속 프레임 및 manifest가 있으면 직접 열어 " +
            "글자 잘림, 계층, 대비, 버튼 Hover, 정오답 연출을 시각적으로 평가하라. " +
            "이미지 내용이 확인되지 않았다면 디자인 합격을 주장하지 마라. " +
            "기존 캡처가 없으면 필요한 해상도·상태별 촬영 계획과 미검증 항목을 남겨라.\n" +
            "3. 검증 가능한 Unity CLI/MCP command 카탈로그를 먼저 확인하라. " +
            "등록되지 않은 capture_game_view, capture_scene_view, eval 또는 커스텀 " +
            "명령이 있다고 추측하지 마라. 실제로 지원되는 기능만 사용하도록 제안하라. " +
            "효과 Preview·테마 강제 전환·프레임 촬영은 게임 점수를 바꾸지 않는 " +
            "Editor 전용 도구로 설계하라.\n" +
            "4. 기존 uGUI에는 절차형 Sprite, 9-slice, Animator/Timeline, " +
            "UI용 셰이더(호환성 검증 후), 파티클 풀 등을 우선 검토한다. " +
            "AI 이미지/벡터/오디오 도구는 사용 가능성과 라이선스·실제 파일을 " +
            "확인할 때만 선택하며 RESOURCE 이미지 추출 사이드카를 이용하지 않는다. " +
            "UI Toolkit 전체 교체는 요구하지 않는다.\n" +
            "5. Windows Player는 필요하면 Application.runInBackground=true로 " +
            "포커스가 없어도 업데이트되게 할 수 있지만, 이것이 창 활성화를 막거나 " +
            "마우스 입력·스크린샷을 보장하지는 않는다. Unity CLI는 기존 " +
            "Editor의 비-UI 명령을 우선하고, 추가 GUI 실행/창 전면화/키 입력 " +
            "포커스 탈취를 피하라. batchmode는 별도 프로젝트가 필요할 수 있고 " +
            "-nographics는 시각 캡처에 사용하면 안 된다.\n" +
            "6. Editor가 포커스를 잃었을 때 Play 표시만 믿지 말고 " +
            "프레임 카운터/Time.frameCount 변화나 서로 다른 시간대의 렌더 " +
            "증거로 게임 업데이트 진행을 확인하라. capture_game_view가 " +
            "Editor 타임아웃으로 데스크톱 전체 스크린샷으로 폴백했다면 " +
            "게임 전용 캡처 PASS로 기록하지 마라. 데스크톱 캡처가 발생하면 " +
            "민감한 화면 정보가 포함되지 않게 주의하라.\n" +
            "7. 색상·요소·이펙트의 전후 프레임 비교와 시각적 수정·재촬영 계획, " +
            "1280x720 및 1920x1080, 시작·정답·오답·결과·테마 0~10 " +
            "검증 매트릭스를 만든다. 실패·미검증은 PASS가 아니다.\n" +
            "8. 문서만 작성하지 말고 실제 Unity Scene, Canvas, Prefab, UI Sprite, " +
            "Material, Shader, Animator, Particle, 미적 배치와 시각 전용 스크립트를 " +
            "직접 제작·저장하라. 필요한 시각 파일에는 쓰기 권한이 있다. " +
            "독립 WORK의 게임 규칙과 이전 체크포인트는 보존하고, " +
            "같은 파일의 WORK 동시 수정을 금지한다.\n\n" +
            "@@RESULT_CONTRACT\n" +
            "[ACTION=RESULT] @@REPORT <STATUS>completed|in_progress|blocked</> " +
            "<SUMMARY>실제 그래픽 변경과 캡처·타임라인·명령 기록</> " +
            "<CHANGED_PATH>실제 수정 파일(반복)</> " +
            "<ISSUES>미확인·실패</> [RESPONSE=OK].";
    }

    // A Markdown document by itself is not evidence of graphic production.
    internal static bool IsVisualArtifact(string path) =>
        new[] { ".unity", ".prefab", ".mat", ".shader", ".shadergraph",
            ".png", ".jpg", ".jpeg", ".svg", ".spriteatlas",
            ".anim", ".controller", ".uxml", ".uss", ".vfx" }
            .Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ||
        (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
         new[] { "/UI/", "/Visual", "/Editor/", "/Presentation/" }
            .Any(fragment => ("/" + path.Replace('\\', '/')).Contains(fragment,
                StringComparison.OrdinalIgnoreCase)));

    internal static bool IsVerifiedDocument(string root, MilestoneDesignDefinition design) =>
        MilestoneDefinitionContract.IsSafeRelativePath(design.Path) &&
        File.Exists(Path.Combine(Path.GetFullPath(root),
            design.Path.Replace('/', Path.DirectorySeparatorChar)));
}
