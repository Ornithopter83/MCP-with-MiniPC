# Agent heartbeat와 상태수집

## 목표

Agent가 heartbeat와 branch, HEAD, dirty, 파일 목록을 Server로 전송한다.

## 세부 작업

### A. Agent 설정과 heartbeat 전송
### B. Git 상태 수집기 구현
### C. FileSystemWatcher debounce와 상태 전송

## 진행

잔여 작업 3개 (A, B, C)

## 변경 금지

- Agent는 Supabase에 직접 접근하지 않으며 Git을 수정하지 않는다.

## 완료 기준

- 등록 프로젝트 상태가 주기적으로 Server에 전달된다.

## 검증 방법

- 모의 Git 저장소와 테스트 Server로 payload 검증

## 결과

- 아직 수행하지 않음
