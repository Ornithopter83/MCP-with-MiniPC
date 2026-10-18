# UnityWorker — 기존 Unity Editor 직접 실행 브리지

## 목적

Unity 공식 `InitializeOnLoad`, `EditorApplication.update`, `EditorApplication.ExecuteMenuItem`, Reflection, `BuildReport` API로 만든 **자체 Editor 확장**이다. Unity 공식 기능만 사용하는 구현이지만, 브리지 자체는 Unity 기본 제공 제품이 아니다. `com.unity.pipeline` CLI 명령 카탈로그가 비어 있어도 독립적으로 동작할 수 있도록 구성했다.

## 설치 및 실행

1. **새 Unity 작업 접수 시 Worker가 먼저 `UnityEditorDirectBridge.Install`을 자동 수행한다.** 필요하면 부모 전문 역할이 MCP `worker_command(action=editor_bridge_install)`을 명시적으로 호출할 수도 있다. Worker는 대상 Unity 프로젝트의 `Assets/Editor/ProjectHub/ProjectHubEditorBridge.cs`에 내장된 Editor 스크립트를 **최초 한 번** 작성한다. 기존 파일의 내용이 다르면 덮어쓰지 않고 `SOURCE_CONFLICT`를 반환한다.
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

## 실행·Scene 실증

- `worker_command`의 `editor_bridge_scene_status`는 기존 Editor 메인 스레드에서 활성 Scene 경로, 루트 오브젝트 수·이름, PlayMode/컴파일 상태를 반환한다. 자동으로 씬을 재생성하거나 PlayMode를 전환하지 않는다.
- Worker가 캡처를 자동 수행하지 않는다. DESIGN/HIGH가 실제 PlayMode와 Scene 구성을 확인한 다음 `native_capture`를 직접 실행하고, 자신이 PlayMode를 시작한 경우에만 종료해 원래 상태를 복원한다.
- 기존 브리지 설치본과 내장 수신 스크립트가 다르면 Worker는 `INSTALLED_SOURCE_CHANGED`를 반환한다. 자동 덮어쓰기·권한 상승 없이 사용자가 설치 상태를 확인해야 한다.
- 각 브리지 호출은 `%LOCALAPPDATA%/ProjectHub/UnityWorker/editor-bridge-evidence`에 시간·응답·요청 ID를 기록한다. 응답 없이 대기 중이면 `PENDING_NOT_CLAIMED`, Editor가 가져간 뒤면 `CLAIMED_BY_EDITOR`로 진단한다. 후자에서는 자동 재시도를 하지 않는다.

## 1~4단계 직접 실행 경로 (2026-10-18)

1. **자동 설치·브리지 우선**: 새로운 사용자 Unity 작업 접수 시 기존 Editor 프로젝트에 수신기를 한 번 설치한다. 기존 다른 내용은 덮어쓰지 않는다. `unity_query(command=editor_state)`는 설치된 수신기의 고정 `scene_status`를 우선 실행하고, 응답이 없을 때만 비변경 CLI status로 돌아간다. `command_catalog`와 명시적 `editor_eval`은 호환성 때문에 CLI를 유지한다.
2. **사전 컴파일 진단**: `editor_bridge_scene_status`는 임의 C# 문자열을 받지 않고 Unity Editor에 미리 컴파일된 읽기 전용 명령을 실행한다. 현재 활성/로드 씬, 루트 GameObject 이름·수, Dirty/Play/Compiling/Updating 플래그를 반환한다. 제품용 임의 스크립트를 Worker가 작성하지 않는다.
3. **명시적 씬 동기화**: 전문 역할이 `editor_bridge_sync_scene`, `scene_path=Assets/Quiz/Scenes/QuizScene.unity`처럼 실제 활성 씬 경로를 지정한 경우에만 실행한다. 실행 전 현재 씬의 **디스크 버전**을 `Library/ProjectHub/SceneBackups`에 복사하고, **미저장 상태**는 Unity 공식 `EditorSceneManager.SaveScene(scene, backupPath, saveAsCopy: true)`로 `Assets/Editor/ProjectHub/SceneBackups/`에 별도 씬 파일을 남긴다. 백업 실패·씬 불일치·PlayMode/컴파일 중이면 재로드를 중단한다. 다른 로드된 씬을 닫지 않도록 현재 씬만 CloseScene 후 OpenScene(Additive)해 활성화한다. 백업 파일은 자동 삭제하지 않는다. 기존 Scene을 자동 재생성하거나 프로젝트 내 다른 작업자의 Dirty 상태를 저장하지 않는다.
4. **에셋 Import 갱신**: 전문 역할이 `editor_bridge_refresh_assets`를 명시적으로 요청하면 기존 Editor에서 `AssetDatabase.Refresh(ForceSynchronousImport)` 후 위 상태를 다시 조회한다. 스크립트 갱신 때문에 도메인이 재로드되면 요청 응답이 없어질 수 있으므로 해당 실행은 `UNVERIFIED`로 남기고 자동 재실행하지 않는다.

**권한 경계:** 임시 WORK가 3번을 실행하려면 `paths`에 대상 씬 파일과 `Assets/Editor/ProjectHub/SceneBackups/`를 모두 명시해야 한다. 4번은 Unity 프로젝트 Import 과정에서 자동 수정이 발생할 수 있어 변경 감사가 필요하다. 모든 동작은 기존 사용자 권한 안에서만 수행하며, 설치 성공을 실제 Editor Import 성공으로 간주하지 않는다.

**실환경 상태:** 이 변경은 요청대로 Unity 실행·Windows CI·테스트 검증을 수행하지 않은 구현이다. 실제 동작 여부는 다음 사용 시 확인해야 한다.

## 작업 시작 시 Editor 재시작

새 사용자 작업의 Git 사전 점검 이후 Worker가 브리지 소스를 먼저 설치한 다음, `UnityEditorStartupRestart`를 1회 실행한다. Unity CLI `status`가 보고하는 **해당 프로젝트 한 개의 PID**를 Process의 Unity.exe 경로와 GUI MainWindow로 교차 확인한다. 신원 확인이 불가능하거나 여러 Editor가 관측되면 종료하지 않는다.

확인된 Editor에는 `CloseMainWindow()`로 정상 종료만 요청한다. Unity가 미저장 Scene·Prefab 등의 저장 확인창을 띄우거나 종료를 거부하면 최대 35초 기다린 뒤 작업에 `EDITOR_RESTART_UNSAVED_OR_BUSY` 등 미검증 상태를 남긴다. `Kill()`·강제 저장·키보드/마우스 입력을 사용하지 않는다.

기존 Editor가 종료한 것이 확인되면 기존 `Unity.exe`를 `-projectPath`와 함께 최소화 시작하도록 요청한다. 105초 안에 해당 프로젝트의 **새 PID와 CLI ready**가 확인될 때만 `EDITOR_RESTART_VERIFIED`로 기록한다. 다른 프로젝트는 건드리지 않는다. 이 기능은 NEW 작업의 시작에서만 실행되며 각 WORK·DESIGN·HIGH, 마일스톤 반복 또는 HQ PAUSE에서 재실행하지 않는다.

윈도우가 새로운 Unity GUI를 활성화할 가능성은 완전히 제거할 수 없으며, FocusGuard 정책은 여전히 작업 종료 시점에만 복구한다. 이번 변경은 실제 Editor 재시작이나 Windows CI를 실행하지 않은 코드 변경이다.
