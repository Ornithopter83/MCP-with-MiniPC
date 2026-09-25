# ProjectHub 구현 로드맵

갱신일: 2026-09-24

정책 원본: Master-Polish.md

## 현재 구조

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE_QUEUE
JUDGE    -> WORK
RESOURCE_QUEUE 접수 -> HQ (RESOURCE_QUEUED)
RESOURCE_QUEUE 실행 -> RESOURCE Web (FIFO 1건) -> 완료 알림 queue
HQ END 전 완료 알림 -> 필요 시 다음 WORK 입력
HQ ACTION=END -> Worker 기계적 대기 게이트 -> 모두 종료 -> DONE / DONE_WITH_ERROR
PAUSED / CANCELED / DONE / DONE_WITH_ERROR + 사용자 작업 추가 -> USER_FOLLOWUP -> HQ (기존 HQ/WORK 세션)
~~~

미확인은 기계적 오류 상태이며 HQ에 한글 요약을 Job당 한 번 전달한다.

## 활성 작업 — 14 RESOURCE Web 역할 + HQ Web 복원

목표:
- HIGH를 제거하고 생성 리소스 전용 RESOURCE 역할로 구조를 교체
- HQ에 ChatGPT Web 실행 대상을 복원
- HQ/RESOURCE Web 작업 목적지를 생존 신호가 아니라 명시적 대화 연결으로 고정
- HQ의 설계 책임과 PAUSE 의미를 강화
- WORK가 ChatGPT Web 생성 파일이 필요한 작업을 RESOURCE에 위임하도록 계약 정리
- RESOURCE 생성 파일 수집/복수 다운로드/저장과 FIFO 대기열을 구현

구현 단위:
1. 역할 열거형/라우터/계약에서 HIGH 제거
2. RESOURCE 사이드카 대기열와 WORK→RESOURCE_QUEUE→HQ 접수 확인 응답 흐름 추가
3. HQ Web/CLI 대상 설정
4. Bridge 역할 연결
5. RESOURCE_TYPE 기계적 분류 + 자연어 전달 + Worker 지정 저장 경로
6. 확장 생성 파일 수집 + 공통 resultFiles 배열
7. Worker 파일 저장 + ResourceRequest 상태
8. 파이프라인/이력/설정
9. HQ END 이후 자동 의미 흐름 차단 + 일반 기계적 대기 게이트
10. PAUSE/END 후 기존 세션 작업 추가 + 고정 크기 이력 입력 UI
11. 테스트/문서

## 보류 항목

- RESOURCE Web 동시 병렬 실행
- 자동 리소스 품질 판정
- 자동 코드/CSS/HTML 연결
- Claude/Muse 실제 CLI 연결
- JobRunner 비정상 종료/재시작 고급 복구
- 비용 기반 자동 정책

## 검증 기준

코드 변경 후 Windows 환경에서 solution test/빌드와 Explorer 실제 Web 왕복 검증을 완료하기 전까지 runtime 완료로 간주하지 않는다.



## 계약 유지 규칙

역할 계약에는 장기 역할 책임, ACTION/GOTO 문법, 전송 형식, 기계적 경계만 둔다. 특정 테스트·도메인·횟수·파일·장애 사례는 계약에 넣지 않고 tests/fixtures/작업 history에 둔다.


## 현재 종료와 후속 작업 정책

- HQ의 ACTION=END는 현재 실행 구간의 의미 작업 종료를 확정한다.
- Worker는 END 이후 현재 실행 구간에서 HQ/WORK/JUDGE 의미 흐름을 자동으로 다시 열지 않는다.
- 남은 RESOURCE 대기열을 포함한 기계적 대기 작업이 있으면 Worker가 대기 상태에서 완료만 기다린다.
- 대기 작업이 모두 끝나면 Worker가 DONE 또는 DONE_WITH_ERROR로 전환한다.
- RESOURCE 완료 이벤트는 HQ를 깨우지 않는다.
- PAUSE, CANCELED, DONE / DONE_WITH_ERROR 이후에도 HQ/WORK 세션과 작업공간은 유지한다.
- 실행 중 사용자 취소는 현재 프로세스를 중단하되 `thread.started`에서 확보한 CLI session ID를 보존한다.
- 사용자가 `작업 추가`를 실행할 때만 USER_FOLLOWUP으로 기존 HQ 세션에서 새 실행 구간을 시작한다.
- `새 작업`을 선택하면 이전 연속 세션과 이력을 명시적으로 초기화한다.

- RESOURCE 성공/실패 completion은 HQ END 전 다음 WORK 입력의 `RESOURCE_RESULT`로 전달하고, RESOURCE 실패를 UNKNOWN으로 승격하지 않는다.
