# CurrentWork

Updated: 2026-09-24

정책 원본: Master-Polish.md

## Current policy

Worker는 판단하지 않는다. Worker는 흐름 제어 도구다.

신규 CLI에서 AI가 반환하는 제어 계약은 ACTION/GOTO뿐이며 제어행 뒤의 내용은 opaque body다.

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

## Current implementation status

핵심 ACTION+GOTO router, HIGH one-shot permit, JUDGE raw transport, 역할별 contract 파일 분리는 구현돼 있다.

사용자 Explorer 기본 경로에서 HQ → WORK → HQ → END 실제 진행은 확인됐지만, WORK 응답 body가 transcript에는 남고 메시지/작업 이력 카드에는 표시되지 않는 문제가 확인됐다.

추가 점검 결과 신규 CLI 역할 계약에 INSTRUCTION/REPORT/VALIDATION REQUEST/JUDGMENT 같은 semantic body tag 요구가 남아 있고, JudgeTransportContract가 VALIDATION REQUEST marker를 검색하며, JUDGE raw 결과에도 JUDGMENT marker를 삽입하고 있다.

따라서 11-C-GOTO-CONTRACT는 아직 완료가 아니다.

## Active residual — opaque body + History

- 역할 output contract에서 INSTRUCTION/REPORT/VALIDATION REQUEST/JUDGMENT 요구 제거
- WorkerGotoContract는 ACTION/GOTO만 파싱
- JudgeTransportContract는 GOTO:JUDGE 뒤 body 전체를 request로 사용
- native JUDGE raw response에 JUDGMENT marker 삽입 금지
- Worker role/state/response completion로 History 카드 직접 생성
- 신규 CLI History에서 LUNA/JEV/source 문자열 추론 제거
- 카드 1줄: body 기계적 truncate + …
- 카드 2줄: token usage
- 카드 3줄: file change telemetry
- 파일 생성/수정/삭제 타입을 모르면 추정하지 않음
- 전체 원문은 transcript/detail에 유지

## Card format

~~~text
<본문 첫 유효 텍스트를 한 줄로 축약> …
토큰 · 총 N · 입력 N · 캐시 N · 출력 N
파일 · 생성 N · 수정 N · 삭제 N · 대표파일 외 N개
~~~

usage 미제공:

~~~text
토큰 · 미제공
~~~

파일 변경 없음:

~~~text
파일 · 변경 없음
~~~

현재 CodexCliFile telemetry가 생성/수정/삭제 타입을 제공하지 않으면 우선 파일 N개 감지처럼 사실만 표시하고, 정확한 구분이 필요하면 기계적 FileChangeTelemetry를 추가한다.

## Worker boundary

Worker가 처리:
- ACTION/GOTO 문법과 상태 전이
- session/provider 실행
- timeout/cancel/auth/transport
- transcript/usage/file telemetry
- HIGH permit
- UI 카드의 기계적 표시 formatting

Worker가 판단하지 않음:
- 요구사항 충족 여부
- AC/test/evidence 충분성
- JUDGE 의미 결과
- 재작업/HIGH 필요 여부
- 최종 완료 여부
- 카드용 의미적 요약

## Verification required

자동 검증은 기존 GOTO baseline에서 통과했지만 이번 opaque-body/history 수정 후 다시 실행해야 한다.

Explorer 재검증:
- HQ→WORK→HQ→END: HQ/WORK/HQ 카드가 순서대로 표시
- HQ→WORK→JUDGE→WORK→HQ→END: JUDGE와 복귀 WORK 카드 표시
- HIGH permit→HQ→HIGH→HQ: HIGH 카드 표시
- invalid route/provider error→UNKNOWN→HQ: 오류 카드/관제 복귀 확인

각 카드에서 AI 본문 tag 검색 없이 3줄 메타 표시가 나와야 한다.

## Other residual

- 11-UI-B-EXPLORER-COLORS: 실제 Explorer 색상 확인 잔여