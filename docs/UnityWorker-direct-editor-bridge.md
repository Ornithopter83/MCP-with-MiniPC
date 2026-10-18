# UnityWorker — 기존 Unity Editor 직접 실행 브리지

## 목적

Unity 공식 `InitializeOnLoad`, `EditorApplication.update`, `EditorApplication.ExecuteMenuItem`, Reflection, `BuildReport` API로 만든 **자체 Editor 확장**이다. Unity 공식 기능만 사용하는 구현이지만, 브리지 자체는 Unity 기본 제공 제품이 아니다. `com.unity.pipeline` CLI 명령 카탈로그가 비어 있어도 독립적으로 동작할 수 있도록 구성했다.

## 설치 및 실행

1. 부모 전문 역할(HIGH/DESIGN/ASSET MANAGER)에서 MCP `worker_command`에 `action=editor_bridge_install`을 호출한다. Worker는 대상 Unity 프로젝트의 `Assets/Editor/ProjectHub/ProjectHubEditorBridge.cs`에 내장된 Editor 스크립트를 **최초 한 번** 작성한다. 기존 파일의 내용이 다르면 덮어쓰지 않고 `SOURCE_CONFLICT`를 반환한다.
2. 기존 Unity Editor가 해당 C# 파일을 **자동 Import·컴파일**하고 도메인을 다시 로드해야 한다. 사용자의 Unity Auto Refresh 설정이나 컴파일 오류에 따라 백그라운드 갱신이 늦어질 수 있다. Worker는 Editor 창을 활성화하거나 새 Editor를 실행하지 않는다.
3. `action=editor_bridge_ping`에서 실제 Editor 메인 스레드의 `EDITOR_RESPONDED`가 반환돼야 연결 성공이다. 단순 CLI `ready`는 이 증거를 대체하지 못한다.
4. WORK 또는 전문 역할은 `action=editor_bridge_invoke`, `type=QuizStandaloneBuild`, `method=BuildQuizStandalone`, `expected_result=Succeeded`로 기존에 컴파일된 **public static 인수 없는 메서드**를 요청할 수 있다. `BuildReport`를 반환하는 메서드는 `BuildResult`와 오류 수·출력 경로를 함께 반환한다.
5. Unity 메뉴 항목 실행은 `action=editor_bridge_menu`, `menu_path=<등록된 메뉴 경로>`, `expected_result=True`로 요청한다. 메뉴 자체가 창을 열 수 있으므로 포커스 비점유가 필요한 경우 창을 여는 메뉴는 선택하지 않는다.

메서드 인수가 필요하거나 새로운 실행 동작이면, 역할 AI가 **위임받은 좁은 소스 경로 안에 Editor용 public static 인수 없는 래퍼**를 작성하고 실제 Editor에서 컴파일된 뒤 해당 래퍼를 호출한다. `editor_bridge_invoke`는 임의 C# 문자열을 해석하거나 소스 코드를 몰래 컴파일하는 기능이 아니다.

## 실행권한과 격리

- 설치는 **기존 Windows 사용자 권한만** 사용한다. 관리자 권한 상승, Unity 인증·프로젝트 잠금 우회 및 Editor 강제 실행은 하지 않는다.
- 요청·결과는 `%LOCALAPPDATA%/ProjectHub/UnityWorker/EditorBridge/<프로젝트 해시>` 아래 파일을 원자적으로 교체해 통신한다. 접근 경계는 로컬 사용자 프로필의 일반 파일 권한이며, **같은 Windows 사용자 계정 내 다른 프로세스로부터의 별도 강한 인증 경계는 아니다**. 별도의 공개 네트워크 포트는 만들지 않는다.
- 직접 WORK가 스크립트 설치를 요청하려면 `Assets/Editor/ProjectHub/`와 같이 브리지 소스 및 Unity 자동 생성 `.meta`를 포함한 **좁은 경로**를 명시적으로 소유해야 한다. 전문 역할의 자체 설치는 해당 역할의 경로·변경 결과 검증에 포함한다.
- WORK는 수행 수단을 폭넓게 사용할 수 있어도 위임된 **작업과 소스 변경 범위**를 넘을 권한은 없다. 다만 FULL_AUTONOMY와 Git 변경 감사는 OS 수준의 선제 격리가 아니다. Editor API 메서드의 외부 파일 변경은 Git 감사만으로 통제되지 않는다.
- 실행 요청에는 GUID와 만료 시각을 포함한다. 응답이 없거나 취소되면 대기 중 요청은 삭제한다. Editor가 이미 요청을 수령했으면 실행 여부를 알 수 없으므로 **자동 재시도하지 않고 `TIMEOUT_UNVERIFIED`**를 반환한다.
- `expected_result`와 실제 Editor 반환값이 일치해도 **Player 빌드 결과의 파일 존재·실행 검증까지 자동 통과한 것은 아니다**. HIGH가 산출물·실제 실행 결과를 확인한다.
- `Master-Polish.md`는 이 브리지 구현에서 읽기 전용으로만 참조한다.

## 진단

`EDITOR_BRIDGE_INSTALLED_WAIT_FOR_UNITY_IMPORT`: C# 파일은 저장됐지만 Editor Import/컴파일은 아직 입증되지 않았다.

`EDITOR_BRIDGE_TIMEOUT_UNVERIFIED`: Editor가 컴파일 중, 비활성화, 다른 사용자 세션 또는 실제 명령 실행 중일 수 있다. 창 포커스를 강제하지 말고 현재 Editor 컴파일 로그와 설치된 C# 상태를 확인한다.

`EDITOR_BRIDGE_RESULT_MISMATCH`: 요청한 작업 메서드는 반환했지만 기대한 결과가 아니다. 실제 반환값과 빌드 오류 보고를 판독한다.

`EDITOR_BRIDGE_INSTALLED_SOURCE_CHANGED` 또는 `SOURCE_CONFLICT`: 설치 파일이 내장된 버전과 다르므로 실행·덮어쓰기를 거부한다.

이 기능의 .NET CI는 설치·로컬 요청/응답·결과 판독을 모의 검증한다. **실제 Windows Unity Editor 자동 Import, 메뉴 명령, Player 빌드는 Unity 실행 로그를 확보해야 실증 완료**다.
