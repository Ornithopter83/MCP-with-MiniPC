# GPT Web 피드백 — 계약문서 무시 직통 작업

갱신일: 2026-09-26
기준 정책: Master-Polish.md

## 구현

- 하단 `계약문서 무시` 체크박스는 기본 해제다.
- 체크 시 왼쪽에 서비스 제공사 / 모델 / 추론 깊이 선택 항목을 표시한다.
- 실행과 작업 추가에서 체크 상태이면 정상 HQ/WorkGraph 경로 대신 선택한 모델을 현재 작업 폴더에서 직접 실행한다.
- 사용자 입력은 HQ/WORK 계약 포맷으로 가공하지 않는다.
- Codex CLI에는 직통 모드에서 `project_doc_max_bytes=0`을 전달해 프로젝트 AGENTS 지침 자동 주입을 막는다.
- 직통 모드의 요청, 진행, 결과는 모두 작업 History 카드로 기록한다.
- 실행 중 Pipeline은 작업 카드만 활성화한다.
- 정상 실행 경로의 기본 동작과 runner 기본 옵션은 변경하지 않는다.

## 변경 파일

- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `MainWindow.DirectWork.cs`
- `AiRoleRunner.cs`
- `CodexCliRunner.cs`
