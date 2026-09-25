# 14 RESOURCE Web 역할 + HQ Web 복원

Updated: 2026-09-25

정책 원본은 Master-Polish.md다.

## 목표

기존 HIGH 역할을 완전히 제거하고, 별도 ChatGPT Web 대화에서 최종 생성 이미지를 만들고 지정 경로에 저장하는 RESOURCE 역할을 도입한다. 동시에 HQ는 ChatGPT Web 또는 CLI Provider를 선택할 수 있게 복원한다.

## 상태 그래프

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE_QUEUE
JUDGE    -> WORK
RESOURCE_QUEUE 접수 -> HQ (RESOURCE_QUEUED)
RESOURCE_QUEUE 실행 -> RESOURCE Web -> 완료 알림 queue
HQ ACTION=END -> 의미 흐름 종료 고정 -> Worker 기계적 대기 작업 확인
기계적 대기 작업 있음 -> 대기 -> 모두 종료 -> DONE / DONE_WITH_ERROR
UNKNOWN  -> HQ 요약 1회
~~~

## A — HIGH removal

- WorkerRoleState.High 제거
- GOTO:HIGH 제거
- HIGH-ROUTING-CONTRACT 삭제
- JobHighLevelPermit 삭제
- highLevel settings/provider/model/reasoning/session 제거
- 고수준 작업 허용 checkbox 제거
- pipeline/history의 HighLevel 의미 제거

## B — HQ Web target

- HQ target = ChatGPT Web / CLI
- Web 선택 시 Provider/Model/Reasoning/session 숨김
- CLI 선택 시 OpenAI/Claude/Muse provider 구조 유지
- WORK는 CLI-only 유지
- coordinator transport=web을 codex_cli로 자동 normalize하지 않음
- HQ Web도 CLI HQ와 동일한 HQ contract 사용

## C — explicit Web binding

브라우저 확장에서 각 conversation을 명시적으로:
- HQ
- RESOURCE

중 하나로 binding한다.

같은 conversation을 두 역할에 동시에 binding하지 않는다. heartbeat는 liveness 확인에만 사용한다. Worker task 생성은 role binding conversationId를 사용한다.

## D — RESOURCE transport

WORK body는 자연어만 사용한다.

~~~text
[GOTO : RESOURCE]
<natural-language image generation request>
~~~

Worker는 본문이 비어 있지 않은지만 기계적으로 확인하고 ChatGPT Web에 그대로 전달한다. 저장 경로/파일명은 Worker가 requestId 기반으로 생성한다.

ResourceRequest:
- Id
- Type
- Prompt
- TargetDirectory
- TargetFileName
- RequestedBy
- Status
- SavedPath
- WorkspaceRoot (transport 내부 저장 안전 경계)

상태:
QUEUED -> REQUESTED -> GENERATING -> DOWNLOADING -> SAVED | FAILED

IMAGE:
- RESOURCE ChatGPT Web에서 한 번에 1건씩 생성
- 실행 중 새 요청은 FIFO queue에 적재
- 확장이 최신 assistant turn의 생성 이미지들을 모두 다운로드해 bytes 배열로 반환
- Worker가 workspace 하위 requestId 폴더에 image-NN.*로 저장
- WORK는 RESOURCE 완료를 기다리지 않는다. 접수 사실은 HQ로 돌아가며 이후 의미적 다음 단계는 HQ가 결정
- HQ END 전 후속 WORK에 실제로 필요한 완료 결과만 기계적으로 전달
- HQ END 시 의미 흐름을 종료하고 outstanding RESOURCE는 Worker의 기계적 대기 작업으로만 추적

## E — 역할 계약

HQ:
- 새/대규모 사용자 목표는 필요한 수준으로 구현 방향 설계
- 작은 후속은 영향 범위만 갱신
- PAUSE는 사람 확인/취향/로그인/권한/사용자 선택이 필요할 때 사용

WORK:
- 최종 이미지/아이콘/스프라이트/배경은 RESOURCE 우선
- placeholder는 임시 확인용만 허용
- RESOURCE 저장 결과를 사용자 후속 명령 없이 자동 연결하지 않음

RESOURCE:
- 생성/저장만
- 코드 작성/연결/통합 금지

## 검증

자동:
- Worker/Test source에서 HIGH 구조 잔존 없음
- HQ only GOTO:WORK
- WORK GOTO:HQ/JUDGE/RESOURCE
- RESOURCE transport traversal 차단
- coordinator web transport 보존
- HQ/RESOURCE 동일 conversation binding 거부

실환경:
- solution test/build
- HQ CLI roundtrip
- HQ Web roundtrip
- 두 Web conversation 동시 heartbeat
- RESOURCE image 실제 생성/저장
- RESOURCE 접수 사실이 HQ로 복귀하고 HQ가 다음 의미적 지시를 결정
- 자동 integration 없음
- JUDGE 회귀


## F — 설정/UI 후속 보정

화면 확인 피드백:
- 대기 상태 전체 컬러는 정상 정책이므로 변경하지 않는다.
- WORK 기본값은 OpenAI / GPT-6 Luna / Medium으로 둔다.
- 저장된 WORK 모델을 별도 schema migration으로 치환하지 않는다.
- 설정의 HQ/RESOURCE Web 카드는 역할별 binding/heartbeat/extension 상태를 표시한다.
- role-specific Web preflight를 사용하여 다른 Web 창의 latest heartbeat가 실행 여부에 영향을 주지 않게 한다.
- Provider icon contrast, text wrapping, scroll/fixed footer, 역할명 표기를 정리한다.


## G — RESOURCE Web 실행/관측성 후속 보정

- RESOURCE 전용 JSON/role wrapper 제거, 자연어 direct forwarding
- Bridge task role과 claimer 분리
- Worker outbound/lifecycle transcript 추가
- resource 오류 단계 세분화
- WORK WORK -> WORK CLI
- error가 있었던 정상 END는 DONE_WITH_ERROR telemetry


## H — 이미지 로드 완료 후속 보정

- generated image DOM insertion과 실제 image load 완료를 분리해 처리
- load/error event에서 response observer 재평가
- text-only no-image 판단 timeout을 120초로 두고, timeout 시점에 이미지가 로드됐으면 성공 전송
- extension reset에서 progress/owner/response state 초기화
- CLI outbound Worker prompt transcript 추가


- HQ/RESOURCE Owner task의 legacy handler 차단을 active flag와 분리
- HQ Web extension progress에서 Coordinator stage 유지
- 종료 후 stale progress가 legacy UI를 재활성화하지 않도록 guard


## I — RESOURCE 사이드카 대기열 / 복수 이미지 / 종료 대기

- RESOURCE는 single-reader FIFO sidecar queue로 실행
- 동시에 RESOURCE Web task 1건만 허용
- 실행 중 후속 RESOURCE 요청은 QUEUED
- WORK는 queue 접수 후 orchestration을 HQ에 반환
- 완료된 RESOURCE 결과는 다음 WORK 호출에 기계적으로 함께 전달
- 최신 assistant turn의 생성 이미지 전부 다운로드
- requestId별 폴더에 image-NN.* 저장
- RESOURCE 카드 독립 orbit + queue count/status
- HQ END 후 outstanding RESOURCE가 있으면 FINALIZING, queue idle 전 DONE 금지
- 1초 completion watchdog으로 생성 완료 후 다운로드 고착 방지

- content script 이미지 fetch 실패 시 background service worker fallback
- 작은 UI 이미지를 generated image candidate에서 제외
- RESOURCE outbound prompt / bridge task transcript 유지

## J — 계약 일반화

- ACTION/GOTO 외 pseudo-control 대괄호 제거
- 역할 contract에서 특정 사용자 요청·도메인·횟수·장애 사례 제거
- JUDGE transport 예시는 concrete scenario가 아닌 placeholder grammar로만 유지
- RESOURCE contract는 자연어 body와 역할 경계만 규정
- 특정 검증 사례는 tests/fixtures로 이동


## K — RESOURCE 다운로드 고착 방지 강화

- baseline 이후 새 large image를 assistant/main 영역에서 탐색
- image response absolute deadline 120초
- loaded image settle 후 streaming 표기와 무관하게 IMAGE_READY/DOWNLOAD_START 진행
- IMAGE_DETECTED candidate/loaded progress
- Worker RESOURCE transport 5분 timeout
- timeout 시 해당 bridge task를 resource_timeout FAILED로 종료해 다음 FIFO slot 해제
- extension 0.1.7 / build 2026-09-25.1


## L — RESOURCE 완료 HQ 깨우기 폐기와 일반 대기 게이트

- RESOURCE 완료마다 HQ를 깨우는 별도 완료 이벤트를 제거한다.
- HQ ACTION=END는 의미 작업 종료를 즉시 확정한다.
- END 이후 Worker는 HQ/WORK/JUDGE 의미 흐름을 다시 열지 않는다.
- END 이후 WORK 보고가 HQ로 향하면 "HQ의 작업은 종료되었습니다."로 차단한다.
- RESOURCE outstanding은 Worker가 관리하는 기계적 대기 작업의 한 종류로 취급한다.
- 기계적 대기 작업이 남아 있으면 대기 상태에서 AI 호출 없이 완료를 기다린다.
- 모든 기계적 대기 작업이 끝나면 Worker가 DONE 또는 DONE_WITH_ERROR로 전환한다.
- [GOTO : RESOURCE]는 새 이미지 생성 요청 한 건 전용이며 상태 조회·취소·추적에 사용하지 않는다.

