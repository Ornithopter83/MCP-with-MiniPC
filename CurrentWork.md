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

2026-09-24 직접 수정으로 신규 CLI 역할 contract에서 INSTRUCTION/REPORT/VALIDATION REQUEST/JUDGMENT 출력 요구를 제거했다. JudgeTransportContract는 GOTO:JUDGE 뒤 body 전체를 opaque request로 사용하고, native JUDGE raw response에는 JUDGMENT marker를 다시 붙이지 않는다.

HQ/WORK/JUDGE/HIGH 응답 완료 시 Worker가 현재 role/state와 usage/files telemetry를 직접 사용해 History 카드를 생성하도록 연결했다. 카드 UI는 제목 아래에 1줄 preview, token line, file line을 표시하며 body tag/source 문자열로 역할을 추론하지 않는다. 현재 CodexCliFile에는 create/modify/delete 구분이 없으므로 파일 줄은 'N개 감지'로만 표시하고 유형을 추정하지 않는다.

이번 변경분은 저장소 코드 검토로 반영 여부를 확인했지만, 현재 GPT 실행 환경에는 .NET SDK가 없어 dotnet test/build를 새 변경분에 대해 직접 실행하지 못했다. 따라서 자동 빌드·테스트와 Explorer 실제 왕복 재검증은 잔여다.

11-C-GOTO-CONTRACT는 아직 완료가 아니다.

## 2026-09-24 GOTO_INVALID 회귀 수정

Explorer에서 HQ가 `[GOTO=WORK]` / `GOTO=WORK`를 출력해 strict parser가 `GOTO_INVALID`로 UNKNOWN→HQ를 반복하는 문제가 확인됐다.

수정:
- HQ/WORK/HIGH/JUDGE role contract에 정확한 `[GOTO : ...]` wire 예시를 명시
- GOTO는 반드시 colon(`:`)과 square bracket을 사용하도록 계약에 명시
- HQ `[AVAILABLE GOTO]` header도 `WORK/HIGH` 문자열이 아니라 실제 `[GOTO : WORK]`, `[GOTO : HIGH]` 형태로 제공
- parser는 느슨하게 만들지 않고 strict syntax 유지
- `[GOTO=WORK]`, `GOTO=WORK`가 `GOTO_INVALID`인 회귀 테스트 추가

이번 수정 이후 Explorer 재실행에서 HQ가 정확히 `[ACTION=CONTINUE]` + `[GOTO : WORK]`를 출력하는지 확인해야 한다.

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

## 2026-09-24 최신 원격 동기화·빌드·게시

- 원격 `main`의 최신 커밋 `60db01f`까지 fast-forward 동기화했다. `GPT-Web-Feedback.md`의 opaque body/History 변경과 strict GOTO wire 보완 사항을 확인했다.
- 전체 테스트 첫 실행에서 Worker 1건이 실패했다. HIGH permit이 없는 HQ prompt footer에도 `[GOTO : HIGH]`가 노출되는 계약/테스트 불일치였다.
- HQ contract footer를 permit 여부에 따라 필터링하도록 수정하고 회귀 테스트를 추가했다. 검증: `dotnet test ProjectHub.sln --no-restore` 통과 (Worker 59, Server 1, Agent 3, Core 1).
- 검증: `dotnet build ProjectHub.sln -c Release --no-restore` 성공 (경고 0, 오류 0); `dotnet publish src/ProjectHub.Worker/ProjectHub.Worker.csproj -c Release -r win-x64 --no-restore` 성공; `git diff --check` 통과.
- 게시 EXE를 `C:\AI-AGENT\Worker`에 복사했고 양쪽 SHA-256은 `7D68083D103296898692429C0BF5DDCBD63148311B31CE81C17719FFA08E3FEC`로 일치한다. 실행 중인 Worker 프로세스는 없었다. `C:\GameProject`는 없다.
- 잔여: Explorer A~D 실화면 왕복 검증. 이번 변경은 아직 commit/push하지 않았다.

## 2026-09-24 제어행 prefix·키워드 판별

- 첫 유효 행의 `[ACTION`/`[GOTO` prefix로 control family를 식별한 뒤 `CONTINUE`/`PAUSE`/`END`, `HQ`/`WORK`/`JUDGE`/`HIGH`/`UNKNOWN`의 포함 여부로 후보 값을 판별한다.
- ACTION/GOTO 제어줄이 한 쌍의 바깥 대괄호로 닫혀야 하고 키워드 후보가 하나만 매칭되어야 한다. 닫는 대괄호 누락·중복 또는 다중 키워드 후보는 거부한다. 상태별 허용 route 및 HIGH permit 검사는 유지한다.
- 기존 테스트 기대값을 새 오류 분류에 맞게 수정했다. 이 파서 변경 이후 자동 테스트는 실행하지 않았고, Release build/publish는 경고 0·오류 0으로 통과했다.
- `C:\AI-AGENT\Worker`에 게시 EXE를 갱신 복사하고 SHA-256 `CBCC02AF7038EE9D1C8D14783753DD5219666734A2E24722B047ACD1893B0E5C` 일치를 확인했다. `git diff --check` 통과.
