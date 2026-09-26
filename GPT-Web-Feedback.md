# GPT Web 피드백 — 예약 WorkItem #0/#1

갱신일: 2026-09-26
기준 정책: Master-Polish.md

## 확정 정책

- WorkItem #0~#9는 시스템 예약 번호다. HQ는 일반 작업을 #10부터 배정한다.
- WorkItem #0은 리소스 전용이다. RESOURCE 요청과 완료 결과는 반드시 #0을 통과하며 다른 WorkItem은 RESOURCE를 직접 호출할 수 없다.
- WorkItem #1은 이미지 가공 전용이다. 스프라이트 분할 등 기존 이미지 가공만 담당한다.
- 메시지 및 작업 이력에서는 #0을 `작업 (#0, 리소스)`, #1을 `작업 (#1, 이미지 가공)`, 일반 작업을 `작업 (#N)`으로 표시한다.

## 구현 변경

- WorkGraph/GraphPatch에서 #0~#9를 일반 ADD 대상에서 제외하고 일반 WorkItem 번호를 #10부터 사용한다.
- RESOURCE 라우터는 #0의 요청만 수락하고 RESOURCE 완료도 #0으로 반환한다.
- #0에는 리소스 외 일반 구현 작업을 배정하지 않고, #1에는 새 리소스 생성을 배정하지 않는다.
- 기존 ResourceSidecarQueue의 FIFO 1건 실행과 Web transport 구조는 유지한다.
- 현재 createdOrder 기반 History 번호 계산은 예약 번호 정책과 맞도록 수정한다.

## 범위

이번 변경에 RESOURCE 의미 중복 판정, JEV preflight, 새 AI 역할 추가는 포함하지 않는다.
