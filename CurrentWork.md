# CurrentWork

Updated: 2026-09-24

## Current policy

정책 원본: Master-Polish.md

**Worker는 판단하지 않는다. Worker는 흐름 제어 도구다.**

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

ACTION은 HQ만 CONTINUE/PAUSE/END를 사용한다.

신규 CLI-to-CLI는 GOTO를 사용한다.

## Implementation status (2026-09-24)

11-C-GOTO-CONTRACT 코드 변경과 자동 검증은 완료했다. Explorer 실제 왕복 검증은 사용자가 직접 수행할 잔여 작업이다.

적용: HQ ACTION+GOTO 상태표, UNKNOWN→HQ 오류 전달, WORK/HIGH 분리 footer, JEV raw 응답의 동일 WORK 세션 복귀, HIGH 실행 시점 one-shot permit, Worker 의미판정·자동 재시도 제거. Legacy Web NEXT:WEB/JEV는 유지했다.

검증: `dotnet test ProjectHub.sln --no-restore` 통과 (Worker 56, Server 1, Agent 3, Core 1), `dotnet build ProjectHub.sln -c Release --no-restore` 성공 (경고 0, 오류 0). Explorer E2E는 수행하지 않았다.

## Implemented scope

- HQ ACTION + GOTO, HQ/WORK/JUDGE/HIGH/UNKNOWN 전이를 구현했다.
- JEV adapter는 raw 응답만 반환하며 같은 WORK 세션으로 전달한다. PASS/PARTIAL threshold 판정, evidence 의미 재검사와 자동 재시도를 제거했다.
- HIGH는 실행 시 체크한 one-shot permit으로만 호출한다.
- protocol/provider/session/transport 오류는 UNKNOWN envelope로 HQ에 전달한다.
- Legacy Web NEXT:WEB/JEV는 보존했다.

Explorer 실사용 경로 검증은 사용자 직접 확인 대상으로 남아 있다.

## Worker boundary

Worker는 다음을 기계적으로 처리한다.

- 제어행 문법
- 상태 전이
- session/provider 실행
- timeout/cancel/auth/transport
- transcript/usage
- HIGH permit

Worker는 다음을 판단하지 않는다.

- 요구사항 충족 여부
- AC PASS/FAIL
- 테스트 충분성
- evidence 충분성
- JUDGE/JEV 의미 결과
- 재작업 필요 여부
- HIGH 필요 여부
- 최종 완료 여부

## HIGH permit

~~~text
unchecked + Run -> high_uses_remaining = 0
checked   + Run -> high_uses_remaining = 1
~~~

HIGH dispatch 직전에 permit을 소모한다.

Worker는 사용자 텍스트에서 HIGH 허가를 추론하지 않는다.

## Verification required

자동 검증 결과: `dotnet test ProjectHub.sln --no-restore` 통과. 사용자 요청에 따라 항목 6 Explorer 실제 검증은 제외하고 사용자가 직접 확인한다.

단위:
- 상태 전이 parser/router
- HQ-only ACTION
- JUDGE→same WORK
- HIGH→HQ only
- HIGH one-shot permit
- UNKNOWN→HQ
- Worker semantic gate 없음

Explorer:
- HQ→WORK→HQ→END
- HQ→WORK→JUDGE→WORK→HQ→END
- HIGH 허가→HQ→HIGH→HQ
- invalid route/provider error→UNKNOWN→HQ

## Other residual

- 11-UI-B-EXPLORER-COLORS: 실제 Explorer 색상 확인 잔여

과거 구현 계약은 현재 판단 기준으로 사용하지 않는다.
