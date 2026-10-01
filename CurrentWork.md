# 현재 작업

갱신일: 2026-10-01

제1조 (CORE)

① 상태는 완료다.
② 최근 완료한 작업은 Core의 장기 책임과 의존 경계를 `Core-Polish.md`로 분리해 현재 정책 원본을 명확히 한 것이다.
③ 다음 작업은 미지정이다.

제2조 (INFRASTRUCTURE)

① 상태는 완료다.
② 최근 완료한 작업은 Infrastructure의 외부 시스템 책임과 보안 경계를 `Infrastructure-Polish.md`로 분리해 현재 정책 원본을 명확히 한 것이다.
③ 다음 작업은 미지정이다.

제3조 (SERVER)

① 상태는 완료다.
② 최근 완료한 작업은 Server의 중앙 HTTP 서비스 책임과 프로젝트 경계를 `Server-Polish.md`로 분리해 현재 정책 원본을 명확히 한 것이다.
③ 다음 작업은 미지정이다.

제4조 (AGENT)

① 상태는 완료다.
② 최근 완료한 작업은 Agent의 개발 PC 상태 수집과 Server 통신 경계를 `Agent-Polish.md`로 분리해 현재 정책 원본을 명확히 한 것이다.
③ 다음 작업은 미지정이다.

제5조 (WORKER)

① 상태는 관찰 중이다.
② HQ는 사용자 목표를 WorkGraph와 checklist로 분해하고, WORK는 배정된 WorkItem의 checklist만 수행해 결과를 보고하는 구조로 정리했다.
③ 서로 독립적인 READY WorkItem은 설정된 동시성 안에서 병렬 실행하고, 여러 CODE_CHANGE 결과의 의미적 결합은 별도 INTEGRATION WorkItem으로 처리한다.
④ WorkGraph patch의 JSON·스키마 오류는 현재 revision과 오류 정보를 HQ에 돌려 형식 수정 응답을 요구하며, 기계 오류 때문에 WORK 의미 작업을 다시 수행하지 않는다.
⑤ WorkItem 격리, CODE_CHANGE provenance, END 종료 게이트와 안전한 fast-forward landing 규칙을 유지한다.
⑥ DONE·DONE_WITH_ERROR 뒤의 추가 작업은 이전 WorkGraph를 이어 붙이지 않고 현재 작업 폴더에서 새 Job으로 시작하며, PAUSE·CANCELED도 작업 폴더가 바뀌었으면 같은 원칙을 적용한다.
⑦ Git 준비 실패 시 사용자 승인으로 .git만 재초기화하고 작업 파일과 .gitignore는 보존하는 단순 복구 경로를 둔다.
⑧ HQ 상태 통지는 WorkItem checklist를 첫 보고에 포함한 뒤 같은 관제 세션의 후속 상태 변화에서는 동일 checklist 전문을 반복하지 않고 새 WORK 보고와 변경 상태를 중심으로 전달한다.
⑨ #0 RESOURCE, #8 MATERIALIZE/COPY, #9 BUILD/PUBLISH를 저장된 AI 맥락에 의존하지 않는 재사용 가능한 단발 고정 슬롯으로 두고 #1~#7은 미배정 예약, 일반 WorkItem은 #10부터 사용한다.
⑩ #8과 #9에만 사용자 프로젝트 루트 쓰기를 허용한다. #8은 완료 결과의 상대경로와 폴더 구조를 그대로 루트에 반영하고, #9는 그 루트의 현재 상태를 기준으로 build/export/publish한다.
⑪ 최종 landing 뒤 사용자 결과에 남은 runtime worktree 파일 경로는 같은 상대 경로의 파일이 사용자 작업 폴더에 실제 존재하는 경우에만 target workspace 경로로 정규화한다.
⑫ 현재는 실제 장기 작업에서 병렬 관제, 통합, 고정 슬롯과 RESOURCE/JUDGE/OBSERVATION sidecar 귀속이 안정적으로 이어지는지 관찰한다.

제6조 (WEB)

① 상태는 관찰 중이다.
② HQ Web은 correlation KEY와 별도 줄의 `[RESPONSE=OK]` 완료 표식을 함께 확인하고, 완료 표식이 보이는 줄까지를 현재 응답 범위로 취급한다.
③ HQ 응답 제한시간 안에 완료 표식이 없으면 직전 답변의 완료 여부를 한 번만 다시 요청하고, 다시 완료 표식을 확인하지 못하면 `WEB_RESPONSE_TIMEOUT`으로 종료한다.
④ RESOURCE는 HQ의 텍스트 완료 표식을 사용하지 않고 현재 요청에서 생성된 파일 candidate의 준비와 capture 결과를 기준으로 완료를 판정한다.
⑤ 관리형 Web 확장 식별자는 0.4.3 / 2026-10-01.2다.
⑥ 현재는 명령 왕복이 정상 수행되는 상태를 유지하면서, 이미지 생성처럼 별도 문자열 응답이 보장되지 않는 RESOURCE의 장기 실행 상관관계를 실제 사용으로 관찰한다.
