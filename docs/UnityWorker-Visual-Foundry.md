# UnityWorker Visual Foundry — 자동 고급 비주얼 제작 킷

## 개요

Visual Foundry는 DESIGN #0에 공급하는 **로컬·결정론적** Unity 프리미엄 화면 킷 생성기다. HQ의 기존 `@@DESIGN=0` 명세에 게임/화면/UI 제작 지시가 포함되면 Worker가 연결된 Unity Editor를 통해 별도 킷 생성을 시도한다. AI가 매번 프리셋·배경·버튼 구조를 새로 설계할 필요가 없다. 새로운 AI 역할, HQ 제어 문법, 외부 서비스는 추가하지 않는다.

## 생성 규격

- 스타일: Neon Command / Arcane Atelier / Noir Edition / Nova Pop.
- 생성물: `Assets/ProjectHub/VisualFoundry/{job}_{milestone}_{style}/` 내 Menu, Quiz, Results Prefab과 Preview.unity, Backdrop.png, 화면 등장 AnimationClip 3개.
- 배경: 픽셀 계산으로 제작하는 512×288 색상 그라데이션·기하학적 패턴 기반 Sprite.
- UI: Canvas/CanvasScaler 기준 해상도 1920×1080, 패널·타이포그래피·선택 효과·버튼·색상 계층을 갖춘 uGUI 프리팹.
- 모션: CanvasGroup 알파를 이용한 각 화면 등장 애니메이션.
- 설계안: `temp/ProjectHub/{job}/visual-foundry/{milestone}/design-dna.json` 기록. 킷 제작 소스도 같은 temp 하위에 배치.

## 실행과 책임

1. Worker가 HQ의 DESIGN 지시에서 화면 제작 요청을 판별하고 규칙 기반 스타일을 선택한다.
2. Unity Editor 연결 확인 후 Editor 독점 구간에서 `eval_file`로 프리팹과 미리보기 씬을 생성한다. 기존 사용자 씬을 열거나 덮어쓰지 않는다.
3. 생성된 에셋 경로와 상태를 DESIGN 프롬프트에 전달한다. DESIGN은 실제 게임 씬·데이터·동작 이벤트에 킷을 연결하고 필요 시 자유롭게 재구성한다.
4. DESIGN 자체 그래픽 결과물과 Worker의 Native Game View 캡처를 분리해 기록한다. 생성 성공은 시각 품질 검증 또는 실제 입력 동작 성공과 같지 않다.
5. 이미 생성된 스타일 킷이 필요 없거나 일부만 필요한 경우 DESIGN은 선택적으로 재사용한다. 외부 에셋은 기존 ASSET MANAGER 계약에 따른다.

`NO_VISUAL_FOUNDRY` 지시가 있거나 화면 제작 작업이 아닌 경우 자동 생성하지 않는다. Editor 비연결/생성 실패 시 실패를 기록하고 기존 DESIGN 경로는 그대로 실행한다.

## 도입 제약 및 후속 검증

- 현재 프리미엄 킷은 **미리 준비된 그래픽/타이포그래피/레이아웃 구조를 자동 생성하는 기반 구현**이며, 실제 사용자 게임 UI의 완성형 자동 교체 기능은 아니다. 데이터 바인딩은 DESIGN이 수행한다.
- 프로시저럴 배경, UI 프리팹, 애니메이션 생성 후 Unity Editor에서 Import·Preview·Game View를 실제로 확인해야 한다.
- 유료 에셋, 사용자 승인이 없는 외부 API, 프로젝트의 기존 사용자 UI를 자동으로 덮어쓰는 작업은 하지 않는다.
- 비전 기반 미학 평가, 다중 화면 후보 탐색, 외부 고급 그래픽 패키지는 이 구현에 포함하지 않는다.
