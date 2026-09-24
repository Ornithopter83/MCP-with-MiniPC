# ProjectHub 구현 로드맵

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

## Current architecture

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

- ACTION은 HQ만 CONTINUE/PAUSE/END를 사용
- 신규 CLI 행선지는 GOTO
- ACTION/GOTO 뒤의 모든 내용은 opaque body
- Worker는 판단하지 않고 흐름·session·transport·telemetry만 관리
- HIGH는 사용자 실행 시점 one-shot permit
- Legacy Web NEXT:WEB/JEV는 별도 legacy mode

## Current active work — 11-C-GOTO-CONTRACT

핵심 GOTO router와 HIGH/JUDGE transport baseline은 구현돼 있다.

Explorer 기본 경로에서 WORK 응답이 transcript에는 남지만 History 카드에 누락되는 문제가 확인됐다. 동시에 역할 output contract에 INSTRUCTION/REPORT/VALIDATION REQUEST/JUDGMENT 같은 불필요한 semantic tag 요구가 남아 있다.

현재 마무리 범위:
- 신규 CLI output contract를 ACTION/GOTO only로 단순화
- role body를 완전 opaque 전달
- JudgeTransport marker 의존 제거
- JUDGE raw body marker 삽입 제거
- History card를 role/state/response completion/usage/file telemetry로 직접 생성
- source 문자열과 body tag 기반 History 분류 제거
- 카드 3줄 표시: 1줄 축약, 2줄 token, 3줄 file change
- file change 정보가 없으면 추정하지 않음
- 단위 테스트/Release build/Explorer E2E 재검증

## Card display target

~~~text
<응답 첫 유효 텍스트를 짧게 표시> …
토큰 · 총 N · 입력 N · 캐시 N · 출력 N
파일 · 생성 N · 수정 N · 삭제 N · 대표파일 외 N개
~~~

전체 원문과 상세 telemetry는 transcript/detail에 유지한다.

## Deferred

- JobRunner crash/restart 복구
- 추가 Provider 실연동
- 비용 기반 자동 정책
- 고급 예산/호출 제한 UX
- 기타 대규모 리팩터링

## UI residual

- 11-UI-B-EXPLORER-COLORS: 실제 Explorer 색상 확인 잔여

## Policy guard

Worker가 작업 결과를 판단하게 만드는 로직은 금지한다.

UI를 위해 AI에게 ACTION/GOTO 외 semantic tag를 출력시키는 설계도 금지한다.

History는 Worker가 이미 보유한 실행 사실과 telemetry로 만든다.