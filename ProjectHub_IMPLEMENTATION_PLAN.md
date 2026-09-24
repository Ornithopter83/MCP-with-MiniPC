# ProjectHub 구현 로드맵

Updated: 2026-09-24

정책 원본은 Master-Polish.md다.

## Current architecture

~~~text
HQ      -> WORK | HIGH
WORK    -> JUDGE | HQ
JUDGE   -> WORK
HIGH    -> HQ
UNKNOWN -> 원문 한글 로그 + HQ 한글 요약 (Job당 1회), 이후 정상 관제 재개
반복 UNKNOWN -> 로그 기록 후 종료
~~~

- ACTION은 HQ만 CONTINUE/PAUSE/END를 사용
- 신규 CLI 행선지는 GOTO
- ACTION/GOTO 뒤의 모든 내용은 opaque body
- Worker는 판단하지 않고 흐름·session·transport·telemetry만 관리
- HIGH는 사용자 실행 시점 one-shot permit
- Legacy Web NEXT:WEB/JEV는 별도 legacy mode

## Current active work — 13-A JUDGE plan review flow

목표는 WORK가 JUDGE에 바로 넓은 NOUL 질문을 반복하는 대신, 판정이 필요할 때 먼저 HQ에 판정 초안과 evidence를 보내 검토받고 그 검토안을 WORK가 인지한 뒤 JUDGE로 전달하게 하는 것이다.

~~~text
WORK -> HQ -> WORK -> JUDGE -> WORK -> HQ
~~~

구현 원칙:
- 기존 GOTO 상태 그래프 사용
- 새 Worker state/control token 없음
- Worker가 body 의미나 HQ 검토 여부를 검사하지 않음
- NOUL/SCORE/CHOICE 사용을 비율이나 금지 규칙으로 규제하지 않음
- WORK/HQ contract에 다양한 실제형 예시 추가
- HQ는 판정 초안의 수치화 가능한 부분, evidence, 질문 범위를 검토
- WORK는 검토안을 읽은 뒤 실제 JUDGE request 작성
- 재판정도 같은 검토 흐름을 다시 사용할 수 있음

상세: `tasks/13-judge-plan-review.md`

12-B Provider-ready 구조는 완료 기록으로 유지하고, Claude/Muse 실제 연동은 기존 12-D deferred 항목으로 남긴다.

## Previous active residual — 11-C-GOTO-CONTRACT

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

## 2026-09-24 제어행 닫힘 판별 후속

- ACTION/GOTO 후보는 시작 접두어로 구분하고, 제어어 포함 상태와 그 제어어 바로 뒤의 닫는 `]`로 제어 토큰을 판별한다. 닫는 괄호가 줄의 마지막 문자일 필요는 없다.
- 괄호 뒤 같은 줄의 설명 문구는 라우팅 본문에 보존한다. 상태 전이/HIGH permit은 유지한다.
- `_20260924_154428.txt` 로그에서 첫 WORK 응답의 `[GOTO : HQ]` 뒤 설명 문구가 기존 parser에 의해 거부된 것을 확인했다. 재지시의 “Worker는 GOTO 제어선을 출력하지 말고” 문구는 HQ AI가 생성한 본문이며 Worker 코드에서 삽입되지 않았다. 재시도 WORK가 GOTO 없이 본문만 반환해 오류가 종결됐다.
- Worker 테스트 61개 통과. 이번 변경 이후 Release build/publish 및 실행파일 복사는 잔여다.

## 2026-09-24 UNKNOWN 오류 및 JEV WORK 계약

- UNKNOWN 오류 원문/기술 상세는 한글 시스템 로그에 보관하고 HQ에는 발생 역할·코드·설명만 담은 한글 요약을 Job당 1회 전달해 관제를 재개한다. 요약 전달 후 오류가 재발하면 로그 기록 후 종료한다.
- `[ROLE : UNKNOWN]` 및 `[ERROR ENVELOPE : JSON]` 프롬프트 봉투를 삭제했다.
- WORK footer에 JEV 호출 시 `[GOTO : JUDGE]`와 원자적 `NOUL`/`SCORE`/`CHOICE` 질문, 고유 QID, SCORE/CHOICE 기준, 선택적 `EVIDENCE:` 경로 형식을 명시했다. JUDGE 비활성 시 이 지침은 프롬프트에서 빠진다.
- 전체 테스트 67개 통과 (Worker 62, Agent 3, Core 1, Server 1), Release build 경고 0/오류 0, Worker publish 성공.
- 실행 중 Worker 프로세스를 종료한 뒤 게시 EXE를 `C:\AI-AGENT\Worker\ProjectHub.Worker.exe`에 복사하고 SHA-256 `460EFAC18A399E3F1E4197807883C3418507B3A4729B2EA3FEE8F6BC4D487CAF` 일치를 확인했다.
- Explorer 화면에서 실제 오류를 재현해 추가 AI 호출이 없는지 확인하는 것은 잔여다.

2026-09-24 후속: 사용자 결정에 따라 UNKNOWN의 원문·기술 상세는 로컬 로그에 보관하고, HQ에는 한글 요약만 Job당 1회 전달해 관제를 재개한다. 반복 오류는 로그 후 종료한다. WORK JEV footer와 API 계약에 atomic NOUL/SCORE/CHOICE, QID, 기준값, 선택적 PASS/SCOPE/COUNTEREXAMPLE/EVIDENCE, 일부 QID만 재판정하도록 명시했으며 transport parser 호환 테스트를 추가했다. 전체 테스트 69개 통과, Release 빌드 경고 0/오류 0, publish/복사 완료. SHA-256 `8C22F3D50CFFEAB2E27A6D84C7568D84A9ECEEA04F5D7DEA9A5B70626891A95A`. Explorer 오류 복귀 E2E 잔여.

## UI residual

- 11-UI-B-EXPLORER-COLORS: 실제 Explorer 색상 확인 잔여

## Policy guard

Worker가 작업 결과를 판단하게 만드는 로직은 금지한다.

UI를 위해 AI에게 ACTION/GOTO 외 semantic tag를 출력시키는 설계도 금지한다.

History는 Worker가 이미 보유한 실행 사실과 telemetry로 만든다.

## 2026-09-24 동기화 빌드 기록

최신 원격 `main` `60db01f`를 동기화했다. 전체 테스트에서 무허가 HQ prompt에 HIGH GOTO 문구가 노출되는 회귀가 발견되어 permit별 footer 필터와 테스트를 추가했다. `dotnet test ProjectHub.sln --no-restore` 통과 (Worker 59, Server 1, Agent 3, Core 1), Release build/publish 성공 (경고 0, 오류 0), `git diff --check` 통과. EXE를 `C:\AI-AGENT\Worker`에 복사하고 SHA-256 일치를 확인했다. Explorer A~D는 미검증 잔여로 유지한다.

제어행 검사도 `[ACTION`/`[GOTO` prefix로 후보를 구분한 뒤 기존 strict syntax parser에 넘기도록 보완했다. 오류 분류 기대값을 갱신했다. 이 후속 parser 조정은 자동 테스트 미실행 상태이나 Release build/publish 성공, `git diff --check` 통과 및 실행파일 복사·해시 대조를 완료했다.

후속 조정: 제어 prefix 식별 후 ACTION/GOTO 키워드를 포함 검색으로 판별하며, 유일한 키워드와 바깥 닫는 대괄호 한 쌍을 요구한다. 이 버전으로 Release build/publish 성공; 전체 테스트는 미실행이다. 실행파일 복사본 SHA-256은 `CBCC02AF7038EE9D1C8D14783753DD5219666734A2E24722B047ACD1893B0E5C`.

## 2026-09-24 현재 작업 pipeline UI 피드백 적용

최신 동기화 피드백의 표시 기준에 따라 상단 pipeline 화살표를 제거하고 단계 간격을 12px로 축소했다. 각 단계 카드에 base gold outline과 2초 주기의 gold dashed orbit을 추가하고, `_currentTaskStage`에 해당하는 카드만 표시·애니메이션하도록 구현했다. Idle도 실제 current일 때 활성화하며 HIGH permit만으로는 활성화하지 않는다. Judge 비활성 카드에는 애니메이션을 표시하지 않는다. role color/grayscale 정책은 유지한다.

상단 arrow routing animation과 미사용 `_nextTaskStage` 상태는 제거했다. `_flowTimer`, 하단 legacy Flow arrow 및 Judge pulse는 유지했고, Worker routing/JEV/UNKNOWN/HIGH permit/History 코드는 수정하지 않았다.

검증: Debug/Release solution build 모두 경고 0·오류 0, 전체 69개 테스트 통과, `git diff --check` 통과. 실제 Explorer UI 확인은 Computer Use 런타임이 `Trusted RPC service is not configured: sky` 오류로 연결되지 않아 잔여다.
