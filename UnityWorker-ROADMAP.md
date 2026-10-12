# ProjectHub UnityWorker — 개발 상태 및 단계별 기준

- 기준: 범용 ProjectHub `v1.0.0` / `aea8279917801b1bff0c1f45d4e956389a98d625`
- 개발 브랜치: `UnityWorker` (범용 `main`과 별도)
- `Master-Polish.md`는 읽기 전용으로 유지한다.
- UnityWorker 브랜치는 **Worker 제품 소스**의 브랜치이며 사용자의 게임 저장소 Git 브랜치와 다르다.

## 책임 분리

| 역할 | 현재 실행 주체 | 표시 카드 | 목표 산출물 |
|---|---|---|---|
| 프로젝트 조정 | HQ | Unity HQ | 요구·완료 기준 및 WORKITEM 설계 |
| #1 에셋 전문 역할 | ASSET MANAGER (첫 구현) | #1 Asset Manager | 검색→획득/Import→씬 적용, 단계별 체크포인트·차단 정책 |
| 도구·플러그인 점검 | Worker Unity 사전 점검 (부분 구현) | 실행 이력 | 프로젝트/Pipeline 및 CLI 응답; MCP 인증·세션은 미검증 |
| Editor 연결·독점 | Worker Editor 연결 Probe (부분 구현) | 실행 이력 | --project-path로 Editor 읽기 명령 확인; 독점 정책은 직렬 WORK |
| 게임플레이 구현 | WORK #10+ | Unity WORK | C# 코드, 컴포넌트 구현 |
| 씬 구성 | WORK #10+ | Unity WORK | 씬·프리팹 저장과 참조 구성 |
| 실행 검증 | QA + Unity CLI 컴파일 BUILD (첫 구현) | Unity QA | CLI 컴파일 및 추후 Console·PlayMode 증거 |
| 통합·릴리스 | HIGH + Worker Git | Unity HIGH | 통합 검토 및 Commit/Push |
| 비차단 이미지 | RESOURCE #0 | #0 IMAGE | GPTWEB 이미지 (기존 동작 유지) |

## 1차 구현에 실제 반영한 범위

- WORKITEM ID, 세션, 체크포인트, HQ/WORK/QA/HIGH 결과 문법 유지
- Unity 역할별 작업·검증 계약과 Worker-Polish 확장
- 화면 파이프라인/모델 설정/이력 카드 이름을 Unity용으로 수정
- 공유 Editor 경쟁 상태를 피하기 위해 Unity WORK를 한 번에 하나만 실행하도록 제한
- UnityWorker 브랜치 대상으로 .NET CI 실행 및 직렬 정책 회귀 테스트 추가
- Unity 프로젝트로 인식되면 Worker BUILD 단계에서 Unity CLI `recompile`을 호출하고, 컴파일 실패(exit 6)와 연결·환경 실패를 구분 (Windows 실증 전)
- HQ 실행 전에 프로젝트 구조·Pipeline 의존성과 `unity command --project-path ... --format json` 읽기 전용 Probe를 확인하고, 도구·Editor 오류가 나면 WORK 실행 전에 차단 (실제 Windows 실증 전)

- #1 ASSET MANAGER 역할 계약·HQ 우선 계획 문법·직렬 3단계 실행·독립 세션·WORK 선행 실행·카드 표시 및 체크포인트 반영 (Windows Editor 실증 전)

## 다음 작업 (구현·실증 전)

1. Unity Toolchain 실증·확장: 현재 Probe를 실제 Windows Unity CLI에서 검증하고 Codex WORK 샌드박스의 MCP 등록·호출/ACL까지 확인 (HQ 시작 전 Probe가 MCP 사용 가능성을 보장하지는 않음)
2. Editor 독점 스케줄러: Gameplay 소스 작업은 병렬로 되돌리고 Scene 작업만 독점 구간에서 실행
3. 기계 BUILD 실증·보강: Unity CLI `recompile`의 실제 Windows 응답/로그 검증, Editor Console 및 컴파일 상태 세부 파싱
4. QA: EditMode/PlayMode 테스트, 프레임·입력·카메라 동작 증거 검증, 미검증 상태 명시
5. Unity Asset/Git: .meta, 프리팹 및 Packages/ProjectSettings 승인 범위, 캐시/생성물 제외
6. Windows 실증과 회귀 검증을 통과한 후 UnityWorker 베타 프리릴리스 발행

## 안전 기준

- `BUILD_SKIPPED`는 Unity 컴파일 통과를 뜻하지 않는다. Editor 또는 MCP와의 연결이 없으면 실제 플레이테스트 성공을 보고하지 않는다.
- 연결·인증 장애는 환경 문제로 즉시 보고하며 구현 WORK를 무의미하게 반복하지 않는다.
- Editor 기능은 Unity 공식 CLI/Pipeline/MCP를 우선 재사용하고 별도의 씬 조작 서버를 새로 만들지 않는다.
- UnityWorker 제품 개발과 실제 Unity 게임 프로젝트의 Git 브랜치·최종화는 별개의 계약이다.
