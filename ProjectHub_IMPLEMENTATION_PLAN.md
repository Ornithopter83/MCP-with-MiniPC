# ProjectHub 구현 로드맵

Updated: 2026-09-24

정책 원본: Master-Polish.md

## Current architecture

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE_QUEUE
JUDGE    -> WORK
RESOURCE_QUEUE 접수 -> HQ (RESOURCE_QUEUED)
RESOURCE_QUEUE 실행 -> RESOURCE Web (FIFO 1건) -> 완료 알림 queue
완료 알림 -> 다음 WORK 입력 또는 HQ END finalization
~~~

UNKNOWN은 기계적 오류 상태이며 HQ에 한글 요약을 Job당 한 번 전달한다.

## Active work — 14 RESOURCE Web role + HQ Web restore

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
9. 테스트/문서

## Deferred

- SOUND 실제 생성/result transport
- RESOURCE Web 동시 병렬 실행
- 자동 리소스 품질 판정
- 자동 코드/CSS/HTML 연결
- Claude/Muse 실제 CLI 연결
- JobRunner crash/restart 고급 복구
- 비용 기반 자동 정책

## Validation gate

코드 변경 후 Windows 환경에서 solution test/build와 Explorer 실제 Web 왕복 검증을 완료하기 전까지 runtime 완료로 간주하지 않는다.


## Orchestration ownership note

RESOURCE 반복 목표 수는 HQ가 관리한다. Worker는 RESOURCE queue의 실제 outstanding/queued 상태만 관리하며 사용자 의도에서 총 횟수나 남은 횟수를 계산하지 않는다. 한 RESOURCE 접수 뒤 RESOURCE_QUEUED는 HQ로 전달되어 다음 WORK 지시 여부를 HQ가 결정한다.
