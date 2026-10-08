# 월드 생성·청크 수명주기·자원 컴포넌트

[전체 색인](README.md)

최초 조사 2026-10-03, 2026-10-06 도메인 분리의 자원 생성·채굴·삭제 경계를 현재 소스와 대조했다. 테스트 코드는 별도로 표시하며 이번 문서 감사에서 컴파일·테스트·Play Mode는 실행하지 않았다. 공간 인덱스와 Fence는 전체 색인의 공간 인덱스 문서에서 다룬다.

현재 연결은 `월드 설정 게시 → 초기 청크 요청 → Pending/Ready → EndCommand 자원 생성·완료 알림 → 다음 Command 완료 확정`이다. 생성된 자원은 `Synchronization 인덱스 등록 → 다음 Decision 채굴 판단 → Execution 매장량 변경 → EndBuilding 고갈 엔티티 삭제`를 거친다. 바닥 설정의 현재 소비자는 진단용 미리보기다.

## ResourceGenerationSettings

- **목적·필드:** `WorldSeed`는 자원 배치와 바닥 선택의 공통 시드, `InitialChunkSize`는 최초 요청할 청크 사각형 한 변의 길이다. `IComponentData`이며 생성자에서 0 이하 크기는 3으로 바꾼다.
- **부착·생성:** `WorldGenerationConfigLoadSystem`이 Resources의 월드 JSON을 검증해 같은 엔티티에 자원과 바닥 설정·버퍼를 게시한다. 기존 설정도 자원 버퍼·바닥 설정·바이옴/변형 버퍼의 완전성을 확인하며 자원 전용 등록을 전체 준비로 인정하지 않는다. 불완전하면 보충/중복 게시 없이 Fatal로 거부한다.
- **Reader·처리:** `InitialChunkLoadBootstrapSystem`은 크기를 읽어 초기 요청을 넣는다. `ResourceGenerationCommandSystem`은 Command에서 시드와 같은 엔티티의 자원 설정을 읽는다. `PrefabDatabaseInitializationSystem`은 자원 설정 엔티티의 유일성과 필수 프리팹을 검증한다. `V2FloorBiomePreview`는 시드를 바닥 계산에 전달한다.
- **생명주기:** 자동 로더는 성공·실패와 기존 Fatal 뒤 비활성화한다. 파일/파싱/검증 실패는 로그·해당 설정 미게시·즉시 SimulationFatalError이며 자동 재시도하지 않는다. 게시 실패는 이번 호출의 미완성 엔티티만 회수한다. 설정은 World 종료 때 정리한다.
- **결합 계약:** 자동 로더는 자원과 바닥을 함께 검증하지만 공개 `PublishConfig` 오버로드는 `floor == null`인 자원 전용 게시도 허용한다. 공개 함수 자체는 기존 싱글톤을 거부하지 않는다. 따라서 자동 초기화의 1회 게시와 공개 함수의 호출 가능 범위를 구분한다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/WorldGenerationConfigComponents.cs), [자동 로더](../../../Assets/Scripts/Systems/Initialization/WorldGenerationConfigLoadSystem.cs), [검증·게시](../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs), [초기 요청](../../../Assets/Scripts/Systems/Initialization/InitialChunkLoadBootstrapSystem.cs).

## ResourceGenerationConfigElement

- **목적·필드:** 품목 `ResourceType`, 후보 광맥의 발생 확률 `Weight`, 반경 범위, 셀 채움 확률, 셀별 매장량 범위를 담는 `IBufferElementData`다. 자원 노드의 현재 매장량은 이 버퍼가 아니라 `ResourceNode.Amount`다.
- **부착·생성:** `WorldGenerationConfigLoader.PublishConfig`가 `ResourceGenerationSettings` 엔티티에 버퍼를 추가하고 검증된 목록 순서로 채운다. 자동 경로는 JSON의 중복 종류·확률·반경·매장량 및 바닥 설정 검증을 통과한 경우에만 게시한다.
- **Reader·처리:** Initialization의 `PrefabDatabaseInitializationSystem`이 활성 항목에 필요한 자원 프리팹을 검사한다. Command의 `ResourceGenerationCommandSystem`은 `ResourceGenerationUtility.TryResolvePrefabs`로 활성 품목 프리팹을 먼저 전부 해결하고 `GenerateChunkResources`에 버퍼를 전달한다.
- **생성 계산:** `IsValidConfig`를 통과한 항목을 대상으로 시드·후보 청크·품목에서 난수를 만든다. 이웃 청크에서 시작한 광맥도 계산하되 목표 청크 안의 셀만 생성한다. 한 목표 청크의 임시 `occupied` 집합은 같은 셀의 중복 생성을 막으며, 순회 중 먼저 등록한 항목이 해당 셀을 차지한다.
- **생명주기·경계:** 설정 자체는 소비하거나 Clear하지 않는다. 생성기는 설정/프리팹이 준비되지 않으면 Ready를 유지한다. 자원 인스턴스의 생성은 EndCommand ECB로 확정한다. 설정 버퍼는 ECS가 소유하며 게시 후 변경·삭제하는 제품 코드 경로는 없다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/ResourceGenerationConfigComponents.cs), [설정 검증·게시](../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs), [Command 소비자](../../../Assets/Scripts/Systems/Command/ResourceGenerationCommandSystem.cs), [광맥 계산](../../../Assets/Scripts/Chunks/ResourceGenerationUtility.cs), [프리팹 검증](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs).

## FloorGenerationSettings

- **목적·필드:** 바이옴 영역 크기, 전이 폭, 경계 노이즈의 스케일·진폭, 가까운 바이옴의 선택 편향, 전이용 변형 수를 가진 `IComponentData`다. 별도 시드 없이 `ResourceGenerationSettings.WorldSeed`를 사용한다.
- **부착·생성:** 자동 월드 설정 로드의 바닥 검증 성공 후 같은 월드 설정 엔티티에 `FloorBiomeElement`·`FloorVariantElement`와 함께 직접 게시한다. 영역 크기는 양수, 노이즈 스케일·편향 지수는 양수 유한값, 노이즈 진폭은 0 이상 유한값, 전이 폭은 0 이상 영역 크기 이하인지 검사한다. 필요한 바이옴/변형 및 Sprite 참조가 없으면 자동 게시 전체가 실패한다.
- **Reader·처리:** 현재 ECS 시뮬레이션에는 전용 바닥 시스템이 없다. `V2FloorBiomePreview.Update`가 설정을 읽고 `FloorBiomeSampler.SelectFloor`에 전달한다. 샘플러는 노이즈로 셀 좌표를 변형한 뒤 영역과 경계 거리로 바이옴·전이 여부를 계산한다.
- **생명주기:** 게시 후 변경·소비·전용 제거 경로는 없다. 미리보기는 미리보기 객체가 이미 있으면 다시 만들지 않으므로 설정 변경을 매 프레임 다시 표시하는 구조가 아니다. 설정 자체는 World 수명 동안 보관되고 ECS가 정리한다.
- **결합 계약·현재 범위:** `TransitionVariantCount`는 `FloorVariantElement` 맨 앞 구간의 길이다. 샘플러 결과는 진단 색상 텍스처에 사용된다. 실제 바닥 Sprite 청크 엔티티 생성·렌더링 수명주기는 현재 연결되어 있지 않다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/FloorGenerationConfigComponents.cs), [검증·게시](../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs), [순수 계산](../../../Assets/Scripts/Chunks/FloorBiomeSampler.cs), [현재 소비자](../../../Assets/Scripts/Debug/V2FloorBiomePreview.cs).

## FloorBiomeElement

- **목적·필드:** 바이옴 ID(`FixedString64Bytes`), 선택 가중치, `FloorVariantElement` 안의 시작 위치·개수를 가진 `IBufferElementData`다. ID는 설정 식별용이며 현재 샘플러는 버퍼 인덱스로 바이옴을 선택한다.
- **부착·생성:** `WorldGenerationConfigLoader`가 중복되지 않는 ID와 양수 유한 가중치, UTF-8 길이, 바이옴별 변형을 검증한다. `PublishConfig`가 월드 설정 엔티티에 버퍼를 붙이고 JSON 순서로 기록한다.
- **Reader·처리:** `V2FloorBiomePreview`가 버퍼를 읽어 `FloorBiomeSampler`에 전달한다. 샘플러는 시드·영역 좌표 기반 값과 누적 `SelectionWeight`로 바이옴을 선택하며, 경계에서는 이웃 영역의 바이옴과 비교한다.
- **결합 계약:** `VariantStart`/`VariantCount`는 같은 엔티티의 변형 버퍼에서 연속 구간을 가리킨다. 전이용 변형을 먼저 채운 뒤 바이옴별 구간을 순서대로 추가하므로 이 오프셋과 버퍼 순서는 함께 유지된다.
- **생명주기·현재 범위:** 게시 후 Writer·Clear·런타임 제거는 없고 ECS 버퍼로 World 종료 때 정리된다. 바이옴별 엔티티나 점유 상태를 만드는 데이터가 아니며 현재 미리보기 계산의 입력이다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/FloorGenerationConfigComponents.cs), [버퍼 구성](../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs), [선택·구간 조회](../../../Assets/Scripts/Chunks/FloorBiomeSampler.cs), [미리보기](../../../Assets/Scripts/Debug/V2FloorBiomePreview.cs).

## FloorVariantElement

- **목적·필드:** Sprite의 Resources 경로(`FixedString128Bytes`)와 선택 가중치를 가진 `IBufferElementData`다. Sprite 객체 자체나 엔티티 참조를 보관하지 않는다.
- **부착·생성:** 월드 설정 검증에서 경로의 공백·UTF-8 길이·양수 유한 가중치와 `Resources.Load<Sprite>` 성공을 확인한다. `PublishConfig`가 월드 설정 엔티티에 전이용 변형을 먼저, 그 뒤 각 바이옴의 변형을 차례로 게시한다.
- **Reader·처리:** `FloorBiomeSampler.SelectFloor`는 선택 구간의 가중치로 구간 내 로컬 `VariantIndex`를 결정한다. `GetVariant`는 전이 여부 또는 바이옴 시작 오프셋을 적용해 항목을 반환하는 조회 API다. 제품에서 샘플러를 호출하는 곳은 `V2FloorBiomePreview`다.
- **현재 사용 범위:** 미리보기는 선택 인덱스와 전이 여부로 색상·밝기를 정하고, `SpriteResourcePath`의 Sprite로 타일을 그리지는 않는다. 경로의 실제 제품 사용은 설정 로더의 존재 검증까지 확인된다.
- **생명주기·결합:** 항목은 요청처럼 소비하지 않는다. 런타임 Writer·삭제 경로는 없고 ECS가 버퍼를 정리한다. `TransitionVariantCount`와 바이옴의 시작/개수는 이 버퍼의 배치를 함께 설명하는 필드다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/FloorGenerationConfigComponents.cs), [Sprite 확인·배열 구성](../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs), [선택·조회](../../../Assets/Scripts/Chunks/FloorBiomeSampler.cs), [미리보기 사용 범위](../../../Assets/Scripts/Debug/V2FloorBiomePreview.cs).

## ChunkLoadRequestQueue

- **목적·종류:** 청크 좌표 요청 버퍼의 엔티티를 찾는 필드 없는 `IComponentData` 태그다. 청크 하나마다 붙는 태그가 아니며 요청 큐 싱글톤에 부착된다.
- **생성·초기화:** Initialization의 `InitialChunkLoadBootstrapSystem`이 월드 설정이 준비된 후 없으면 큐 엔티티와 `ChunkLoadRequestElement` 버퍼를 함께 만든다. 기존 큐가 있으면 그 엔티티의 버퍼를 사용한다.
- **Reader·처리:** 같은 부트스트랩이 초기 N×N 좌표를 추가하고 비활성화한다. Command의 `ChunkLoadCommandSystem`은 태그로 큐를 찾고 좌표 버퍼를 처리한다. 현재 제품 소스에서 카메라 이동 등에 따른 추가 Producer는 확인되지 않는다.
- **생명주기:** 버퍼 내용은 매 Command 소비하지만 태그와 큐 엔티티는 유지된다. 요청마다 생성·삭제하지 않는다. 전용 큐 파괴·청크 언로드 경로는 없고 World 종료 때 ECS가 정리한다.
- **결합 계약:** 기존 큐 엔티티에는 요청 버퍼가 있어야 한다. `GeneratedChunkTracker` 엔티티와 큐 엔티티는 역할과 생성 경로가 별개다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/ChunkRequests.cs), [생성·Producer](../../../Assets/Scripts/Systems/Initialization/InitialChunkLoadBootstrapSystem.cs), [Consumer](../../../Assets/Scripts/Systems/Command/ChunkLoadCommandSystem.cs).

## ChunkLoadRequestElement

- **목적·필드:** 청크 좌표 `ChunkCoord` 하나를 전달하는 `IBufferElementData`다. 개별 요청 엔티티가 아니라 `ChunkLoadRequestQueue`의 버퍼 항목이다.
- **생성·초기화:** `InitialChunkLoadBootstrapSystem`이 `InitialChunkSize`를 읽고 `(-size/2, -size/2)`에서 시작하는 N×N 좌표를 한 번 추가한다. 좌표는 셀 좌표가 아닌 청크 좌표다.
- **Reader·Writer:** Command의 `ChunkLoadCommandSystem`이 버퍼를 순회한다. 이미 Tracker의 `Map`에 있으면 무시하고 `Pending.Add`가 실패하는 중복도 무시한다. 새 좌표만 Pending과 Ready에 등록한다.
- **소비·재사용:** 요청 버퍼는 순회 후 즉시 `Clear`한다. 생성 준비가 아직 안 된 요청의 재시도 상태는 이 버퍼가 아니라 `GeneratedChunkReadyElement`와 `Pending`으로 인계한다. 버퍼/큐 엔티티 자체는 재사용한다.
- **반영 시점:** 접수와 중복 제거는 Command 메인 스레드에서 직접 이루어진다. 자원 엔티티 생성 완료와 같지 않으며 실제 생성은 후속 ResourceGeneration의 EndCommand ECB에서 확정한다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/ChunkRequests.cs), [초기 좌표 구성](../../../Assets/Scripts/Systems/Initialization/InitialChunkLoadBootstrapSystem.cs), [접수·Clear](../../../Assets/Scripts/Systems/Command/ChunkLoadCommandSystem.cs).

## GeneratedChunkTracker

- **목적·필드:** 생성 반영이 확인된 청크 집합 `Map`과 접수/반영 대기 집합 `Pending`을 가진 `IComponentData`다. 둘 다 `NativeParallelHashSet<int2>`이며 공간 점유 인덱스가 아니다.
- **부착·생성:** `ChunkLoadCommandSystem.OnCreate`가 싱글톤이 없으면 별도 엔티티에 Persistent 할당 집합 2개(초기 용량 256)와 Ready/Completed 버퍼를 만든다. 엔티티가 사전에 있으면 생성·보완하지 않는다.
- **단독 Writer·흐름:** `ChunkLoadCommandSystem`이 Command에서 이전 EndCommand의 Completed를 `Map.Add`·`Pending.Remove`로 확정한다. 이어 신규 요청을 Pending에 추가하고 Ready를 만든다. `ResourceGenerationCommandSystem`은 Tracker 엔티티를 찾아 연결된 버퍼를 사용하며 집합을 직접 변경하지 않는다.
- **결합 계약:** Ready가 소비되어도 Pending은 남는다. 같은 EndCommand에서 자원 스폰 뒤에 기록한 Completed가 다음 Command에 도착해야 완료 처리하므로, ECB 기록만 한 청크를 이미 생성된 청크로 취급하지 않는다. 자원이 0개인 정상 생성도 같은 완료 절차를 따른다.
- **생명주기·해제:** 완료 좌표는 유지하며 재요청을 드롭한다. 청크 언로드/재생성용 삭제 경로는 없다. `ChunkLoadCommandSystem.OnDestroy`가 싱글톤에서 두 집합을 찾아 각각 `IsCreated`를 확인하고 Dispose한다. 일반 컴포넌트 데이터와 달리 내부 NativeContainer는 이 명시적 해제 경로를 가진다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/ChunkLifecycleComponents.cs), [소유·생성·Dispose](../../../Assets/Scripts/Systems/Command/ChunkLoadCommandSystem.cs), [완료 기록](../../../Assets/Scripts/Systems/Command/ResourceGenerationCommandSystem.cs).

## GeneratedChunkReadyElement

- **목적·필드:** `ChunkCoord`를 가진 `IBufferElementData`로, 접수됐지만 자원 생성 명령을 아직 기록하지 않은 청크를 전달한다. Tracker와 같은 엔티티에 부착된다.
- **생성·Writer:** 버퍼는 `ChunkLoadCommandSystem.OnCreate`가 만들고, Command의 신규 요청 접수 시 `Pending.Add`에 성공한 좌표만 추가한다.
- **Reader·처리:** `UpdateAfter(ChunkLoadCommandSystem)`인 `ResourceGenerationCommandSystem`이 같은 Command에서 읽는다. 자원 DB/매핑 버퍼, 설정 버퍼, EndCommand ECB, 활성 품목 프리팹과 Transform이 모두 준비되어야 생성 기록을 시작한다.
- **대기·소비:** 준비 조건이 충족되지 않으면 버퍼를 유지한다. 처리할 모든 청크의 스폰 및 Completed 기록을 마친 뒤 Ready를 즉시 `Clear`한다. 재시도용으로 남은 Ready와 이미 기록한 Pending을 구분한다.
- **반영·수명:** Clear는 메인 스레드의 직접 버퍼 변경이며 자원·Completed 실체화는 EndCommand다. 생성 성공 후 Ready 항목은 사라져도 버퍼와 Tracker 엔티티는 유지된다. ECS 버퍼 자체의 별도 Dispose는 없다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/ChunkLifecycleComponents.cs), [생성·접수](../../../Assets/Scripts/Systems/Command/ChunkLoadCommandSystem.cs), [대기 조건·소비](../../../Assets/Scripts/Systems/Command/ResourceGenerationCommandSystem.cs).

## GeneratedChunkCompletedElement

- **목적·필드:** `ChunkCoord`를 가진 `IBufferElementData`로, 해당 청크의 자원 생성 ECB가 재생된 사실을 다음 Command에 전달한다. Tracker 엔티티의 버퍼다.
- **생성·Writer:** 버퍼는 `ChunkLoadCommandSystem.OnCreate`가 만든다. 항목은 `ResourceGenerationCommandSystem`이 청크별 자원 스폰 명령 뒤에 같은 ECB의 `AppendToBuffer`로 기록한다.
- **반영 시점:** EndCommand 재생 때 항목이 실제 버퍼에 나타난다. 광맥 계산 결과 자원이 없더라도 완료 알림을 기록한다. 호출 직후 Ready Clear와 Completed의 실제 추가는 서로 다른 시점이다.
- **Reader·소비:** 다음 Command의 `ChunkLoadCommandSystem`이 항목별로 완료 Map에 추가하고 Pending에서 제거한다. 모든 완료 알림을 처리하면 즉시 버퍼를 `Clear`한다.
- **수명·결합 계약:** 완료 사실의 장기 보관은 Tracker.Map이 맡고, Completed는 단계 간 전달에만 쓰인다. 버퍼 자체는 재사용하며 항목별 엔티티 삭제나 NativeContainer Dispose는 필요 없다.
- **소스:** [정의](../../../Assets/Scripts/Components/World/ChunkLifecycleComponents.cs), [ECB 기록](../../../Assets/Scripts/Systems/Command/ResourceGenerationCommandSystem.cs), [다음 Command 확정](../../../Assets/Scripts/Systems/Command/ChunkLoadCommandSystem.cs).

## ResourceNode

- **목적·필드:** 매장 자원의 품목 `ResourceType`과 현재 잔량 `Amount`를 가진 `IComponentData`다. 바닥 자원 엔티티에 `GridPosition`·`LocalTransform`과 함께 부착된다. 물류 아이템의 `ItemIdentity`/`ItemOwnership`과 별개다.
- **생성·초기화:** `ResourceGenerationCommandSystem`이 호출한 `ResourceGenerationUtility`가 등록된 자원 프리팹을 Instantiate하고 셀 좌표·Transform·품목/매장량을 EndCommand ECB에 기록한다. 현재 제품 소스에서 ResourceNode 전용 Baker는 없고 런타임 생성 유틸리티가 값을 주입한다.
- **Reader·단계:** Synchronization의 `ResourceSpatialSyncSystem`이 `ResourceNode + GridPosition`을 인덱스에 등록한다. 다음 Decision의 `MinerDecisionSystem`이 회전된 채굴기 영역을 순회해 `Amount > 0`인 첫 자원을 대상으로 고른다. 개발용 `WorldInvariantValidationSystem`은 Synchronization 마지막에 자원과 공간 인덱스의 대응을 검증한다.
- **Writer·처리:** Execution의 `MinerExecutionSystem`은 단일 워커 Job에서 대상 존재·양수 잔량·생산 대기 상태를 재검사한다. 채굴 진행 완료 시 `ProductResult` 1개를 기록하고 유한 모드이면 `Amount`를 1 감소시킨다. 실제 물류 아이템은 StateApply의 Item Lifecycle이 생산 결과에서 생성한다.
- **종료·ECB:** 유한 모드에서 0 이하가 되면 같은 Job이 EndBuilding ECB에 `DestroyEntity`를 기록한다. 잔량 변경은 BuildingExecution에 먼저 반영되고 엔티티 삭제는 건물 그룹 종료의 EndBuilding, 인덱스 제거는 최종 EndSimulation 뒤 Synchronization이다. 무한 모드도 최초 대상의 `Amount > 0` 조건을 우회하지 않는다.
- **소스:** [정의](../../../Assets/Scripts/Components/Resources/ResourceComponents.cs), [생성](../../../Assets/Scripts/Chunks/ResourceGenerationUtility.cs), [대상 선택](../../../Assets/Scripts/Systems/Buildings/Decision/MinerDecisionSystem.cs), [차감·삭제](../../../Assets/Scripts/Systems/Buildings/Execution/MinerExecutionSystem.cs), [동기화](../../../Assets/Scripts/Systems/Synchronization/ResourceSpatialSyncSystem.cs), [개발용 검증](../../../Assets/Scripts/Validation/WorldInvariantValidationSystem.cs).

## ResourceConfig

- **목적·필드:** `IsResourceInfinite`를 가진 `IComponentData`로, 채굴 완료 때 매장량을 차감할지를 정하는 선택적 전역 설정이다.
- **부착·생성 여부:** 현재 `Assets/Scripts`에는 이를 생성하는 Init/Baker/설정 로더가 없다. `MinerExecutionSystem`은 싱글톤이 있으면 읽고 없으면 유한 모드(`false`)로 실행한다. 월드 자원 생성 JSON이 이 값을 게시하지 않는다.
- **확인된 테스트 경로:** `Phase4MinerPipelineTests.Test05_InfiniteResourceMode_DoesNotDecrementAmount`가 별도 설정 엔티티를 직접 생성하여 `true`를 부착한다. 이는 테스트 구성 경로를 확인한 것이며 이번 작업에서 해당 테스트를 실행한 결과는 아니다.
- **Reader·처리:** Execution의 `MinerExecutionSystem.OnUpdate`가 bool을 읽어 `MinerExecutionJob`에 전달한다. `true`이면 채굴 생산은 진행하되 잔량 차감·고갈 삭제를 생략한다. Decision과 Execution의 양수 잔량 조건은 그대로 남는다.
- **생명주기·현재 범위:** 제품 코드의 Writer·변경 API·제거 경로는 없다. 생성된 경우 일반 ECS 설정 엔티티로 남으며 World 종료 시 정리된다. 자체 NativeContainer나 Dispose 책임은 없다.
- **소스:** [정의](../../../Assets/Scripts/Components/Resources/ResourceConfigComponents.cs), [실제 Reader와 기본값](../../../Assets/Scripts/Systems/Buildings/Execution/MinerExecutionSystem.cs), [테스트 생성 경로](../../../Assets/Editor/Tests/Phase4MinerPipelineTests.cs).
