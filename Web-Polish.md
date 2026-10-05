# Web-Polish — GPT Web 확장 정책

이 문서는 ChatGPT Web과 Worker 사이의 전송 경계만 정의한다.

제1조 (책임)

① 확장은 Worker가 준 요청을 Web에 입력하고 실제 Web 응답을 수집한다.
② 확장은 응답 의미, WorkGraph 또는 작업 성공 여부를 판단하지 않는다.
③ HQ와 RESOURCE는 서로 다른 conversation binding을 사용한다.
④ Web 확장은 HQ의 ACTION 의미나 RESOURCE의 프로젝트 반영 여부를 판단하지 않는다.

제2조 (상관)

① Worker 요청에 KEY가 있으면 같은 KEY가 포함된 응답만 현재 요청 결과로 사용한다.
② KEY 이전의 다른 대화 내용은 현재 결과로 취급하지 않는다.
③ KEY는 의미 명령이 아니라 transport 상관 표식이다.

제3조 (전송)

① composer 입력, 전송 동작과 실제 전송 증거를 구분한다.
② 전송 확인 실패는 transport 오류로 반환한다.
③ transport 오류 때문에 AI 역할 계약을 추가하거나 의미 WorkItem을 변경하지 않는다.

제4조 (응답 수집)

① 현재 요청과 상관된 assistant 응답의 실제 텍스트를 수집한다.
② HQ 응답은 현재 KEY 뒤에서 별도 줄의 `[RESPONSE=OK]`가 보일 때까지 기다리고, 그 줄까지를 현재 응답 범위로 취급한다.
③ HQ 응답은 streaming 상태, 본문 길이 안정화 또는 초기 DOM 조각으로 완료를 추정하지 않는다.
④ `[RESPONSE=OK]` 뒤에 페이지 UI나 다른 텍스트가 보이더라도 현재 HQ 응답 범위에 포함하지 않는다.
⑤ 일반 HQ 텍스트 응답에서 무관한 페이지 asset을 결과 파일로 취급하지 않는다.

제5조 (RESOURCE)

① RESOURCE 결과 파일은 현재 RESOURCE 요청에서 실제 생성된 candidate만 수집한다.
② 수집된 RESOURCE 파일은 Worker에 전달하고 Worker가 프로젝트 루트의 temp/Resource 임시영역에 저장한다. Web 확장은 최종 프로젝트 경로를 직접 수정하지 않는다.
③ 이미 생성된 candidate의 capture가 실패하면 같은 생성 요청을 의미적으로 다시 만들지 않고 capture만 유한 재시도할 수 있다.
④ capture 실패는 RESOURCE 의미 품질 판단이 아니다.
⑤ RESOURCE에는 HQ용 `[RESPONSE=OK]` 텍스트 완료 표식을 요구하지 않는다.
⑥ 이미지 생성처럼 별도 문자열 응답이 보장되지 않는 경우에도 텍스트 유무만으로 RESOURCE 완료나 실패를 판정하지 않는다.

제6조 (완료)

① HQ 텍스트 응답은 현재 KEY와 `[RESPONSE=OK]` 완료 줄이 모두 확인된 뒤 Worker에 제출한다.
② HQ 응답 제한시간 안에 완료 줄을 확인하지 못하면 직전 답변 완료 여부를 한 번만 다시 요청하고, 그 요청에서도 완료 줄을 확인하지 못하면 Web transport 오류로 종료한다.
③ RESOURCE 완료는 생성 파일 준비와 capture 상태를 기준으로 별도 처리한다.
④ Web UI 변화에 대응하기 위한 복구는 transport 내부에 한정한다.
⑤ 특정 UI 오류 사례를 역할 프롬프트의 영구 규칙으로 추가하지 않는다.
