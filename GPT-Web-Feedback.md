# GPT Web 피드백 — RESOURCE 역할 전환

갱신일: 2026-09-24
기준 정책: Master-Polish.md
활성 작업: tasks/14-resource-web-역할.md

## 현재 방향

ProjectHub의 현재 역할 흐름은 다음과 같다.

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE_QUEUE
JUDGE    -> WORK
RESOURCE_QUEUE 접수 -> HQ
RESOURCE_QUEUE 실행 -> RESOURCE Web -> 완료 알림 queue
~~~

HIGH 역할과 일회성 허가 구조는 제거한다.

## 구현 초점

- HQ는 ChatGPT Web 또는 CLI 제공자를 선택할 수 있다.
- WORK는 CLI 제공자 기반 구현 역할이다.
- RESOURCE는 별도 ChatGPT Web 대화에 고정한다.
- HQ Web과 RESOURCE Web은 명시적 역할 연결을 사용한다.
- 생존 신호는 생존 확인용이며 목적지 선택에 사용하지 않는다.
- RESOURCE 실제 범위는 IMAGE 생성 -> 복수 다운로드 -> requestId별 작업공간 저장이며 메인 역할 흐름과 분리된 FIFO 사이드카로 실행한다.
- RESOURCE가 저장한 파일은 사용자의 별도 연결 명령 전까지 코드/CSS/HTML에 자동 연결하지 않는다.

## Worker 경계

Worker는 ACTION/GOTO, 상태 전이, 프로세스/세션, Web 연결, 전송 스키마, 작업공간 path safety, file/사용량 계측만 기계적으로 처리한다.

Worker가 판단하지 않는 것:
- 리소스 품질
- 리소스 사용 위치
- JUDGE 결과의 의미
- 작업 완료 여부
- 다음 작업의 의미적 우선순위

## 남은 검증

현재 Web 작업 환경에는 .NET SDK/Windows Explorer 실행 환경이 없으므로 다음은 Windows에서 실검증해야 한다.

- dotnet test ProjectHub.sln
- Release 빌드/게시
- HQ CLI 흐름
- HQ Web 흐름
- HQ/RESOURCE 두 Web 대화 동시 생존 신호 격리
- WORK -> RESOURCE 대기열 접수 -> 실제 이미지 생성/복수 다운로드/저장 -> 이후 orchestration/마무리 반영
- 저장된 RESOURCE가 자동 integration되지 않는지 확인
- JUDGE 회귀


## 2026-09-24 UI 확인 후 보정

- 대기 상태에서 5개 Pipeline 카드가 모두 컬러인 것은 의도된 정책이다.
- WORK 기본값은 OpenAI / GPT-6 Luna / Medium이며 별도 모델 마이그레이션 로직은 두지 않는다.
- HQ/RESOURCE Web 상태는 각각 독립 연결 기준으로 표시하고 사전 점검한다.
- 설정창 제공자 아이콘 대비, 긴 Web 설명 wrapping, 작은 화면 scroll/footer, 설계·관제/판정 용어를 보정한다.


## 2026-09-24 RESOURCE Web 실제 왕복 피드백

- RESOURCE Web에는 JSON/역할 계약을 보내지 않고 WORK가 만든 자연어 이미지 요청만 그대로 보낸다.
- 저장 경로/파일명은 Worker 내부에서 requestId 기반으로 생성한다.
- Web 클레임이 HQ/RESOURCE 역할 식별 정보를 덮어쓰지 않게 역할과 ClaimedBy를 분리한다.
- Worker 송신와 RESOURCE 생명주기을 기록에 남긴다.
- RESOURCE 오류를 생성 없음/캡처/다운로드/저장/Web 전달 단계로 나눈다.


## 2026-09-24 RESOURCE 이미지 로드 보강

- 생성 이미지 element가 먼저 생기고 load만 나중에 끝나는 경우를 별도로 처리한다.
- load 이벤트가 오면 즉시 RESOURCE 응답을 재평가한다.
- 추가 DOM mutation이 없어도 시간 초과 검사에서 이미지가 로드됐으면 정상 저장 경로로 진행한다.
- CLI HQ/WORK 송신 프롬프트도 원본 기록에 남긴다.


## 2026-09-24 RESOURCE 사이드카 방향 반영

- RESOURCE 실행은 WORK/HQ/JUDGE와 분리된 FIFO 사이드카로 둔다.
- RESOURCE Web은 동시에 1건만 실행하며 새 요청은 실패 대신 대기열에 넣는다.
- 복수 생성 이미지는 한 assistant turn에서 전부 수집해 각각 다운로드/저장한다.
- HQ가 END를 반환해도 RESOURCE 실행/대기가 남아 있으면 Worker는 FINALIZING으로 유지한다.
- RESOURCE 카드 애니메이션은 메인 활성 역할과 독립한다.
- 이미지 생성 완료 후 DOM mutation이 끊겨도 1초 watchdog이 완료 감시를 계속한다.


## 2026-09-24 계약 일반화

- 역할 계약에는 역할 책임, ACTION/GOTO, 전송 형식, Worker/AI 경계만 남긴다.
- 특정 사용자 요청·테스트·도메인·횟수·파일명·장애 사례는 계약에 넣지 않는다.
- RESOURCE 접수/완료는 Worker의 기계적 사실로 HQ에 전달하며 의미적 다음 단계는 HQ가 현재 목표와 실행 결과로 결정한다.
- JUDGE 형식은 concrete example 대신 placeholder grammar로만 안내한다.
