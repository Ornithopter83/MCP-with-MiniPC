# ProjectHub 구현 로드맵

Updated: 2026-09-24

정책 원본: Master-Polish.md

## Current architecture

~~~text
HQ       -> WORK
WORK     -> HQ | JUDGE | RESOURCE
JUDGE    -> WORK
RESOURCE -> WORK
~~~

UNKNOWN은 기계적 오류 상태이며 HQ에 한글 요약을 Job당 한 번 전달한다.

## Active work — 14 RESOURCE Web role + HQ Web restore

목표:
- HIGH를 제거하고 생성 리소스 전용 RESOURCE 역할로 구조를 교체
- HQ에 ChatGPT Web 실행 대상을 복원
- HQ/RESOURCE Web task 목적지를 heartbeat가 아니라 explicit conversation binding으로 고정
- HQ의 설계 책임과 PAUSE 의미를 강화
- WORK가 최종 이미지/사운드 제작을 RESOURCE에 위임하도록 계약 정리
- 초기 실제 transport는 IMAGE 생성/저장까지만 구현

구현 단위:
1. 역할 enum/router/contract에서 HIGH 제거
2. RESOURCE state와 WORK→RESOURCE→WORK 추가
3. HQ Web/CLI target settings
4. Bridge role binding
5. RESOURCE transport JSON + workspace path safety
6. 확장 generated image capture + result payload
7. Worker file save + ResourceRequest status
8. Pipeline/History/Settings
9. 테스트/문서

## Deferred

- SOUND 실제 생성/result transport
- RESOURCE 병렬 queue
- 자동 리소스 품질 판정
- 자동 코드/CSS/HTML 연결
- Claude/Muse 실제 CLI 연결
- JobRunner crash/restart 고급 복구
- 비용 기반 자동 정책

## Validation gate

코드 변경 후 Windows 환경에서 solution test/build와 Explorer 실제 Web 왕복 검증을 완료하기 전까지 runtime 완료로 간주하지 않는다.
