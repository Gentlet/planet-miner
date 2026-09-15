# Planet Miner 코드 지도

## 프로젝트 개요

`planet miner`는 격자 기반 2D 채굴·공장 시뮬레이션 Unity 프로젝트다. 카메라, 입력, UI Toolkit, 건물 배치 미리보기는 GameObject/MonoBehaviour 계층이 담당하고, 월드 상태와 시뮬레이션은 Unity Entities 컴포넌트와 `SystemBase` 시스템이 담당한다.

런타임 진입 장면은 `Assets/Scenes/SampleScene.unity`이며, 이 장면의 `SubScene`이 `Assets/Scenes/SampleScene/Sub.unity`를 로드한다. SubScene은 초기 청크 범위, 자원 생성 설정, 건물·아이템·자원·드론 프리팹 데이터베이스를 베이킹하여 ECS 싱글턴 데이터로 제공한다.

현재 소스 코드를 최종 기준으로 삼는다. `.agents/`의 계획 문서는 설계 배경을 확인할 때만 참고하고, 현재 동작을 판단할 때는 컴포넌트, 시스템 쿼리, 업데이트 순서, 실제 호출 관계를 다시 확인한다.

## 문서 기준과 탐색·출력 규칙

- 이 `AGENTS.md`를 프로젝트 공통 설계·안전 규칙, 문서 경로, 검색·출력 및 Unity CLI 검증 절차의 기준 문서(canonical source)로 둔다. 외부 `planet miner.md`는 시작 경로·Git 환경과 여기서 다루지 않는 고유 제약을 보관하고, `Docs/CodeMemory/`는 영역별 상세 계약과 실행 흐름을 보관한다. 같은 규칙의 전문을 다른 파일에 복제하지 않는다.
- 이미 컨텍스트에 제공되거나 이번 작업에서 읽은 동일한 지침은 변경되지 않았다면 다시 전문 출력하지 않는다. 지침 변경이나 컨텍스트 소실이 있으면 필요한 부분을 다시 확인한다.
- 대상 파일이나 심볼을 알고 있으면 저장소 전체 검색부터 시작하지 않는다. 기본 순서는 **파일명/경로 확인 → 정확한 심볼 검색 → 해당 메서드·초기화·직접 의존 구간 확인 → 필요한 경우에만 범위 확대**다. `rg --files`로 후보 경로를 좁히고, 리터럴 심볼에는 `rg -n -F`를 사용한다.
- 일반 코드 탐색은 대상이 속한 `Assets/Scripts/`, `Assets/UI/` 하위 경로와 관련 `Assets/Editor/` 테스트로 제한한다. 범위를 넓힐 때는 누락된 호출자, 변경된 데이터 계약, 실패한 테스트 등 이유를 정한다. 파일 목록·크기는 후보 선정에 사용하며 목록 전체의 내용을 읽을 이유로 삼지 않는다.
- `.meta`, `.unity`, `.prefab`, `.asset`, `Packages/`, `UIElementsSchema/`는 해당 형식·참조·직렬화·패키지 문제가 작업과 직접 관련 있을 때만 탐색한다. `Library/`, `Temp/`, `Logs/`, `.git/`도 일반 소스 검색에서 제외하고 진단 근거가 있을 때 필요한 경로만 연다. `Assets/Resources/Config/`는 관련 설정 의미를 확인할 때 추가한다. 파일 삭제나 Git 추적 제외를 뜻하지 않는다.
- 검색 결과가 많거나 출력이 잘리면 출력 한도를 늘리지 말고 경로·심볼·검색어를 좁힌다. `Get-Content -Raw ... | Select-String`은 사용하지 않는다. 줄 단위 `rg -n` 또는 `Select-String -Path`로 검색한 뒤 필요한 구간만 읽는다.
- 이미 읽은 대형 파일이 변경되지 않았다면 다시 전문 출력하지 않는다. 심볼·변경 구간과 필요한 직접 의존 부분만 확인한다. 다른 작업자의 변경이나 컨텍스트 소실이 의심되면 해당 파일의 변경 여부를 확인한다. 긴 작업의 인계 요약에는 읽은 경로·심볼, 확인한 계약과 남은 의문을 남겨 재탐색을 줄인다.
- CodeMemory 전체를 읽지 않는다. 현재 작업 영역에 직접 대응하는 문서만 읽고, 다른 영역은 변경된 계약이나 실패 근거가 생겼을 때 관련 절만 추가한다. 문서의 "함께 확인할 영역"은 해당 변경 종류에 적용하며, 모든 의존 문서를 연쇄적으로 읽으라는 뜻이 아니다. 테스트도 관련 사례의 초기화·헬퍼·assertion부터 확인한다.
- 대량 원본 로그는 컨텍스트에 재출력하지 않고 필요한 경우 파일로 보존한다. 성공 결과는 최종 상태·통계만, 실패는 실패명·메시지·관련 스택만 출력한다. 요약 과정에서 실패·경고·생략·미완료 상태를 숨기지 않는다.
- 코드 구조나 계약이 바뀌면 그 변경에 해당하는 AGENTS/CodeMemory 절의 갱신 필요성을 확인한다. 국소 변경을 이유로 모든 문서와 과거 기록을 재감사하지 않는다.

## 핵심 구조 및 기존 규칙

- MonoBehaviour는 입력, 카메라, UI, 미리보기, ECS 요청 생성에 집중한다. 게임플레이 상태, 공간 소유권, 아이템 소유권, 작업 진행은 ECS 시스템이 소유한다.
- `ChunkMapSystem`은 청크와 셀, 건물 점유·예약, 공사 현장, 자원, 월드 아이템, 벨트 인덱스, 전신주·드론 정거장 범위의 중앙 공간 인덱스다. 각 partial 파일은 같은 상태 소유자를 나눈 것이며 별도 공간 소유자를 만들지 않는다.
- `ItemStorageSystem`은 아이템을 월드와 건물 소유 버퍼 사이에서 이동시키고 저장·복원·소비하는 경계다. `StoredItem`, `Disabled`, `StoredItemElement`, `ProducedItemElement`를 다른 시스템에서 직접 조합해 소유권을 변경하지 않는다.
- 건물 설치는 모든 footprint 셀을 원자적으로 예약한 뒤 요청을 만든다. 실패 시 전체 예약을 되돌리고, 실제 건물 등록 시 `ChunkMapSystem`이 예약을 해제한다.
- 건물과 공사 현장의 `GridPosition.gridPosition`은 anchor다. 생성 시 정규화한 회전 전 `BuildingFootprint.size`는 런타임에 불변이며, 생성 전 후보/예약만 `BuildingPrefabElement.size`를 사용한다. 공간 계산과 현장 방향 계약은 [WorldSpatialAndResources.md](Docs/CodeMemory/WorldSpatialAndResources.md)를 따른다.
- 배치부터 완성까지의 주 흐름은 `ConstructionSiteCreateRequest -> ConstructionSite -> DroneTask/Reservation -> 자재 전달 -> BuildingSpawnRequest -> BuildingOccupantRequest -> BuildingOccupant`이다.
- 철거와 공사 취소는 즉시 상태를 건너뛰지 않는다. 사용자 철거는 드론 작업을 만들고, 공사 취소는 관련 작업·도착 자재·셀 예약을 함께 정리한다. 같은 프레임에는 `ConstructionCancelSystem -> ConstructionCompletionSystem -> BuildingSpawnSystem` 순서를 보존한다.
- 월드 아이템의 셀 이동은 `LocalTransform` 변경과 enableable `ItemCellChanged`를 통해 `ItemTrackingSystem`으로 전달한다. 같은 프레임의 건물 입출력은 `ItemTrackingSystem` 및 `ItemStorageSystem`의 immediate API를 사용한다.
- 전력망 토폴로지는 `PowerGridSystem`, 전력 공급 범위의 셀 인덱스는 `ChunkMapSystem`이 소유한다. 공급 범위 중첩은 망 연결 조건이 아니며, 전신주 connection range로만 연결한다.
- 드론 정거장 범위 인덱스는 `ChunkMapSystem`, 정거장 등록과 네트워크 병합·분리는 `DroneStationNetworkSystem`, 작업·예약·운반·회수 상태는 각 드론 ECS 시스템이 소유한다.
- `BuildingUI`, `BeltMoveSystem`, `ChunkMapSystem`, 전력·드론의 partial 클래스는 코드 정리용 분할이다. 같은 책임을 가진 새 UI 문서나 시스템, 캐시를 중복 생성하지 않는다.
- 구조 변경은 ECB 재생 시점까지 지연될 수 있다. 구조 변경 전후에 `DynamicBuffer`를 계속 보관하지 말고, 필요하면 `DynamicBufferCopyUtility`로 읽기 사본을 만들고 변경 후 버퍼를 다시 얻는다.
- 격자 변환은 `VectorExtension.ToGridCell`, `Float3Extension.ToGridCell`, 청크 변환은 `ChunkUtility`, footprint 계산은 `BuildingFootprintUtility`, 방향 조합은 `DirectionExtension`을 사용한다. 음수 좌표와 회전 footprint 규칙을 임의 구현하지 않는다.
- `DirectionEnum`의 `Up, Right, Down, Left, Count` 순서는 직렬화 및 회전 의미를 가진다. 도메인 enum은 기존 관례대로 `Enum` 접미사와 terminal `Count`를 유지하고, 정의된 `None` 값을 보존한다.
- 읽기 쉬운 작은 메서드와 설명적인 이름을 사용한다. null guard는 각각의 조기 반환으로 분리하고, null 검사와 행동 조건을 한 조건문에 섞지 않는다. 롤백과 소유권 경계는 축약 때문에 숨기지 않는다.
- UI Toolkit 콜백은 `OnEnable`에서 연결하고 `OnDisable`에서 해제한다. 월드 입력을 막는 요소는 `blocking-ui` USS 클래스를 사용한다.

## 주요 폴더

| 경로 | 역할 |
| --- | --- |
| `Assets/Scenes/` | 메인 GameObject 장면과 ECS 베이킹용 SubScene |
| `Assets/Scripts/Authoring/` | SubScene 및 프리팹을 ECS 컴포넌트·싱글턴 버퍼로 변환하는 Baker |
| `Assets/Scripts/Components/` | 격자, 청크, 아이템, 자원, 건물, 전력, 드론 데이터 계약 |
| `Assets/Scripts/Systems/Chunks/` | 중앙 공간 인덱스와 청크 로드 요청 처리 |
| `Assets/Scripts/Systems/Resources/` | 결정론적 청크 자원 생성과 자원 엔티티 생성 |
| `Assets/Scripts/Systems/Buildings/` | 공사·완성·생성·파괴 및 벨트·생산·저장 시뮬레이션 |
| `Assets/Scripts/Systems/Items/` | 월드 아이템 추적, 생성, 건물 소유권 전환 |
| `Assets/Scripts/Systems/Power/` | 설정 로드, 전력망 토폴로지, 수요·발전 배분, 석탄 연료 |
| `Assets/Scripts/Systems/Drones/` | 정거장 네트워크, 작업, 예약, 배차, 이동, 운반, 충전, 회수 |
| `Assets/Scripts/BuildingPlacement/` | 배치·복사·회전·설정 붙여넣기와 미리보기, ECS 요청 생성 |
| `Assets/UI/` | 기본/건설 모드 UI Toolkit 컨트롤러와 건물 정보 패널 |
| `Assets/Scripts/Extension/` | 방향, 격자, 레시피, 아이템 버퍼의 도메인 확장 메서드 |
| `Assets/Resources/Config/` | 공사 비용, 레시피, 저장 한도, 전력, 드론, 자원 생성 JSON |
| `Assets/Editor/` | 전력·드론·공사·운반 수명주기의 EditMode 테스트 |

## 시스템 지도와 실행 흐름

1. SubScene Baker가 프리팹 버퍼, 초기 로드 영역, 자원 설정을 만든다. 런타임 config loader가 `Resources/Config` JSON을 ECS 설정 엔티티로 게시한다.
2. `MainFacilityBootstrapSystem`이 `(0,0)`의 주 시설, 시작 저장품, 드론 정거장 및 기초 발전을 만든다.
3. `ChunkLoadAreaSystem`과 `CameraChunkLoader`가 `ChunkLoadRequest`를 만들고, 자원 생성·스폰 후 `ChunkMapSystem`이 셀 점유를 등록한다.
4. UI/입력이 배치, 취소, 철거, 레시피, 드론 아이템 작업 요청을 만든다. 요청은 해당 ECS 수명주기 시스템에서 실제 상태로 변환된다.
5. 월드 아이템은 `ChunkMapSystem -> ItemTrackingSystem -> BeltMoveSystem -> SplitterSystem -> MergerSystem -> StorageSystem`의 공간·물류 경계를 통과한다. 생산 시스템은 저장 소유권 API와 `ItemSpawnRequest`를 사용한다.
6. 전력 시스템은 전신주 토폴로지와 참가자 연결을 다시 계산하고 공급 비율을 게시한다. 생산과 드론 충전은 이 비율을 소비한다.
7. 드론 파이프라인은 작업 명령/계획 -> 아이템·용량 예약 -> 배차 -> 이동 -> 픽업/전달 또는 철거 -> 귀환/충전/보관 순으로 진행하며, 실패 시 예약 해제와 화물 회수로 전환한다.
8. 건물 파괴는 생산·저장 처리 뒤에 소유 아이템과 건설 재료를 월드로 복원하고 공간·전력·정거장 상태를 해제한다.

## 상세 구조 문서

| 영역 | 문서 |
| --- | --- |
| 장면, 베이킹, 설정 로드, 초기화 | [`Docs/CodeMemory/RuntimeBootstrap.md`](Docs/CodeMemory/RuntimeBootstrap.md) |
| 청크, 셀 공간 소유권, 자원 생성 | [`Docs/CodeMemory/WorldSpatialAndResources.md`](Docs/CodeMemory/WorldSpatialAndResources.md) |
| 배치, 복사, 공사, 완성, 취소, 철거 | [`Docs/CodeMemory/BuildingLifecycle.md`](Docs/CodeMemory/BuildingLifecycle.md) |
| 아이템 추적, 벨트, 저장, 채굴, 제작 | [`Docs/CodeMemory/ItemLogisticsAndProduction.md`](Docs/CodeMemory/ItemLogisticsAndProduction.md) |
| 전력 설정, 토폴로지, 배분, 연료 | [`Docs/CodeMemory/PowerGrid.md`](Docs/CodeMemory/PowerGrid.md) |
| 드론 정거장, 작업, 예약, 이동, 충전, 회수 | [`Docs/CodeMemory/DroneLogistics.md`](Docs/CodeMemory/DroneLogistics.md) |
| 연구 설정, 진행, 해금, 보상, 연구 UI 계약 | [`Docs/CodeMemory/ResearchSystem.md`](Docs/CodeMemory/ResearchSystem.md) |
| 카메라, 입력, UI 모드, 건물 정보 패널 | [`Docs/CodeMemory/UIAndPresentation.md`](Docs/CodeMemory/UIAndPresentation.md) |

## 기획 불명확성 및 확인

- 기능 구현에 필요한 기획이 부족하거나 선택지에 따라 게임 동작 또는 설계가 달라지는 경우 임의로 결정하지 않는다.
- 구현 전에 현재 코드, 이 문서, 작업 대상 시스템에 대응하는 `Docs/CodeMemory/` 문서에서 의도와 기존 규칙을 확인한다.
- 위 자료에서도 의도를 확인할 수 없으면 구현을 진행하지 말고, 동작이나 설계를 결정하는 선택지를 사용자에게 질문하여 확인받는다.
- 명확하게 정의되지 않은 게임 규칙이나 동작을 새로 추가하지 않는다.
- 기존 규칙 안에서 판단할 수 있는 일반적인 코드 구현 세부사항은 사용자에게 묻지 않고 진행하되, 기존 동작과 책임 경계를 유지한다.

## 변경 및 검증 시 주의

- Unity 버전은 `ProjectSettings/ProjectVersion.txt`, 패키지 버전은 `Packages/manifest.json`을 따른다.
- C# 또는 에셋을 변경한 경우에는 변경 범위에 맞는 Unity CLI 컴파일/재컴파일 확인을 수행한다. C# 변경은 크기와 무관하게 항상 확인한다. 읽기 전용 분석이나 문서만의 변경에는 새 컴파일을 실행하지 않는다. 비동기 명령은 제출만으로 완료로 간주하지 말고 최종 상태를 확인한다.
- 관련된 기존 EditMode 테스트가 있으면 전체 테스트보다 해당 테스트를 먼저 실행한다. 검증에서 문제가 발견되었을 때만 의존 시스템이나 더 넓은 테스트 범위로 확대한다.
- 단순한 코드 변경은 컴파일 확인으로 충분할 수 있다. 단순하지 않은 게임 로직, 라이프사이클·소유권 규칙, 계산 로직, 회귀 가능성이 높은 동작을 변경한 경우에만 관련 테스트를 추가하거나 수정한다.
- 실제 플레이 동작, UI·입력, 시각적 결과 등의 수동 작동 테스트는 사용자가 직접 수행한다. 명시적으로 요청받지 않은 경우 Play Mode 기반 작동 검증을 수행하지 않는다.
- 명시적으로 요청받지 않은 경우 변경 내용과 무관한 테스트 범위나 검증 절차를 확장하지 않는다. 검증 중 문제가 발견된 경우에만 추가적인 코드 분석과 검증을 진행한다.
- Unity CLI 명령 경로와 검증 순서는 아래 "Unity CLI 검증 절차"를 재사용한다.
- 관련 시스템을 수정하면 해당 영역 문서의 "함께 확인할 영역"과 `Assets/Editor/`의 연관 테스트를 먼저 확인한다.
- 컴파일/EditMode 테스트 성공은 Play Mode 입력, UI, 시각 결과, WebGL/브라우저 동작이나 persistence를 증명하지 않는다. 검증 보고에서 관련된 미수행 항목을 구분한다.
- 이 문서와 상세 문서는 현재 구조를 찾기 위한 지도다. 개별 필드와 분기 조건은 항상 실제 소스를 다시 확인한다.

### Unity CLI 검증 절차

Unity CLI 작업에는 설치된 스킬의 적용 지침을 따르되, 매 작업마다 전체 명령 목록이나 고급 참고 문서를 다시 출력하지 않는다. 아래 검증된 경로를 사용하고, 실행 파일 부재·버전/스키마 변경·명령 오류가 확인될 때만 해당 명령의 도움말이나 관련 참고 절을 조회한다.

```powershell
$unityCli = 'C:\Users\cyc07\AppData\Local\Unity\bin\unity.exe'
$planetMinerProject = 'C:\Projects\unity\PlanetMiner\planet miner'
```

일반 검증에서는 원본 상태 JSON을 반복 출력하지 않도록 `Tools/Codex/Verify-Unity.ps1`을 우선 사용한다. `-CompileOnly`는 컴파일만, `-TestFilter '<관련 테스트>'`는 컴파일 후 지정한 EditMode 테스트만 실행한다. 전체 EditMode 테스트는 사용자가 명시적으로 요청한 경우에만 `-AllEditModeTests`로 실행한다. 래퍼가 실행되지 않거나 결과 해석에 필요한 오류가 발생한 경우에만 아래 개별 명령 절차로 진단한다.

1. 코드·에셋 변경을 한 묶음으로 마친 뒤 검증한다. 대상 프로젝트가 열린 Editor에 연결되어 있으면 `command` 경로를 사용하며 standalone `unity test`를 실행하지 않는다. 연결이 없으면 그 상태를 확인하고 설치된 CLI의 해당 연결/오프라인 절차만 조회한다. Editor 설치·교체나 무조건적인 명령 재시도로 우회하지 않는다.
2. 아래 명령으로 재컴파일을 시작한 뒤 종료 상태를 확인한다. 명령들은 순차적으로 실행하며, 다음 테스트 명령은 컴파일 확인을 마친 뒤 실행한다.

   ```powershell
   & $unityCli command recompile --focus false --project-path $planetMinerProject --format json
   & $unityCli command recompile_status --project-path $planetMinerProject --format json
   ```

3. 상태 응답은 외부 JSON의 성공/오류부터 확인한다. `data.result`가 문자열이면 한 번 더 JSON으로 파싱하고, 객체이면 그대로 사용한다. null·파싱 실패·연결 오류는 완료로 간주하지 않는다. `completed` 또는 `up_to_date`라는 이름만으로 통과시키지 말고 `failed:false`, `errors:[]`, Editor가 준비되어 있고 컴파일/리로드 중이 아님을 확인한다. `up_to_date`이면 `Library/ScriptAssemblies/Assembly-CSharp.dll`이 최신 프로젝트 C# 소스보다 오래되지 않았는지 확인하고, 테스트 변경 시 테스트 어셈블리의 최신성도 확인한다.
4. 비동기 작업은 polling하되 상태 변경·새 오류·최종 결과만 출력하고 동일한 전체 응답을 반복 출력하지 않는다. 짧은 연속 polling 대신 간격을 늘리며 기다리고, 한 번의 대기는 60초 이내로 제한한다. 예상 시간을 넘기면 연결/Editor 상태와 필요한 로그만 진단한다. 재연결 중 일시 오류는 상태 조회를 복구하며, 작업 상태가 불명확하다는 이유만으로 컴파일이나 테스트를 중복 제출하지 않는다.
5. 컴파일 확인 후 관련 테스트 필터를 지정해 실행한다. `<관련 테스트 클래스 또는 필터>`는 실제 대상 이름으로 바꾼다.

   ```powershell
   & $unityCli command run_tests --mode editor --filter '<관련 테스트 클래스 또는 필터>' --async_tests true --project-path $planetMinerProject --format json
   & $unityCli command test_status --project-path $planetMinerProject --format json
   ```

6. 테스트도 최종 완료를 확인한다. 성공 시 필터와 Total/Passed/Failed/Skipped/Inconclusive 등 제공된 통계만 보고하고 전체 통과 목록·원본 로그를 다시 넣지 않는다. 실패 시 해당 테스트의 메시지와 필요한 스택을 확인한다. 수정 전 assertion이 실행되었다면 테스트 어셈블리 반영 여부부터 확인한다.
7. 추가 코드/에셋 변경, 검증 실패, 오래된 어셈블리 등 결과를 무효화하는 근거가 없다면 같은 컴파일·테스트를 반복하지 않는다. 새 변경이 있으면 그 변경 이후의 컴파일과 관련 테스트를 확인한다. 완료된 결과를 다시 보고하려는 목적만으로 재실행하거나 전체 테스트로 확대하지 않는다.
