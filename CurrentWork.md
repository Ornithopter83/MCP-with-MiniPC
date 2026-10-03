# 현재 작업

갱신일: 2026-10-03

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
⑦ WorkGraph 새 작업은 선택한 작업 폴더 자체가 Git 저장소 root이고 `origin`이 존재해야 한다. 기존 HEAD가 있으면 현재 branch와 로컬 HEAD가 대응 원격 branch HEAD와 일치하는 clean 상태를 요구한다. 최초 commit이 없는 unborn 저장소는 `.gitignore` 기준 현재 파일로 Worker가 `projecthub/*` 초기 baseline을 만들어 원격에 게시한 뒤 그 SHA에서 시작하고, 현재 로컬 branch도 파일 변경 없이 같은 baseline commit에 연결한다. 기본 branch 원격 push는 수행하지 않는다. 상위 폴더의 Git 저장소를 자동 채택하지 않고, 기존 HEAD의 불일치·dirty·원격 부재 상태를 Worker가 임의 merge·reset·재초기화로 우회하지 않는다. 하네스 없음 Direct Work는 이 조건과 별개다.
⑧ HQ 상태 통지는 WorkItem checklist를 첫 보고에 포함한 뒤 같은 관제 세션의 후속 상태 변화에서는 동일 checklist 전문을 반복하지 않고 새 WORK 보고와 변경 상태를 중심으로 전달한다.
⑨ NORMAL WORK는 원격 기준 disposable clone과 `projecthub/*` checkpoint 정책을 유지한다. 하네스 없음 Direct Work는 이 관제 경로와 분리되어 사용자가 선택한 작업 폴더에서 직접 실행한다.
⑩ 현재는 실제 장기 작업에서 병렬 관제, 통합, RESOURCE/JUDGE/OBSERVATION sidecar 귀속이 안정적으로 이어지는지 관찰한다.
⑪ HIGH는 Job별 사용자 one-shot 허용이 있을 때 HQ가 직접 호출하고 결과를 같은 HQ 관제 루프로 반환하는 구조로 복원한다. HIGH는 danger-full-access 실행을 사용하되 일반 WorkItem·RESOURCE 대체로 사용하지 않는다.
⑫ RESOURCE는 IMAGE 전용으로 제한하며 AUDIO·VIDEO·DOCUMENT·FILE 요청은 Web 전송 전에 기계적으로 거부한다.
⑬ Integration 준비는 로컬 branch 이름에 대응하는 origin branch를 추론하지 않고, fetch된 origin ref 중 현재 로컬 HEAD와 정확히 같은 commit을 가리키는 ref가 있는지 검증한다.
⑭ WORK의 Git 환경은 terminal/GCM 상호작용만 비활성화하고 HTTPS/SSH protocol 자체를 막지 않는다. `git ls-remote` 같은 read-only 원격 사실 확인은 허용하되, WORK 실행 중 현재 worktree의 Git metadata는 계속 분리·복원하며 clone·stage·commit·push와 원격 branch 변경은 역할 계약상 금지한다. 원격 Git 상태 변경이 필요한 인프라 복구는 사용자 one-shot 허용이 있는 HIGH 경로를 사용한다.
⑮ managed CLI 역할의 Codex 내부 output/schema 임시파일은 작업 폴더의 `.projecthub/runtime/temp` 아래 role/work 전용 경로를 우선 사용한다. PAUSE compact와 DONE runtime reset이 이 경로를 정리하며, RESOURCE staging은 IMAGE만 허용한다.
⑯ WorkGraph 기계 상태의 `kind`는 NORMAL/INTEGRATION 실행 방식을 나타내며 #0/#9 고정 임무는 별도 `slot=RESOURCE` / `slot=BUILD_PUBLISH`로 표시한다.

제6조 (WEB)

① 상태는 관찰 중이다.
② HQ Web은 correlation KEY와 별도 줄의 `[RESPONSE=OK]` 완료 표식을 함께 확인하고, 완료 표식이 보이는 줄까지를 현재 응답 범위로 취급한다.
③ HQ 응답 제한시간 안에 완료 표식이 없으면 직전 답변의 완료 여부를 한 번만 다시 요청하고, 다시 완료 표식을 확인하지 못하면 `WEB_RESPONSE_TIMEOUT`으로 종료한다.
④ RESOURCE는 HQ의 텍스트 완료 표식을 사용하지 않고 현재 요청에서 생성된 파일 candidate의 준비와 capture 결과를 기준으로 완료를 판정한다.
⑤ 관리형 Web 확장 식별자는 0.4.4 / 2026-10-03.1이다.
⑥ RESOURCE는 명시적 Send click 뒤 composer prompt가 안정적으로 소비되면 user/assistant/generation DOM이 즉시 보이지 않아도 전송 수락으로 판정하고 생성 파일 감시 단계로 전환한다.
⑦ 현재는 명령 왕복이 정상 수행되는 상태를 유지하면서, 이미지 생성처럼 별도 문자열 응답이 보장되지 않는 RESOURCE의 장기 실행 상관관계를 실제 사용으로 관찰한다.
