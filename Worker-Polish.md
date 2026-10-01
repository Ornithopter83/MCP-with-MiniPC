# Worker-Polish — ProjectHub Worker 정책

이 문서는 Worker의 장기 책임만 정의한다. 실제 출력 문법은 전용 라우팅 계약을 사용한다.

제1조 (역할)

① HQ는 사용자 목표 해석, WorkGraph 관제와 CONTINUE·PAUSE·END 판단을 담당한다.
② WORK는 현재 WorkItem을 수행하고 결과를 HQ에 보고한다.
③ RESOURCE는 WorkItem #0의 생성 리소스를 Web으로 요청하고 수집한다.
④ Worker는 의미 판단 대신 상태, 전송, 프로세스, Git과 기계 작업을 관리한다.

제2조 (보고)

① WORK 보고는 Worker가 의미적으로 요약하거나 다시 작성하지 않는다.
② Worker는 workItemId, state, resultRef 등 필요한 최소 기계 메타데이터와 WORK 보고 본문을 HQ에 전달한다.
③ Commit Manifest 전문이나 코드 내용을 HQ 프롬프트에 자동 주입하지 않는다.
④ 오류가 발생해도 역할 계약에 해당 오류 전용 규칙을 추가하지 않는다.

제3조 (WorkGraph)

① WorkGraph는 Worker가 기계 상태로 보존한다.
② HQ가 반환한 WORK_GRAPH_PATCH만 WorkGraph 의미 변경에 사용한다.
③ Worker는 revision, JSON 구조, dependency와 상태 전이처럼 기계적으로 확인 가능한 조건만 검사한다.
④ 서로 독립적인 READY WorkItem은 설정된 동시성 범위에서 병렬 실행할 수 있다.

제4조 (WORK 실행)

① WORK에는 현재 WorkItem 목표와 필요한 현재 사실만 제공한다.
② 역할 계약 전문은 새 AI 세션의 첫 호출에만 주입한다.
③ 후속 호출에는 현재 입력과 필요한 기계 사실만 전달한다.
④ 선행 결과는 필요한 최소 메타데이터와 보고를 전달하고 대용량 manifest 전문을 자동 주입하지 않는다.

제5조 (RESOURCE)

① WorkItem #0만 RESOURCE를 요청할 수 있다.
② RESOURCE Web 전송·수집 실패는 의미 작업 실패와 구분한다.
③ RESOURCE 실패를 일반 WORK의 다른 생성 경로로 자동 우회하지 않는다.

제6조 (Web)

① HQ Web과 RESOURCE Web은 별도 역할 슬롯으로 관리한다.
② KEY는 요청과 응답을 연결하는 기계 표식으로 사용한다.
③ Web 확장은 전송, DOM 관측과 결과 수집만 담당하고 작업 의미를 해석하지 않는다.

제7조 (Git과 결과)

① 병렬 WORK의 작업공간 준비, checkpoint와 최종 반영은 Worker가 기계적으로 관리할 수 있다.
② CODE_CHANGE의 Git 정보와 Commit Manifest는 내부 기계 사실로 보존할 수 있다.
③ 사용자 작업 폴더의 위험한 병합이나 강제 reset을 자동 수행하지 않는다.

제8조 (프로세스)

① Worker가 시작한 외부 프로세스는 Worker의 기계적 수명 관리 아래 둔다.
② 정상 종료와 취소에서 소유 프로세스를 정리하며 AI 응답에 프로세스 정리를 의존하지 않는다.
③ 프로세스 수명 문제가 발생하면 계약을 추가하지 않고 실행 계층을 수정한다.

제9조 (UI와 설정)

① UI는 HQ, WORK, RESOURCE와 실제 실행 상태를 표시한다.
② 설정은 실제 사용 중인 역할과 실행 옵션만 노출한다.
③ 사용하지 않는 역할이나 transport의 설정 UI를 유지하지 않는다.