# GPT Web 피드백 — 토큰 누적 억제

갱신일: 2026-09-26
기준 정책: Master-Polish.md

## 반영 완료

- AGENTS.md의 과거 계획/작업 문서 자동 조사 지시를 제거했다.
- 프로그램 시작 시 저장된 continuation과 이벤트 이력을 자동 복구하지 않는다.
- 새 작업은 선택되거나 설정에 남은 과거 CLI session ID를 실행 세션으로 사용하지 않는다.
- HQ WorkGraph 상태 통지는 전체 snapshot 반복 전송 대신 직전 HQ 전달 이후 변경된 WorkItem만 전달한다.
- 같은 작업의 USER_FOLLOWUP에서도 이벤트 로그 경로, handoff, 전체 WorkGraph를 HQ 입력에 다시 붙이지 않는다.
- HQ/WORK 역할 계약 전문은 각 AI 세션 첫 호출에만 주입하고 같은 세션 resume에는 재주입하지 않는다.

## 유지 범위

- 현재 프로그램 실행 안의 `작업 추가`는 같은 작업의 기존 HQ/WORK 세션과 WorkGraph를 이어갈 수 있다.
- 기존 .projecthub 기록 파일 형식과 worktree 실행 구조 자체는 이번 변경에서 제거하지 않았다.
