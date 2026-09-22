# GPTWeb-Hub UI Preview

이미지 시안에 맞춘 Chrome Manifest V3 content-script UI 목업이다.

## 설치

1. Chrome에서 `chrome://extensions`를 연다.
2. 개발자 모드를 켠다.
3. `압축해제된 확장 프로그램을 로드`를 누른다.
4. 이 폴더를 선택한다.
5. `https://chatgpt.com/`을 새로고침한다.

## 확인 방법

- 우측 패널의 톱니바퀴를 누르면 Worker bridge 연결 설정을 여는 Settings 창이 표시된다. Host/Port/BasePath를 저장할 수 있으며 기본값은 127.0.0.1:43821/bridge다.
- 닫기 버튼은 패널을 숨기며, 원형 버튼으로 다시 표시할 수 있다.
- Worker bridge 설정은 chrome.storage.local에 저장되며 1.5초 간격 polling에 즉시 적용된다. 기본적으로 loopback 주소만 허용하고 Test Connection으로 /status 응답을 확인한다. 현재 대화의 입력·파일 전송과 assistant 응답 표시를 지원한다.


## Conversation binding

- ChatGPT URL의 conversation ID를 현재 대화 식별자로 사용한다.
- 현재 대화 제목은 GPT Web 행에 표시한다. 연결되지 않은 대화에서는 GPT Web 행의 연결 버튼으로 현재 Worker 프로젝트를 binding한다.
- Worker 행에는 bridge가 보고한 실제 Git 저장소명을 표시한다. 연결된 대화는 새로고침·대화 이동 후 Worker에 저장된 binding을 조회해 자동 복원한다. 대화 이동 중 늦게 도착한 이전 polling 응답은 현재 대화 상태에 적용하지 않으며, 연결 실패 시 원인을 패널에 표시한다.
- task 조회도 conversationId로 필터링해 다른 대화의 작업을 표시하지 않는다.


## Request and response

- CURRENT REQUEST가 대기 상태이고 현재 ChatGPT 대화가 식별된 경우에만 입력창, 전송 버튼, 파일 드롭 영역이 활성화된다.
- 드롭한 파일은 패널에 파일명만 표시하고, 전송 시 ChatGPT 입력창에 함께 첨부한다.
- ChatGPT의 최신 assistant 메시지는 RESULT MESSAGE 영역에 자동 갱신한다.
