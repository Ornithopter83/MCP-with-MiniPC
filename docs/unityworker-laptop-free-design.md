# UnityWorker — Windows 노트북 무료 DESIGN 도구

대상: **UnityWorker 브랜치**, 단일 Windows 노트북 (Core i7-12650H, RTX 4070 Laptop GPU).
다른 PC 연동, ComfyUI, 로컬 이미지 생성 모델, 유료 AI 크레딧은 이번 단계에서 제외한다.
이 문서는 실행 환경 안내이며 `Master-Polish.md`를 수정하지 않는다.

## 최소 구성과 설치

- 필수 기존 환경: 정상 작동 중인 Unity Editor + Unity CLI/Pipeline. 새로운 Unity Editor 인스턴스를 병렬 실행하지 않는다.
- 무료 기본 제작 도구: **Inkscape**. SVG 아이콘·버튼·패널 제작 및 PNG 변환.
- 무료 선택 제작 도구: **Blender**. 실제 3D 모델이나 렌더 소재가 필요한 프로젝트에서만 사용.
- 무료 에셋: 프로젝트에 이미 있는 파일 → 출처·라이선스 확인된 개별 Kenney CC0 에셋 → Poly Haven CC0 API 순으로 판단. 에셋 검색·획득은 ASSET MANAGER가 담당.

PowerShell에서 저장소 루트에서 실행한다.

```powershell
# 1. 설치 상태 확인. 아무것도 설치하지 않는다.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\UnityWorker_DesignTools.ps1

# 2. 최초 1회, 동의하여 Inkscape 설치가 필요한 경우에만
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\UnityWorker_DesignTools.ps1 -InstallInkscape

# 3. 3D 제작이 필요한 경우에만 Blender 설치
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\UnityWorker_DesignTools.ps1 -InstallBlender
```

`winget`이 없다면 공식 설치 방법을 사용한다. 설치 후 UnityWorker를 재시작하고 첫 줄의 READY 상태를 확인한다. 프로그램이 있다고 해서 UnityWorker가 Editor 사용권을 획득한 것은 아니다.

## DESIGN 자동 호출 정책

1. Worker가 DESIGN 프롬프트를 만들 때 노트북에 설치된 Inkscape·Blender의 실제 경로를 **읽기 전용으로 탐지**한다. 자동 설치는 절대로 하지 않는다.
2. DESIGN은 필요한 경우 무료 CLI 프로그램을 호출해 SVG/PNG 또는 3D 결과물을 만들고 **실제 프로젝트에 반영**한다. 생성 파일만 만들어 두거나 디자인 설명만 작성해서 완료하지 않는다.
3. Unity Editor, Scene, Prefab, Import, 재컴파일, PlayMode, 시각 캡처를 수행하는 호출은 **전체 독점** 실행한다. 다른 WORK를 포함한 AI 작업과 병렬 실행하지 않는다.
4. 코딩 페이즈 병렬 실행은 Editor 없는 독립 **코드 수정만** 허용한다. 외부 그래픽 제작도 Editor 호출과 동시에 실행하지 않는다.
5. 유료 API, 로그인 필수 서비스, 유료 팩, 라이선스 불명 에셋은 무인 획득하지 않는다. 모든 무료 팩이 무료인 것은 아니므로 개별 다운로드 조건을 확인한다.
6. 똑같은 CLI/MCP 환경 오류에 재시도 횟수를 소모하지 않고, 실패 사실과 대체 방법을 남긴다. 이미 확인한 결과는 무효화되지 않는 한 재검증하지 않는다.

예시(Inkscape 설치가 확인됐을 때):

```powershell
& 'C:\Program Files\Inkscape\bin\inkscape.exe' '.\Assets\Art\ui-button.svg' --export-filename='.\Assets\Art\ui-button.png'
```

실제 파일 경로·출력 형식·Unity Import 검증은 작업별로 확인한다. Blender 실행은 모델 제작 요청이 있을 때만 검토한다.

## 무료 에셋 출처

- [Kenney 게임 에셋](https://kenney.nl/assets): 개별 에셋 및 라이선스 검증.
- [Poly Haven API](https://polyhaven.com/our-api): 공개 API, CC0 HDRI·텍스처·모델. API 이용약관과 표시 요건 확인.
- [Inkscape CLI 설명](https://inkscape.org/ko/doc/inkscape-man-master.html): 지원하는 변환 옵션 확인.

## 범위에서 제외

현재는 4페이즈 스케줄러, HIGH 검증 체계, 동시 작업 수(기본 16), HQ 계약 구조를 변경하지 않는다.
