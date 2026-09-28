# 프로젝트 상태 API

> 문서 상태: **HISTORICAL IMPLEMENTATION RECORD**
> 이 문서는 작성 당시의 목표·구현·검증 기록이다. 현재 정책·계약·런타임 상태의 원본으로 사용하지 않는다.
> 현재 기준은 `Master-Polish.md`, 프로젝트별 `*-Polish.md`, 전용 계약을 따른다.


## 목표

ProjectService를 통해 프로젝트 목록과 상세 상태를 조회한다.

## 세부 작업

### A. ProjectService 핵심 조회 계약
### B. `GET /api/projects` 구현
### C. `GET /api/projects/{projectId}` 구현

## 진행

잔여 작업 3개 (A, B, C)

## 변경 금지

- API는 상태 조회 중심이며 외부 시스템을 변경하지 않는다.

## 완료 기준

- 프로젝트별 상태·활성 workstation·변경 수를 조회할 수 있다.

## 검증 방법

- WebApplicationFactory 또는 HTTP 통합 테스트

## 결과

- 아직 수행하지 않음
