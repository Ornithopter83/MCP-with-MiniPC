# UnityWorker Visual Foundry — DESIGN이 명시적으로 호출하는 UI 제작 도구

## 목표

DESIGN #0이 필요하다고 선택했을 때 Unity Editor의 게임 UI 제작에 활용 가능한 **완성형 화면 후보·절차적 그래픽·재질·모션·런타임 데이터 바인딩 표면**을 준비한다. Visual Foundry는 신규 AI 역할이 아니라 Worker가 DESIGN에 공급하는 모델 비의존적인 제작 도구다. 기존 HQ 프로토콜, DESIGN, ASSET MANAGER, WORK, HIGH, Git 정책은 유지한다.

## DESIGN 결정 → MCP 실행

1. DESIGN은 기존 에셋과 요구사항을 검토하여 Foundry 사용 여부를 결정한다. HQ 문장 키워드로 Worker가 자동 선택하지 않는다.
2. 사용을 선택했다면 MCP `worker_command`의 `visual_foundry`에 `style`(neon/arcane/luxury/playful), `layout`(Base/Editorial/Immersive), `description`을 **모두 명시**한다.
3. Worker는 입력값의 유효성 및 Editor 실행 가능 상태만 확인하고, 스크립트를 실행한다. Play Mode에서는 생성할 수 없다는 기존 Unity Editor 제약을 유지한다.
4. Worker는 기존 프로젝트 시각 에셋의 단순 파일 목록을 만들 수 있으나 우선순위·적합성·라이선스를 결정하지 않는다. ASSET MANAGER가 실제 후보를 선정한다.
5. DESIGN은 생성 결과를 실제 게임 씬과 데이터에 연결한다. Worker의 Native PNG와 픽셀 통계는 관측 자료이고 품질 합격 판정이 아니다.
6. Worker는 화면 평탄도만으로 자동 보정·재시도·DESIGN 추가 호출을 하지 않는다. DESIGN이 판단하고 HIGH가 최종 검증한다.

## 생성 자산 (uGUI)

`Assets/ProjectHub/VisualFoundry/{job}_{milestone}_{style}/`

| 분류 | 생성 항목 |
|---|---|
| Menu / Quiz / Results | 3개 화면 × Base / Editorial / Immersive = 9개 Prefab |
| 미리보기 | `Preview-Menu.unity`, `Preview-Quiz.unity`, `Preview-Results.unity` |
| 절차적 스프라이트 | `Backdrop.png`, `Halo.png` |
| Shader/Material | `PrismaticUI.shader`, 지원될 때 `PrismaticUI.mat` |
| 모션 | 화면별 `*-intro.anim`과 `*-pulse.anim`, 페이드·스케일·발광 |
| 구조 감사 | `LayoutAudit.json`: 생성 정보와 미검증 상태 |

기본 디자인 해상도는 **1920×1080**이며, Unity CanvasScaler로 실제 창 비율을 따라간다. UI 아트 킷의 화면 예시는 실제 게임플레이 논리로 간주하지 않는다. Unity UI/Default 호환 셰이더가 지원되지 않는 환경은 기본 Image 렌더링으로 유지된다.

## 실제 데이터·이벤트 연결

최초 생성 성공 시 기존 파일을 덮어쓰지 않고 아래 런타임 소스 두 개를 설치한다.

- `Assets/ProjectHub/VisualFoundry/Runtime/VisualFoundryBridge.cs`
- `Assets/ProjectHub/VisualFoundry/Runtime/VisualFoundryWatcher.cs`

네임스페이스 `ProjectHub.VisualFoundry`, API:
- `VisualFoundryBridge.ApplyQuiz(question, choices, number, total)`
- `VisualFoundryBridge.ApplyResults(title, summary)`
- `VisualFoundryBridge.ApplyMenu(title, subtitle)`
- `VisualFoundryBridge.ShowScreen("Quiz")`
- `VisualFoundryBridge.ActionPressed += (screen, index) => { ... }`

브리지는 화면 찾기·텍스트 변경·버튼 번호 이벤트 전달만 수행한다. 실제 정답 판정, 점수, 데이터 로드, 화면 흐름은 **기존 게임 코드의 책임**이며 DESIGN이 실제 프로젝트에 맞게 연결해야 한다. 동적 생성된 Prefab도 런타임 watcher가 일정 주기로 버튼을 연결한다.

## 안전성과 검증 의미

- 기존 Scene을 닫거나 덮어쓰지 않고 별도 미리보기 Scene을 생성한다. Preview 생성 뒤 원래 Scene을 활성으로 복귀시킨다.
- 생성 경로에 기존 킷이 있다면 전체 파일 구성이 확인된 경우 재사용한다. 일부만 남은 경우 기존 파일을 덮어쓰지 않고 충돌로 보고한다.
- 재사용 후보 에셋과 사용자 편집한 런타임 바인딩 파일은 절대로 덮어쓰지 않는다.
- 생성된 PNG·Prefab·에셋 수량·CLI 성공은 디자인 미적 승인/실제 GUI 동작 확인을 의미하지 않는다. Visual Pixel Audit도 빈 이미지 탐지 정도의 보조 지표다.
- 최종 검증은 Windows Action의 Worker 빌드·테스트와 실제 Unity Editor에서의 9개 Prefab Import/미리보기/게임 UI 이벤트 확인으로 구분한다.
- Figma·Scenario·유료 Unity Asset Store 팩은 별도의 라이선스·연동 승인이 있어야 사용한다. 이 버전은 완성형 상용 아트팩을 자동 획득하지 않는다.
