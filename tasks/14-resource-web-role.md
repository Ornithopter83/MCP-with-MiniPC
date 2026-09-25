# 14 RESOURCE Web 역할 + HQ Web 복원

갱신일: 2026-09-25

정책 원본은 Master-Polish.md다.

## 목표

기존 HIGH 역할을 완전히 제거하고, 별도 ChatGPT Web 대화에서 최종 생성 이미지를 만들고 지정 경로에 저장하는 RESOURCE 역할을 도입한다. 동시에 HQ는 ChatGPT Web 또는 CLI 제공자를 선택할 수 있게 복원한다.

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

## A — HIGH 제거

- WorkerRoleState.High 제거
- GOTO:HIGH 제거
- HIGH-ROUTING-계약 삭제
- JobHighLevelPermit 삭제
- highLevel 설정/제공자/모델/추론/세션 제거
- 고수준 작업 허용 checkbox 제거
- 파이프라인/이력의 HighLevel 의미 제거

## B — HQ Web 대상

- HQ 대상 = ChatGPT Web / CLI
- Web 선택 시 제공자/모델/추론/세션 숨김
- CLI 선택 시 OpenAI/Claude/Muse 제공자 구조 유지
- WORK는 CLI-only 유지
- coordinator transport=web을 codex_cli로 자동 정규화하지 않음
- HQ Web도 CLI HQ와 동일한 HQ 계약 사용

## C — 명시적 Web 연결

브라우저 확장에서 각 대화을 명시적으로:
- HQ
- RESOURCE

중 하나로 연결한다.

같은 대화을 두 역할에 동시에 연결하지 않는다. 생존 신호는 생존 확인 확인에만 사용한다. Worker 작업 생성은 role 연결 conversationId를 사용한다.

## D — RESOURCE 전송

WORK body는 자연어만 사용한다.

~~~text
[GOTO : RESOURCE]
<자연어 이미지 생성 요청>
~~~

Worker는 본문이 비어 있지 않은지만 기계적으로 확인하고 ChatGPT Web에 그대로 전달한다. 저장 경로/파일명은 Worker가 requestId 기반으로 생성한다.

ResourceRequest:
- Id
- Type
- Prompt
- TargetDirectory
- TargetFileName
- RequestedBy
- 상태
- SavedPath
- WorkspaceRoot (전송 내부 저장 안전 경계)

상태:
QUEUED -> REQUESTED -> GENERATING -> DOWNLOADING -> SAVED | FAILED

IMAGE:
- RESOURCE ChatGPT Web에서 한 번에 1건씩 생성
- 실행 중 새 요청은 FIFO 대기열에 적재
- 확장이 최신 assistant turn의 생성 이미지들을 모두 다운로드해 bytes 배열로 반환
- Worker가 작업공간 하위 requestId 폴더에 image-NN.*로 저장
- WORK는 RESOURCE 완료를 기다리지 않는다. 접수 사실은 HQ로 돌아가며 이후 의미적 다음 단계는 HQ가 결정
- HQ END 전 후속 WORK에 실제로 필요한 완료 결과만 기계적으로 전달
- HQ END 시 의미 흐름을 종료하고 미완료 RESOURCE는 Worker의 기계적 대기 작업으로만 추적

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
- Worker/테스트 소스에서 HIGH 구조 잔존 없음
- HQ는 GOTO:WORK만 허용
- WORK GOTO:HQ/JUDGE/RESOURCE
- RESOURCE 전송 경로 이탈 차단
- 관제 Web 전송 보존
- HQ/RESOURCE 동일 대화 연결 거부

실환경:
- solution test/빌드
- HQ CLI 왕복
- HQ Web 왕복
- 두 Web 대화 동시 생존 신호
- RESOURCE image 실제 생성/저장
- RESOURCE 접수 사실이 HQ로 복귀하고 HQ가 다음 의미적 지시를 결정
- 자동 통합 없음
- JUDGE 회귀


## F — 설정/UI 후속 보정

화면 확인 피드백:
- 대기 상태 전체 컬러는 정상 정책이므로 변경하지 않는다.
- WORK 기본값은 OpenAI / GPT-6 Luna / Medium으로 둔다.
- 저장된 WORK 모델을 별도 schema 마이그레이션으로 치환하지 않는다.
- 설정의 HQ/RESOURCE Web 카드는 역할별 연결/생존 신호/확장 상태를 표시한다.
- role-specific Web 사전 점검를 사용하여 다른 Web 창의 최신 생존 신호가 실행 여부에 영향을 주지 않게 한다.
- 제공자 아이콘 대비, 텍스트 줄바꿈, 스크롤/고정 하단 영역, 역할명 표기를 정리한다.


## G — RESOURCE Web 실행/관측성 후속 보정

- RESOURCE 전용 JSON/role 래퍼 제거, 자연어 직접 전달
- Bridge 작업 role과 선점 주체 분리
- Worker 송신/생명주기 기록 추가
- resource 오류 단계 세분화
- WORK 기록 출처를 WORK CLI로 유지
- 오류가 있었던 정상 END는 DONE_WITH_ERROR 계측


## H — 이미지 로드 완료 후속 보정

- 생성 이미지 DOM 삽입과 실제 이미지 로드 완료를 분리해 처리
- load/오류 event에서 응답 감시기 재평가
- text-only no-image 판단 시간 초과을 120초로 두고, 시간 초과 시점에 이미지가 로드됐으면 성공 전송
- 확장 초기화에서 진행 상황/소유자/response state 초기화
- CLI 송신 Worker 프롬프트 기록 추가


- HQ/RESOURCE 소유자 작업의 레거시 처리기 차단을 활성 플래그와 분리
- HQ Web 확장 진행 상황에서 관제 단계 유지
- 종료 후 오래된 진행 상황가 legacy UI를 재활성화하지 않도록 보호 로직


## I — RESOURCE 사이드카 대기열 / 복수 이미지 / 종료 대기

- RESOURCE는 단일 읽기 FIFO 사이드카 대기열로 실행
- 동시에 RESOURCE Web 작업 1건만 허용
- 실행 중 후속 RESOURCE 요청은 QUEUED
- WORK는 대기열 접수 후 관제 흐름을 HQ에 반환
- 완료된 RESOURCE 결과는 다음 WORK 호출에 기계적으로 함께 전달
- 최신 assistant turn의 생성 이미지 전부 다운로드
- requestId별 폴더에 image-NN.* 저장
- RESOURCE 카드 독립 궤도 + 대기열 count/상태
- HQ END 후 미완료 RESOURCE가 있으면 FINALIZING, 대기열 유휴 전 DONE 금지
- 1초 completion watchdog으로 생성 완료 후 다운로드 고착 방지

- 콘텐츠 스크립트 이미지 가져오기 실패 시 백그라운드 서비스 워커 대체 처리
- 작은 UI 이미지를 생성 이미지 후보에서 제외
- RESOURCE 송신 프롬프트 / 브리지 작업 기록 유지

## J — 계약 일반화

- ACTION/GOTO 외 의사 제어 대괄호 제거
- 역할 계약에서 특정 사용자 요청·도메인·횟수·장애 사례 제거
- JUDGE 전송 예시는 구체 시나리오가 아닌 자리표시자 문법로만 유지
- RESOURCE 계약는 자연어 body와 역할 경계만 규정
- 특정 검증 사례는 tests/fixtures로 이동


## K — RESOURCE 다운로드 고착 방지 강화

- 기준선 이후 새 대형 이미지를 assistant/main 영역에서 탐색
- image response 절대 마감 시간 120초
- 로드 완료 이미지 안정화 후 스트리밍 표기와 무관하게 IMAGE_READY/DOWNLOAD_START 진행
- IMAGE_DETECTED 후보/로드 완료 진행 상황
- Worker RESOURCE 전송 5분 시간 초과
- 시간 초과 시 해당 브리지 작업을 resource_timeout FAILED로 종료해 다음 FIFO 슬롯 해제
- 확장 0.1.7 / 빌드 2026-09-25.1


## L — RESOURCE 완료 HQ 깨우기 폐기와 일반 대기 게이트

- RESOURCE 완료마다 HQ를 깨우는 별도 완료 이벤트를 제거한다.
- HQ ACTION=END는 의미 작업 종료를 즉시 확정한다.
- END 이후 Worker는 HQ/WORK/JUDGE 의미 흐름을 다시 열지 않는다.
- END 이후 WORK 보고가 HQ로 향하면 "HQ의 작업은 종료되었습니다."로 차단한다.
- RESOURCE 미완료은 Worker가 관리하는 기계적 대기 작업의 한 종류로 취급한다.
- 기계적 대기 작업이 남아 있으면 대기 상태에서 AI 호출 없이 완료를 기다린다.
- 모든 기계적 대기 작업이 끝나면 Worker가 DONE 또는 DONE_WITH_ERROR로 전환한다.
- [GOTO : RESOURCE]는 새 이미지 생성 요청 한 건 전용이며 상태 조회·취소·추적에 사용하지 않는다.



## M — PAUSE/END 후 작업 추가

- PAUSE와 DONE / DONE_WITH_ERROR에서 현재 HQ/WORK 세션과 작업공간을 보존한다.
- END는 현재 실행 구간의 종료이며 세션 자체를 폐기하지 않는다.
- 사용자가 명시적으로 `작업 추가`를 실행할 때만 `USER_FOLLOWUP`으로 HQ부터 새 실행 구간을 연다.
- 자동 후속 호출이나 자동 재개는 하지 않는다.
- `메시지 및 작업 이력` 그룹 크기는 고정하고, 중단/완료 시 목록 아래에 약 두 개 이력 카드 높이의 후속 메시지 입력 영역을 삽입한다.
- 하단 기존 실행/새 작업 버튼 왼쪽에 녹색 `작업 추가` 버튼을 표시한다.
- `새 작업`은 기존 연속 세션과 이력을 명시적으로 초기화하는 동작으로 유지한다.
- 동일 작업 transcript는 후속 구간이 추가될 때 같은 파일을 갱신한다.
