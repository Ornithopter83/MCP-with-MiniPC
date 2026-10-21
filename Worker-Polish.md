# Worker-Polish — ProjectHub Worker 정책

이 문서는 ProjectWorker의 역할 책임과 마일스톤 실행 경계를 정의한다. Master-Polish.md가 최상위 정책이며 역할별 전송 형식은 Contracts/*에서 규정한다.

제1조 (역할)
① 화면과 실행 경로는 HQ → SuperVisor → WORK → RESOURCE → HIGH 순으로 구성한다.
② HQ는 설계·목표·마일스톤·검토 해석과 다음 WORK/PAUSE/END를 결정한다. 구현용 WORK를 직접 배정하지 않는다.
③ SuperVisor(#1)는 마일스톤 첫 실행자이며 구현을 책임진다. 필요한 파일 제작·조사·테스트를 임시 WORK에 위임하고 결과를 통합한다.
④ WORK는 SuperVisor의 delegate_work(task, paths)로만 생성되는 단발성 SubAgent다. HQ WORKITEM과 영구 체크포인트는 발급하지 않는다.
⑤ RESOURCE(#0)는 GPT Web 이미지 생성 전용 비차단 sidecar이며 다른 역할은 완료를 기다리지 않는다.
⑥ HIGH는 SuperVisor 통합 결과를 심층 검토·검증한다. 수정 요구는 SuperVisor와 HQ에 보고하고 직접 구현하지 않는다.

제2조 (세션과 위임)
① SuperVisor와 각 WORK 호출은 매번 새 Provider 세션(SessionId=null)을 사용한다. 이전 호출이나 마일스톤 세션을 재사용하지 않는다.
② WORK는 부모 토큰을 받지 않으며 재위임할 수 없다. 각 호출은 고유 실행 ID·허용 경로·결과·오류·토큰 기록을 가진다.
③ SuperVisor는 분할·위임 횟수·재작업·모델 활용을 자율 판단하되 실행 작업은 WORK에 먼저 위임한다. Worker는 실제 호출·성공·실패·범위 위반과 세션 격리를 기계적으로 검증한다.
④ 겹치지 않는 임시 WORK는 병렬 실행하고 겹치는 쓰기 경로는 거부한다.
⑤ 첫 SuperVisor 실행은 읽기 전용으로 WORK 위임과 통합을 수행한다. WORK 실행 실패가 확인되고 경로 위반·취소 등 차단 사유가 없을 때만 Worker가 별도 신규 SuperVisor 세션에 직접 복구 쓰기 권한을 한 번 부여한다. 위임 미시도나 단순 완료 주장으로 직접 복구하지 않는다. WORK는 Git commit/push를 수행하지 않으며 Git 최종화는 Worker 책임이다.

제3조 (프로젝트 경계)
① 선택한 로컬 폴더가 프로젝트 루트다. 일반 프로젝트 상대경로를 허용하되 루트 외부·상위 경로·보호 메타데이터를 거부한다.
② 기존 사용자 파일과 dirty 상태를 보존한다. 실제 변경 및 오류를 추적하고 자연어 보고를 단독 성공 증거로 사용하지 않는다.
③ 범위 이탈, 권한/환경 실패, 취소·타임아웃은 기계적으로 기록하고 HQ에 보고한다.

제4조 (마일스톤)
① HQ 지시 → 읽기 전용 SuperVisor 새 세션 → 임시 WORK 우선 배정·통합 → (실제 WORK 실패 시 제한된 직접 복구 새 세션) → BUILD → HIGH 검토 → Git 최종화 → HQ 보고 순으로 진행한다.
② RESOURCE는 독립적으로 생성·검증하고 차후 Git에 반영한다. READ_ONLY_NO_FILE_CHANGES는 쓰기·Git 반영을 생략한다.
③ HIGH의 수정 요구는 다음 SuperVisor 실행으로 연결한다. 복구를 위한 암묵적인 세션 재개는 하지 않는다.
④ Worker는 Git preflight, 빌드·commit/push, 결과·오류·Provider 토큰을 관리한다.
⑤ 입력·전송·보고·상태·로그는 UTF-8을 기본으로 한다.
