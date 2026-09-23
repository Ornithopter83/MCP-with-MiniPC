# GPT-6 Luna 인계 가이드

## 목적

GPT-5.6 Luna로 진행하던 Codex 작업을 GPT-6 Luna가 같은 스레드와 작업 폴더에서 이어받도록 한다. 실행 중인 CLI 프로세스의 모델을 중간에 변경하는 방식이 아니라, 다음 실행부터 동일 session을 `gpt-6-luna`로 resume한다.

## Worker에서 인계하는 방법

1. 실행 중인 작업이 있으면 먼저 `Cancel`로 종료한다. 실행 중인 프로세스의 모델은 중간 변경하지 않는다.
2. Codex 선택 콤보박스에서 기존 작업 스레드를 선택한다. 신규 스레드가 아니라 기존 스레드를 선택해야 session ID가 유지된다.
3. 모델 콤보박스에서 `GPT-6 Luna`를 선택한다. Worker의 초기 기본값도 `GPT-6 Luna`다.
4. 같은 작업 폴더와 필요한 후속 지시를 확인한 뒤 `Run Task`를 실행한다.
5. Worker는 저장된 session ID가 있으면 `codex exec resume ... --model gpt-6-luna` 형태로 실행하므로 기존 대화 문맥을 이어간다.

## 신규 스레드에서 시작할 때

- `GPT-6 Luna`를 선택하고 신규 스레드를 실행한다.
- CLI가 반환한 session ID는 Worker가 선택 상태에 저장한다.
- 다음 실행에서 해당 스레드를 선택하면 GPT-6 Luna로 계속 진행할 수 있다.

## 인계 시 보존되는 것

- Codex session ID와 대화 문맥
- 선택된 프로젝트 및 작업 폴더
- Worker의 JEV footer와 Git 기준 정보
- 작업 transcript 및 누적 토큰 기록

## 주의

- GPT-5.6 Luna의 이전 reasoning 상태를 GPT-6 Luna가 완전히 동일하게 재현한다고 보장하지 않는다. 모델은 같은 session 문맥을 받지만 모델별 reasoning 동작은 다르다.
- 인계 직후에는 `TASK START`와 `CLI STATUS`의 model 항목에서 실제 `gpt-6-luna` 사용 여부를 확인한다.
- 계정이나 CLI 버전에서 모델이 제공되지 않으면 exit code와 stderr를 확인하고 사용 가능한 모델로 선택한다.

## 공식 모델 비교

- GPT-5.6 Luna: 입력 $0.20/1M, 출력 $1.20/1M, 1.05M context, 최대 출력 128K
- GPT-6 Luna: 입력 $0.10/1M, 출력 $0.50/1M, 1.05M context, 최대 출력 128K
- 두 모델 모두 공식 문서상 `medium` reasoning이 기본값이다.

공식 문서:
- https://developers.openai.com/api/docs/models/gpt-5.6-luna
- https://developers.openai.com/api/docs/models/gpt-6-luna
