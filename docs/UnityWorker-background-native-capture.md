# UnityWorker: 백그라운드 Native Game View 캡처 및 포커스 처리

이 문서는 UnityWorker 브랜치의 실행 정책을 설명한다. HQ의 마일스톤 배정, DESIGN/WORK/HIGH 역할 및 완료 기준은 변경하지 않는다.

## 기본 동작

1. Worker가 이미 연결된 Unity Editor를 사용한다. Native Game View 캡처는 Play Mode의 GameView에 `Repaint()`와 `EditorApplication.QueuePlayerLoopUpdate()`를 요청한 뒤 `ScreenCapture.CaptureScreenshot()`의 **새 파일**을 기다린다. `GameView.Focus()`나 Windows `SetForegroundWindow()`를 호출하여 화면 앞으로 가져오지 않는다.
2. 캡처 시도 중에는 해당 프로젝트 Unity 창의 우발적인 전면 활성화에 대한 자동 복구를 잠시 유예한다. 성공·실패·취소와 관계없이 캡처 종료 시 유예를 해제하고, 필요한 경우 이전 사용자 창으로 포커스를 복구한다. 다른 프로젝트 Unity 및 사용자의 명시적인 창 선택을 변경하지 않는다.
3. Unity가 비활성 상태라 Native 프레임을 기록하지 못하면 **NATIVE_UNVERIFIED**로 남긴다. 포커스를 강제로 빼앗는 두 번째 시도는 하지 않는다. RenderTexture 카메라 캡처는 선택적 보조 증거이며 Overlay UI/입력/실제 Game View의 통과 증거로 승격하지 않는다.
4. Native PNG 존재·크기·SHA 및 CLI 반환값은 파일 획득의 근거일 뿐 화면 품질 승인 증거가 아니다. 실제 영상 내용·UI·입력 검토는 별도로 필요하다.
5. Editor 연결 복구 모니터는 기존 프로젝트 잠금과 Pipeline 상태를 확인한 다음에만 제한적으로 재실행을 시도한다. `unity open` 요청 성공과 실제 재연결 성공은 로그에서 구분한다. GUI의 전면 활성화를 완벽히 방지한다고 주장하지 않는다.

## 확인해야 할 Windows 실환경 검증

- 사용자가 다른 앱을 활성화한 상태에서 Play Mode Game View PNG가 정상 생성·갱신되는지 확인
- Screen Space Overlay UI 및 카메라 없는 화면에서도 Native 캡처가 정상인지, 불가 시 미검증으로 남는지 확인
- Editor가 스스로 앞으로 나온 경우 **캡처 중에는 복구하지 않고**, 종료 시 원래 사용자 창으로 복구하는지 확인
- 사용자가 직접 Unity/다른 앱을 선택한 경우 이를 덮어쓰지 않는지, 다른 프로젝트 Editor가 영향을 받지 않는지 확인
- 검증 로그의 `UNITY NATIVE FOCUS`, `UNITY NATIVE GAME VIEW`, `FOCUS_GUARD` 진단과 실제 PNG 내용을 함께 확인

Windows 대화형 환경의 실제 Unity Editor가 없으면 포커스·Game View 렌더링 검증 완료를 선언하지 않는다.
