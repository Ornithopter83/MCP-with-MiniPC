# 15 비동기 기계 작업과 계측

갱신일: 2026-09-25

정책 원본은 Master-Polish.md다.

## 목표

WORK가 장시간 프로그램 실행·테스트·계측을 시작한 뒤 AI 호출을 붙잡아 두지 않고 다른 작업을 진행할 수 있게 한다. 완료 시점과 대기, 시간 초과, 결과 전달은 Worker가 기계적으로 관리한다. 새 AI 역할이나 GOTO 목적지는 추가하지 않는다.

## 공통 기계 작업

`MechanicalWorkRegistry`가 RESOURCE와 OBSERVATION의 outstanding 상태를 함께 관리한다.

완료 모드:
- `FINALIZE_ONLY`: 현재 의미 흐름을 막지 않는다. HQ END 뒤 남아 있으면 Worker가 AI 호출 없이 완료만 기다린다.
- `WORK_RESULT_REQUIRED`: 현재 WORK 응답의 의미 라우팅을 보류한다. 완료까지 Worker가 AI 호출 없이 기다린 뒤 같은 WORK 세션에 결과를 반환한다.

## OBSERVATION 요청

WORK 프롬프트 헤더에 현재 Job 전용 요청 폴더를 제공한다.

`.projecthub/mechanical/<jobId>/requests`

WORK는 요청 JSON을 임시 파일에 완성한 다음 같은 폴더의 `.json` 파일로 이름을 바꿔 게시한다.

기계 필드:
- `kind=OBSERVATION`
- `id`
- `command`
- `arguments[]`
- `workingDirectory`
- `timeoutSeconds`
- `resultPaths[]`
- `environment`
- `completionMode`

Worker는 명령을 별도 프로세스로 실행하고 stdout/stderr, 종료 코드, 시간 초과와 명시된 결과 경로를 수집한다. 결과는 `.projecthub/mechanical/<jobId>/results/<id>/` 아래에 기록한다.

## 안전한 흐름

~~~text
WORK 실행 중
  -> OBSERVATION 요청 게시
  -> Worker 별도 프로세스 실행
  -> WORK는 현재 호출 안에서 다른 작업 가능

WORK 응답 시점
  -> WORK_RESULT_REQUIRED 없음: 기존 GOTO 처리
  -> WORK_RESULT_REQUIRED 있음:
       Worker WAIT
       AI 호출 없음
       완료
       OBSERVATION_RESULT -> 같은 WORK 세션
       새 WORK 응답
       기존 의미 라우팅 계속
~~~

FINALIZE_ONLY는 WORK 응답을 막지 않는다.

~~~text
HQ ACTION=END
  -> 공통 MechanicalWork outstanding 확인
  -> 남아 있으면 AI 호출 없이 대기
  -> 전부 완료
  -> DONE / DONE_WITH_ERROR
~~~

## Codex 실행 권한

WORK의 `workspace-write` 실행에는 ProjectHub 실행 파일 디렉터리를 `--add-dir`로 추가한다. 따라서 실행 파일 디렉터리 아래의 공용 Sounds, Tools, SharedAssets 같은 형제 폴더를 현재 프로젝트와 함께 사용할 수 있다. HQ read-only 실행에는 추가 쓰기 루트를 부여하지 않는다.

## 검증

- MechanicalWorkRegistry가 FINALIZE_ONLY와 WORK_RESULT_REQUIRED를 독립 집계한다.
- WORK_RESULT_REQUIRED 완료까지 wait가 해제되지 않는다.
- RESOURCE는 공통 레지스트리에 FINALIZE_ONLY로 등록된다.
- OBSERVATION 요청 스키마와 안전한 ID를 검증한다.
- WORK 프롬프트에 Job 전용 요청 폴더가 포함된다.
- OBSERVATION은 새 GOTO 목적지를 만들지 않는다.
- Codex workspace-write만 ProjectHub 실행 루트를 추가 writable directory로 받는다.
- HQ END 최종 대기는 RESOURCE와 OBSERVATION 전체 outstanding을 사용한다.
