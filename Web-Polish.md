# Web-Polish — GPT Web 확장 정책

이 문서는 ChatGPT Web과 Worker 사이의 전송 경계만 정의한다.

제1조 (책임)

① 확장은 Worker가 준 요청을 Web에 입력하고 실제 Web 응답을 수집한다.
② 확장은 응답 의미, WorkGraph 또는 작업 성공 여부를 판단하지 않는다.
③ HQ와 RESOURCE는 서로 다른 conversation binding을 사용한다.

제2조 (상관)

① Worker 요청에 KEY가 있으면 같은 KEY가 포함된 응답만 현재 요청 결과로 사용한다.
② KEY 이전의 다른 대화 내용은 현재 결과로 취급하지 않는다.
③ KEY는 의미 명령이 아니라 transport 상관 표식이다.

제3조 (전송)

① composer 입력, 전송 동작과 실제 전송 증거를 구분한다.
② 전송 확인 실패는 transport 오류로 반환한다.
③ transport 오류 때문에 AI 역할 계약을 추가하거나 의미 WorkItem을 변경하지 않는다.

제4조 (응답 수집)

① 현재 요청과 상관된 assistant 응답의 실제 전체 텍스트를 수집한다.
② 스트리밍 중 초기 DOM 조각을 최종 응답으로 고정하지 않는다.
③ 여러 후보가 있으면 현재 상관 응답을 가장 완전하게 나타내는 후보를 사용한다.
④ 일반 HQ 텍스트 응답에서 무관한 페이지 asset을 결과 파일로 취급하지 않는다.

제5조 (RESOURCE)

① RESOURCE 결과 파일은 현재 RESOURCE 요청에서 실제 생성된 candidate만 수집한다.
② 이미 생성된 candidate의 capture가 실패하면 같은 생성 요청을 의미적으로 다시 만들지 않고 capture만 유한 재시도할 수 있다.
③ capture 실패는 RESOURCE 의미 품질 판단이 아니다.

제6조 (완료)

① 응답 텍스트 또는 파일이 실제로 안정된 뒤 Worker에 제출한다.
② Web UI 변화에 대응하기 위한 복구는 transport 내부에 한정한다.
③ 특정 UI 오류 사례를 역할 프롬프트의 영구 규칙으로 추가하지 않는다.