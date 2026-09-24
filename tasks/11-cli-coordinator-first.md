# 11 Coordinator-first CLI-to-CLI

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

## Current active scope — 11-C-GOTO-CONTRACT

핵심 GOTO router는 구현됐지만 Explorer 기본 경로에서 WORK 응답 카드 누락이 확인됐고, 신규 CLI 역할 계약에 불필요한 semantic body tag가 남아 있다.

현재 목표는 **ACTION/GOTO만 제어 계약으로 남기고 body를 완전히 opaque하게 전달하면서, History 카드는 Worker의 role/state/telemetry로 직접 기록하는 것**이다.

## Core invariant

Worker는 판단하지 않는다.

AI 출력에서 Worker가 해석하는 것은:
- HQ의 ACTION
- 역할의 GOTO

뿐이다.

제어행 뒤 전체 문자열은 opaque body다.

INSTRUCTION, REPORT, VALIDATION REQUEST, JUDGMENT 같은 semantic section tag는 신규 CLI 계약에서 사용하지 않는다.

## State contract

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> HQ
~~~

## Output examples

HQ:

~~~text
[ACTION=CONTINUE]
[GOTO : WORK]
<opaque body>
~~~

WORK:

~~~text
[GOTO : HQ]
<opaque body>
~~~

또는:

~~~text
[GOTO : JUDGE]
<opaque body>
~~~

HIGH:

~~~text
[GOTO : HQ]
<opaque body>
~~~

native JEV는 AI routing token을 출력하지 않는다. raw provider response를 같은 WORK session에 opaque body로 반환한다.

## Required implementation changes

### A. Role contracts

- HQ-ROUTING-CONTRACT.md에서 INSTRUCTION/REPORT 요구 제거
- WORK-ROUTING-CONTRACT.md에서 REPORT/VALIDATION REQUEST 요구 제거
- HIGH-ROUTING-CONTRACT.md에서 REPORT 요구 제거
- JUDGE-ROUTING-CONTRACT.md에서 JUDGMENT 요구 제거
- role footer는 허용 ACTION/GOTO와 금지 route만 설명

### B. Judge transport

- JudgeTransportContract.ExtractRequest()의 VALIDATION REQUEST marker 검색 제거
- WORK의 GOTO:JUDGE 뒤 body 전체를 provider request source로 사용
- native JEV raw response 앞에 JUDGMENT marker를 붙이지 않음
- same WORK session에 raw body를 그대로 전달

### C. History

새 wire/event protocol을 만들지 않는다.

Worker는 이미 다음을 알고 있다:
- 현재 WorkerRoleState
- 어떤 role call이 끝났는지
- parsed ACTION/GOTO
- Codex/JEV usage
- result.Files 및 향후 file change telemetry

이 정보를 직접 History card builder에 전달한다.

CreateHistoryEvent()에서 신규 CLI role을 LUNA/JEV/SOL/HIGH 같은 source 문자열로 역추론하지 않는다.

### D. Card display

역할명/아이콘/시간 외 본문은 3줄:

~~~text
<첫 유효 body 텍스트를 100~140자 내에서 잘라 표시> …
토큰 · 총 N · 입력 N · 캐시 N · 출력 N
파일 · 생성 N · 수정 N · 삭제 N · 대표 파일 외 N개
~~~

규칙:
- 요약은 추가 AI 호출 없이 whitespace normalize + deterministic truncate
- 짧아서 잘리지 않으면 …를 붙이지 않음
- usage unknown이면 토큰 · 미제공
- 파일 변경 없으면 파일 · 변경 없음
- create/modify/delete telemetry가 없으면 추정 금지
- 현재 CodexCliFile(path/name/mime/size)만으로 change type을 만들지 않음
- 필요하면 별도 FileChangeTelemetry를 기계적으로 수집
- 상세 body/usage/files는 transcript/detail에 보존

## HIGH one-shot

메인 화면 고수준 작업 허용 체크 + 실행 클릭 시 현재 Job에만 permit 1회를 만든다.

HIGH 사용 여부는 HQ가 판단한다. Worker는 permit 가용성만 전달한다.

## UNKNOWN

protocol/provider/session/transport 오류를 source/code/detail로 HQ에 전달한다.

오류 body에도 UI 분류용 semantic tag를 요구하지 않는다.

## Regression tests

- HQ parser가 ACTION/GOTO만 요구
- WORK/HIGH parser가 GOTO만 요구
- semantic body tag가 없어도 정상 route
- body에 REPORT/JUDGMENT 문자열이 있어도 routing에 영향 없음
- WORK GOTO:JUDGE 뒤 body 전체가 Judge transport 입력
- native JUDGE raw body가 marker 없이 same WORK session으로 복귀
- WORK response completion 시 Implementer History card 1개 생성
- HIGH response completion 시 HighLevel History card 1개 생성
- JUDGE response completion 시 Judge History card 1개 생성
- HQ response completion 시 Coordinator History card 생성
- card summary는 deterministic truncate
- usage unknown을 0으로 표시하지 않음
- file change type unknown을 created/modified로 추정하지 않음
- Legacy Web NEXT:WEB/JEV 회귀 없음

## Explorer E2E

~~~text
A. HQ -> WORK -> HQ -> END
B. HQ -> WORK -> JUDGE -> WORK -> HQ -> END
C. HIGH permit -> HQ -> HIGH -> HQ
D. error -> UNKNOWN -> HQ
~~~

A 경로에서 최소:
- 사용자 작업 요청 카드
- HQ 작업 지시 카드
- WORK 수행 결과 카드
- HQ 최종 결과 카드

가 순서대로 보이고, 각 AI 응답 카드가 3줄 표시 규격을 따라야 한다.

## Implementation update — 2026-09-24 direct edit

적용:
- HQ/WORK/HIGH/JUDGE role footer에서 semantic body tag 요구 제거
- JudgeTransportContract의 VALIDATION REQUEST marker 의존 제거
- native JUDGE raw response의 JUDGMENT marker 재삽입 제거
- HQ/WORK/JUDGE/HIGH/UNKNOWN 응답 완료 지점에서 typed History 카드 기록
- History 카드 1줄 preview + token line + file line UI 적용
- 신규 CLI History는 role/state를 직접 사용하고 source 문자열 추론을 우회
- 현재 file telemetry가 change type을 제공하지 않으므로 created/modified/deleted를 추정하지 않고 'N개 감지'로 표시
- contract/formatter 회귀 테스트 추가

검증 상태:
- 저장소 최신 코드/계약 재조회로 적용 여부 확인
- 현재 GPT 실행 환경에는 .NET SDK가 없어 이번 변경 이후 dotnet test/build는 미실행
- Explorer A~D 실제 왕복도 재검증 필요

## Completion condition

11-C-GOTO-CONTRACT는 ACTION/GOTO 외 semantic body tag 의존성이 신규 CLI runtime과 History에서 제거되고, Explorer에서 역할별 카드가 누락 없이 표시된 뒤 완료 처리한다.

## 2026-09-24 최신 동기화 검증·게시

- 동기화 기준: `main` `60db01f`.
- 전체 테스트 첫 실행에서 무허가 HQ prompt의 footer가 `[GOTO : HIGH]`를 여전히 포함하는 회귀가 드러났다. 역할 계약을 permit에 따라 필터링하고 회귀 테스트를 보강했다.
- `dotnet test ProjectHub.sln --no-restore`: 통과 (Worker 59, Server 1, Agent 3, Core 1).
- `dotnet build ProjectHub.sln -c Release --no-restore`: 성공, 경고 0/오류 0.
- `dotnet publish src/ProjectHub.Worker/ProjectHub.Worker.csproj -c Release -r win-x64 --no-restore`: 성공.
- 게시 EXE를 `C:\AI-AGENT\Worker`에 복사했고 SHA-256 `7D68083D103296898692429C0BF5DDCBD63148311B31CE81C17719FFA08E3FEC` 일치. Explorer A~D 검증은 미수행. 현재 변경은 commit/push 전 상태다.

## 2026-09-24 제어행 prefix 후보 판별 후속

- `[ACTION`/`[GOTO` 시작 문자열로 control candidate를 식별하고 exact parser가 실제 wire 문법을 검증한다. 유효 문법 허용 범위는 확장하지 않았다.
- malformed candidate를 `ACTION_INVALID`/`GOTO_INVALID`로 분류하도록 하고 기존 test 기대값을 갱신했다.
- 이 parser 변경 뒤 자동 테스트는 미실행. `dotnet build ProjectHub.sln -c Release --no-restore` 및 Worker Release publish는 경고 0/오류 0으로 성공했다.
- 게시 EXE를 `C:\AI-AGENT\Worker`로 복사했고 SHA-256 `ACF5EF21945D14AEB40A4DB97D22602639B76096943ED8C6B654277A29BE3389`를 대조했다. `git diff --check` 통과.

### 제어행 키워드 포함 판별 후속

- `[ACTION`/`[GOTO` 시작으로 후보 종류를 식별하고 ACTION/GOTO 키워드 포함 상태로 값을 판별한다.
- 바깥 대괄호 한 쌍이 닫히고 일치 키워드가 하나인 경우만 파싱하며, role route/HIGH permit 제한은 그대로 적용한다.
- 이 변경 뒤 전체 테스트는 실행하지 않았다. Release build/publish 성공, `git diff --check` 통과. `C:\AI-AGENT\Worker` 복사본 SHA-256: `CBCC02AF7038EE9D1C8D14783753DD5219666734A2E24722B047ACD1893B0E5C`.
