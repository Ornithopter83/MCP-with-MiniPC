# GPTWeb-Hub UI Preview

이미지 시안에 맞춘 Chrome Manifest V3 content-script UI 목업이다.

## 설치

1. Chrome에서 `chrome://extensions`를 연다.
2. 개발자 모드를 켠다.
3. `압축해제된 확장 프로그램을 로드`를 누른다.
4. 이 폴더를 선택한다.
5. `https://chatgpt.com/`을 새로고침한다.

## 확인 방법

- 우측 패널의 톱니바퀴를 누르면 `IDLE` → `WORKER_TO_WEB` → `WEB_TO_WORKER` → `FINISHED` 상태를 순환한다.
- 닫기 버튼은 패널을 숨기며, 원형 버튼으로 다시 표시할 수 있다.
- Worker의 `http://127.0.0.1:43821` loopback bridge를 1.5초 간격으로 polling해 연결 상태·프로젝트·pending task를 표시한다. ChatGPT DOM 자동입력과 실제 Web 응답 제출은 아직 후속 범위다.
