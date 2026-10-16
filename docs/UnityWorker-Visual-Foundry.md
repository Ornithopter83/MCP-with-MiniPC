# UnityWorker Visual Foundry — 자동 프리미엄 UI 제작 엔진

## 목표

HQ 설계안과 기존 DESIGN #0 배정만으로 Unity Editor의 게임 UI 제작에 활용 가능한 **완성형 화면 후보·절차적 그래픽·재질·모션·런타임 데이터 바인딩 표면**을 준비한다. Visual Foundry는 신규 AI 역할이 아니라 Worker가 DESIGN에 공급하는 모델 비의존적인 제작 도구다. 기존 HQ 프로토콜, DESIGN, ASSET MANAGER, WORK, HIGH, Git 정책은 유지한다.

## HQ → Worker → DESIGN 실행

1. Worker는 HQ의 전체 승인된 지시(`RawHqMessage`)와 마일스톤 DESIGN 지시를 함께 해석한다. 별도의 Visual Foundry 제어 태그는 없다. 필요하면 `NO_VISUAL_FOUNDRY`로 제외한다.
2. Theme: Neon Command(SF), Arcane Atelier(판타지), Noir Edition(럭셔리), Nova Pop(캐주얼). **AI 호출 없이** 키워드 기반으로 선택하고, 최소 대비를 자동 보정한다.
3. 정보 밀도·연출 의도를 기준으로 Base / Editorial / Immersive 화면 구도를 추천한다. 디자인 명세는 `temp/ProjectHub/.../visual-foundry/.../design-dna.json`으로 저장한다.
4. 지정 프로젝트의 기존 Prefab, Material, Shader, UI 이미지를 **읽기 전용**으로 조사하여 `existing-visual-assets.json`에 적합한 재사용 후보를 기록한다. 사용자가 보유한 유료 Kit를 임의 다운로드하거나 라이선스를 추정하지 않는다.
5. Editor 독점 레인에서 기존 Editor의 `eval_file`로 생성 스크립트를 실행한다. 생성 성공 시 Runtime Bridge를 독립된 경로에 추가한다. 새로운 MonoBehaviour가 컴파일될 때까지 게임의 바인딩 기능을 확인된 사실로 보고하지 않는다.
6. DESIGN은 게임 목표에 맞춰 선택한 Prefab을 실제 Scene에 통합하고 게임 데이터를 연결한다. 뒤따르는 Worker 캡처는 품질 검토용 증거이지 자동 합격 판단이 아니다.
7. Worker는 실제 PNG 내용을 경량 디코딩하여 매우 평탄하거나 거의 빈 화면을 감지한다. 확실한 경우 **동일한 DESIGN 배정에서 한 번만** 시각 보정 지시를 실행하고 재캡처한다. 미생성/CLI 실패/미확인 PNG는 재시도 사유로 자동 간주하지 않는다.

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
