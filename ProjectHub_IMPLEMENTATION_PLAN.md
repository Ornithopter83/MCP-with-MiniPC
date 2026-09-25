# ProjectHub 구현 로드맵

Updated: 2026-09-24

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
~~~

UNKNOWN은 기계적 오류 상태이며 HQ에 한글 요약을 Job당 한 번 전달한다.

## 활성 작업 — 14 RESOURCE Web 역할 + HQ Web 복원

목표:
- HIGH를 제거하고 생성 리소스 전용 RESOURCE 역할로 구조를 교체
- HQ에 ChatGPT Web 실행 대상을 복원
- HQ/RESOURCE Web task 목적지를 heartbeat가 아니라 explicit conversation binding으로 고정
- HQ의 설계 책임과 PAUSE 의미를 강화
- WORK가 최종 이미지 제작을 RESOURCE에 위임하도록 계약 정리
- RESOURCE IMAGE 생성/복수 다운로드/저장과 FIFO queue를 구현

구현 단위:
1. 역할 enum/router/contract에서 HIGH 제거
2. RESOURCE sidecar queue와 WORK→RESOURCE_QUEUE→HQ 접수 ack 흐름 추가
3. HQ Web/CLI target settings
4. Bridge role binding
5. RESOURCE natural-language forwarding + Worker-assigned save path
6. 확장 generated multi-image capture + result payload array
7. Worker file save + ResourceRequest status
8. Pipeline/History/Settings
9. HQ END 이후 의미 흐름 차단 + 일반 기계적 대기 게이트
10. 테스트/문서

## 보류 항목

- SOUND 실제 생성/result transport
- RESOURCE Web 동시 병렬 실행
- 자동 리소스 품질 판정
- 자동 코드/CSS/HTML 연결
- Claude/Muse 실제 CLI 연결
- JobRunner crash/restart 고급 복구
- 비용 기반 자동 정책

## 검증 기준

코드 변경 후 Windows 환경에서 solution test/build와 Explorer 실제 Web 왕복 검증을 완료하기 전까지 runtime 완료로 간주하지 않는다.



## 계약 유지 규칙

역할 contract에는 장기 역할 책임, ACTION/GOTO 문법, transport 형식, 기계적 경계만 둔다. 특정 테스트·도메인·횟수·파일·장애 사례는 contract에 넣지 않고 tests/fixtures/task history에 둔다.


## 현재 종료 정책

- HQ의 ACTION=END는 의미 작업 종료를 확정한다.
- Worker는 END 이후 HQ/WORK/JUDGE 의미 흐름을 다시 열지 않는다.
- 남은 RESOURCE queue를 포함한 기계적 대기 작업이 있으면 Worker가 대기 상태에서 완료만 기다린다.
- 대기 작업이 모두 끝나면 Worker가 DONE 또는 DONE_WITH_ERROR로 전환한다.
- RESOURCE 완료 이벤트는 HQ를 깨우지 않는다.
