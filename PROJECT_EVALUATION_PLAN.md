# PlanetMiner 프로젝트 품질 평가 계획

> 목적: 현재 체크아웃된 PlanetMiner의 **실제 구현 코드**를 기준으로, 이후 프로젝트 품질 평가를 여러 개의 독립적인 Codex 작업으로 나누어 수행하기 위한 단계별 실행 계획을 정의한다.
>
> 이 문서는 **평가 결과가 아니다.** 현재 작업에서는 코드 수정, 리팩터링, 결함 판정, 점수화, 본격적인 성능 분석을 수행하지 않는다.

## 0. 평가 기준 및 현재 범위

### 기준

- 평가 시점의 **현재 체크아웃 코드와 Working Tree**를 최우선 근거로 사용한다.
- 기존 Architecture V2 문서와 Task 문서는 의도 파악용 참고 자료로만 사용한다. 문서와 코드가 다르면 코드를 기준으로 한다.
- 각 단계는 폴더/파일 단위가 아니라 **기능, 책임, 상태 소유권, 데이터 흐름** 단위로 평가한다.
- 같은 파이프라인을 구성하는 System / Component / Buffer / Request / Utility는 같은 단계에서 본다.
- 한 시스템의 주 평가는 한 단계에서만 수행한다. 다른 단계에서는 해당 시스템의 **공개 데이터 계약이나 연결 지점만** 확인한다.
- Job/Burst, Lookup, ECB, Structural Change, Sync Point, 메인 스레드 의존성은 별도 전역 단계로 몰지 않고 **각 기능 파이프라인 안에서 실제 사용 맥락과 함께 평가**한다.
- 마지막 통합 단계는 앞 단계의 결과를 사용하며, 프로젝트 전체 코드를 처음부터 다시 전수 탐색하지 않는다.

### 현재 코드에서 확인된 평가 대상

현재 런타임 구현은 다음 영역을 가진다.

- Architecture V2 6단계 실행 Phase
- Request / Enableable Request / ECB 수명주기
- Grid 및 Belt / Item / Building / Resource 공간 인덱스
- Item 생성·파괴·Ownership
- Belt Item Movement
- Storage 및 Building Item Input / Output
- Miner / Crafter 생산 파이프라인
- Splitter / Merger 및 공유 Belt 목적지 경합
- Chunk / Resource / Floor Biome 생성
- Building Placement / Construction / Building Lifecycle
- Config / Blob / Prefab Authoring 및 초기화
- Invariant Validation 및 EditMode 테스트

### 현재 독립 평가 단계에서 제외할 영역

아래 항목은 타입·설정·프리팹 확장 지점은 존재하지만 현재 독립적인 런타임 기능 System이 확인되지 않았으므로 별도 품질 평가 단계로 만들지 않는다.

- Power Grid
- Drone runtime
- Research runtime
- 실제 Floor 렌더링/Presentation

이들에 대한 enum, config, prefab placeholder는 **현재 사용되는 Building/Config 계약을 평가할 때만** 필요한 범위에서 확인한다.

---

# 전체 진행 순서

| 단계 | 평가 영역 | 주요 목적 | 선행 단계 |
| --- | --- | --- | --- |
| 1 | V2 실행 프레임워크와 상태 변경 경계 | 6단계 Phase, Request 수명주기, ECB 경계 확정 | 없음 |
| 2 | Config / Authoring / 초기화 데이터 공급 | Managed→ECS 경계, Blob·Prefab·Singleton 수명주기 확인 | 1 |
| 3 | Grid / Spatial Index / Synchronization | Source of Truth와 파생 인덱스, Job Fence 및 동기화 구조 확인 | 1 |
| 4 | Item Lifecycle / Ownership | Item 생성·파괴·소유권 State Owner와 Request 흐름 확인 | 1, 2, 3 |
| 5 | Belt Item Movement | Decision→Execution 이동 흐름과 병렬 처리·Backpressure 확인 | 3, 4 |
| 6 | Storage / Building Item Transfer | Belt↔Storage/생산건물 입출고, 슬롯 예약, Ownership 경계 확인 | 3, 4, 5 |
| 7 | Mining / Crafting Production | 생산 Decision·Execution·Result·StateApply와 재료/출력 흐름 확인 | 2, 3, 4, 6 |
| 8 | Splitter / Merger Routing | 라우팅 Decision, 공유 목적지 경합, Cursor/PlacementStamp 적용 확인 | 3, 4, 5, 6 |
| 9 | Chunk / Resource / Floor Generation | 청크 요청 수명주기, 결정론적 생성, Resource 생성·동기화 확인 | 1, 2, 3 |
| 10 | Construction / Building Lifecycle | 배치→현장→자재→완공/취소→건물 생성 전체 수명주기 확인 | 2, 3, 4, 5, 6, 7, 8 |
| 11 | Validation / Test Architecture | 앞 단계 계약을 테스트·Invariant가 실제로 검증하는지 확인 | 1~10 |
| 12 | 전체 프로젝트 통합 아키텍처 평가 | 단계별 결과를 합쳐 전역 일관성·결합도·확장성 검증 | 1~11 |

---

# 단계별 평가 실행 공통 규칙

각 단계는 별도 Codex 세션에서 실행할 수 있어야 한다.

세션 시작 시 다음 순서만 수행한다.

1. 현재 branch / working tree 확인.
2. 이 문서의 해당 단계에 적힌 **주요 대상 코드만 우선 읽기**.
3. 선행 단계의 “다음 단계 전달 정보”가 있으면 그것을 먼저 사용.
4. 불명확한 연결이 있을 때만 검색 범위를 인접 코드로 확장.
5. Architecture V2 문서는 코드 의도 확인이 필요한 경우에만 참고.
6. 코드 수정은 하지 않고 평가 결과만 작성.
7. 정상적으로 설계된 부분은 정상이라고 명시하고, 문제를 만들기 위한 문제 제기는 하지 않는다.

각 단계의 평가 결과는 최소한 다음 형식으로 남긴다.

- 확인한 실제 데이터 흐름
- 확인된 State Owner / Source of Truth
- 잘 설계된 부분
- 개선이 필요한 부분
- 구조적 위험도와 근거 코드
- 성능상 확인이 필요한 부분과 실제 근거 여부
- 다음 단계 전달 정보
- 미확인 또는 후속 확인 필요 항목

성능 문제는 코드 모양만으로 단정하지 않는다. 명백한 강제 동기화나 직렬 경로는 구조적 특성으로 기록할 수 있지만, 비용의 심각도는 Profiler 근거와 구분한다.

---

# 단계 1 — V2 실행 프레임워크와 상태 변경 경계

## 1. 평가 목적

- 프로젝트 전체의 기본 실행 순서가 Architecture V2의 6단계 Phase와 일치하는지 확인한다.
- Persistent State, Frame Decision, Transient Request, Structural Change의 경계를 확인한다.
- Request 소비 책임과 EndStateApply ECB Playback 경계를 기준 계약으로 확정한다.
- 이후 모든 단계에서 사용할 “어느 Phase에서 무엇을 변경해도 되는가”의 기준을 만든다.

## 2. 주요 대상 코드

### 주요 폴더

- `Assets/Scripts/Phases/`
- `Assets/Scripts/Common/`
- `Assets/Scripts/Systems/5_StateApply/` 중 공통 실행 경계

### 주 평가 대상

- `GameSimulationGroup`
- `CommandGroup`
- `DecisionGroup`
- `ReservationGroup`
- `ExecutionGroup`
- `StateApplyGroup`
- `SynchronizationGroup`
- `IRequestComponent`
- `IEnableableRequest`
- `EndStateApplyEntityCommandBufferSystem`

### 보조 확인 대상

- 실제 System들의 `UpdateInGroup`, `UpdateBefore`, `UpdateAfter` 선언
- `GameConstants`

## 3. 확인할 데이터 흐름

```text
외부/도메인 입력
→ Command
→ Decision
→ Reservation
→ Execution
→ StateApply
→ EndStateApply ECB Playback
→ Synchronization
```

Request의 일반 흐름:

```text
Producer
→ IRequestComponent / IEnableableRequest
→ Consumer
→ 성공/실패/보류 처리
→ Destroy / Disable
```

## 4. 핵심 평가 항목

- Phase 책임 분리
- 세부 System 순서 의존성의 최소화 여부
- StateApply 이전/이후 Structural Change 경계
- Consume-on-Apply 일관성
- IEnableableComponent와 IEnableableRequest의 의미 구분
- ECB가 단순 데이터 전달 수단으로 남용되는지 여부
- 직접 System 호출이나 lifecycle 제어 존재 여부
- 공통 실행 규칙이 실제 코드에서 예외 없이 이해 가능한지

## 5. 선행 평가 단계

없음

## 6. 다음 단계에 전달해야 할 정보

- 확정된 6단계 Phase 실행 순서
- StateApply와 ECB Playback의 실제 시점
- Request / Enableable Request의 수명주기 규칙
- 허용되는 System 간 의존 방식
- 발견된 실행 순서 예외가 있다면 정확한 System 이름과 이유

## 7. 코드 읽기 범위 제한

이 단계에서는 각 게임 기능의 내부 알고리즘을 평가하지 않는다. System 파일은 **Group/순서/ECB/Request 연결을 확인하는 수준**까지만 읽는다.

---

# 단계 2 — Config / Authoring / 초기화 데이터 공급 경계

## 1. 평가 목적

- Managed 설정·Unity Authoring 데이터가 ECS 런타임 데이터로 들어오는 경계를 확인한다.
- BlobAsset, Prefab Database, Singleton, DynamicBuffer의 생성·소유·해제 책임을 확인한다.
- 동일 의미의 설정이 중복 Source of Truth가 되지 않는지 확인한다.
- 런타임 핵심 System이 Managed API나 UnityEngine 객체에 불필요하게 의존하지 않는지 확인한다.

## 2. 주요 대상 코드

### Authoring / Baker

- `BuildingPrefabDatabaseAuthoring`
- `ItemAuthoring`
- `ItemPrefabDatabaseAuthoring`
- `ResourcePrefabDatabaseAuthoring`

### Config / Registry

- `BuildingConfigLoader`
- `RecipeConfigLoader`
- `WorldGenerationConfigLoader`
- `BuildingConfig`, `BuildingConfigElement`
- `BuildingConstructionMaterialElement`
- `BuildingRuntimeConfig`, `BuildingRuntimeConfigElement`
- `ItemRegistry`, `ItemRegistryBlob`
- `RecipeRegistry`, `RecipeRegistryBlob`
- `PrefabDatabaseComponents`

### Initialization

- `BuildingConfigInitSystem` / `BuildingConfigLoadSystem`
- `ItemConfigInitSystem`
- `RecipeInitSystem`
- `WorldGenerationConfigLoadSystem`

## 3. 확인할 데이터 흐름

```text
JSON / Resources / StreamingAssets / Inspector
→ Managed Loader 또는 Baker
→ 검증
→ Blob / Singleton / DynamicBuffer / Prefab Entity
→ Runtime System ReadOnly 소비
```

## 4. 핵심 평가 항목

- 설정 Source of Truth
- BuildingConfig와 BuildingRuntimeConfig의 역할 중복 여부
- BlobAssetReference 생성·교체·Dispose 수명주기
- Prefab DB의 누락/중복/잘못된 타입 처리
- Managed 초기화 영역과 Burst 런타임 영역의 경계
- 초기화 순서 의존성
- Fallback 정책의 일관성
- 테스트 환경용 fallback과 실제 런타임 계약의 분리
- Config 확장성

## 5. 선행 평가 단계

- 1단계 V2 실행 프레임워크

## 6. 다음 단계에 전달해야 할 정보

- Item / Recipe / Building / World 설정의 실제 Source of Truth
- 각 Registry/Singleton의 생성자와 수명주기 Owner
- Prefab lookup 계약
- 런타임 System이 설정 준비 여부를 판단하는 방식
- fallback이 허용되는 환경과 strict fail 환경

## 7. 코드 읽기 범위 제한

이 단계에서는 Miner/Crafter/Construction의 **게임 규칙 자체**를 평가하지 않는다. 설정 데이터가 만들어져 런타임에 전달되는 경계까지만 평가한다.

---

# 단계 3 — Grid / Spatial Index / Synchronization 인프라

## 1. 평가 목적

- GridPosition 등 권위 상태와 Spatial Index의 Source of Truth 관계를 확인한다.
- Belt / Item / Building / Resource 인덱스의 공통 구조와 차이를 평가한다.
- Reader/Writer Fence가 Job 의존성을 올바르게 표현하는지 확인한다.
- Synchronization 단계의 rebuild, capacity 확장, `Complete()` 호출이 병렬성을 어떻게 제한하는지 확인한다.

## 2. 주요 대상 코드

### 공통 데이터

- `GridPosition`
- `Direction`
- `BuildingFootprint`
- `DirectionExtensions`
- `ChunkUtility`

### Spatial Index / Fence

- `BeltSpatialIndex`, `BeltSpatialIndexFence`
- `ItemSpatialIndex`, `ItemSpatialIndexFence`
- `BuildingSpatialIndex`, `BuildingSpatialIndexFence`
- `ResourceSpatialIndex`, `ResourceSpatialIndexFence`

### Synchronization System

- `BeltSpatialSyncSystem`
- `ItemSpatialSyncSystem`
- `BuildingSpatialSyncSystem`
- `ResourceSpatialSyncSystem`

## 3. 확인할 데이터 흐름

```text
권위 Component
(GridPosition / Direction / ItemOwnership / BuildingFootprint ...)
→ SynchronizationGroup
→ Clear Index
→ Parallel Populate
→ Spatial Index + Fence
→ 다음 프레임 Decision/Command Reader
```

## 4. 핵심 평가 항목

- Source of Truth와 파생 인덱스 구분
- NativeParallelHashMap / MultiHashMap 선택 적합성
- NativeContainer 생성·Dispose 책임
- Fence의 Reader/Writer JobHandle 결합
- `ScheduleParallel`의 실제 안전성
- Capacity 증가 시 메인 스레드 `Complete()`
- 매 프레임 전체 rebuild 비용 구조
- Building 다중 타일 인덱싱
- Item의 World/Stored 필터링
- 중복 키 또는 잘못된 점유 처리
- Sync Point 가능성과 실제 필요성
- 향후 대규모 월드에서의 확장성

## 5. 선행 평가 단계

- 1단계 V2 실행 프레임워크

## 6. 다음 단계에 전달해야 할 정보

- 각 Spatial Index의 Source of Truth
- Reader가 반드시 지켜야 하는 Fence 사용 규칙
- Index 갱신이 관찰 가능한 프레임 시점
- 각 Index의 key/value 의미
- 메인 스레드 강제 동기화가 발생할 수 있는 조건

## 7. 코드 읽기 범위 제한

이 단계에서는 Belt 이동, Miner 탐색, Placement 검증 등 **인덱스 소비자의 도메인 알고리즘은 평가하지 않는다.**

---

# 단계 4 — Item Lifecycle / Ownership Pipeline

## 1. 평가 목적

- Item 생성·파괴·소유권 이전의 State Owner를 확정한다.
- World Item / Stored Item 전환 시 Component, Buffer, Rendering, Spatial 상태의 정합성을 확인한다.
- 독립 Request Entity와 대상 부착형 Enableable Request의 선택이 적절한지 확인한다.
- Item의 Structural Change가 StateApply/ECB 경계에서 일관되게 처리되는지 확인한다.

## 2. 주요 대상 코드

### Component / Request

- `ItemIdentity`
- `ItemOwnership`
- `SpawnItemRequest`
- `DestroyItemRequest`
- `TransferOwnershipRequest`
- `ItemSpawnDestination`
- `ItemRegistry`는 Stage 2 결과만 참조

### StateApply

- `ItemLifecycleApplySystem`
- `ItemOwnershipApplySystem`

### 연결 데이터

- `StoredItemElement`
- `ProductItemElement`
- `ProductResult`의 Item spawn 연결 부분
- `ItemSpatialIndex`는 Stage 3 계약만 참조

## 3. 확인할 데이터 흐름

생성:

```text
Producer
→ SpawnItemRequest 또는 ProductResult
→ ItemLifecycleApplySystem
→ Item Entity 생성
→ World / Storage / Product 목적지 초기화
→ EndStateApply ECB
→ ItemSpatialSync
```

소유권 이전:

```text
Domain StateApply
→ TransferOwnershipRequest 활성화
→ ItemOwnershipApplySystem
→ ItemOwnership 변경
→ Rendering structural change
→ Request Disable
```

파괴:

```text
Owner Buffer 선제거
→ DestroyItemRequest Enable
→ ItemLifecycleApplySystem
→ Entity Destroy
```

## 4. 핵심 평가 항목

- ItemOwnership 단일 Source of Truth
- Owner Buffer와 Ownership의 양방향 정합성
- Producer와 Consumer 책임 분리
- Consume-on-Apply
- IEnableableRequest 사용 적합성
- Structural Change 최소화
- ECB Playback 이후 관찰 시점
- Stored Item의 GridPosition 의미
- 렌더링 활성/비활성 전환 책임
- 병렬 Job에서의 Lookup write 충돌 가능성
- 생성/파괴 실패 정책과 테스트 가능성

## 5. 선행 평가 단계

- 1단계 실행 프레임워크
- 2단계 데이터 공급
- 3단계 Spatial 계약

## 6. 다음 단계에 전달해야 할 정보

- ItemOwnership의 유일한 Writer/Owner
- Buffer 변경과 Ownership 변경의 순서 계약
- Item 생성/파괴의 실제 소비 시점
- Stored ↔ World 전환에 필요한 필수 Component 상태
- Item Domain 외부 시스템이 사용해야 할 공개 계약

## 7. 코드 읽기 범위 제한

Storage, Crafter, Construction이 Item 상태를 요청하는 코드는 연결 지점만 확인한다. 해당 도메인의 전체 로직은 후속 단계에서 평가한다.

---

# 단계 5 — Belt Item Movement Pipeline

## 1. 평가 목적

- Belt 이동의 Decision / Execution 책임 분리가 실제 코드에서 유지되는지 확인한다.
- Item 간격, Backpressure, 타일 경계 이동의 데이터 흐름을 검토한다.
- Belt/Item Spatial Index 읽기와 병렬 Job의 의존성 처리를 확인한다.
- 이동 상태가 고빈도 Persistent/Frame State로 적절하게 표현되어 있는지 확인한다.

## 2. 주요 대상 코드

### Component

- `BeltComponent`
- `BeltMovementState`
- `BeltMovementDecision`
- `GridPosition`
- `ItemOwnership`

### System

- `BeltMovementDecisionSystem`
- `BeltMovementExecutionSystem`

### 참조 인프라

- `BeltSpatialIndex` / Fence
- `ItemSpatialIndex` / Fence
- `BeltSpatialSyncSystem`, `ItemSpatialSyncSystem`은 Stage 3 계약만 참조
- `GameConstants`

## 3. 확인할 데이터 흐름

```text
World Item + BeltMovementState
+ BeltSpatialIndex + ItemSpatialIndex
→ BeltMovementDecisionSystem
→ BeltMovementDecision
→ BeltMovementExecutionSystem
→ BeltMovementState / GridPosition / LocalTransform
→ Synchronization
→ 다음 프레임 Spatial Index
```

## 4. 핵심 평가 항목

- Decision이 Persistent 이동 상태를 직접 변경하지 않는지
- Execution이 Decision을 다시 판단하지 않는지
- Backpressure 및 ItemSpacing 규칙
- 다중 타일 hop과 DeltaTime clamp
- 결정론
- `ComponentLookup` read와 Query ref/write 범위
- `ScheduleParallel` 안전성
- Spatial Index stale-window가 의도된 Phase 계약인지
- 불필요한 Request/Structural Change 유무
- 대량 Item 증가 시 알고리즘 비용

## 5. 선행 평가 단계

- 3단계 Spatial Index
- 4단계 Item Ownership

## 6. 다음 단계에 전달해야 할 정보

- BeltMovementState와 BeltMovementDecision의 정확한 의미
- Item이 “벨트 종단에 도달했다”는 판정 기준
- 다음 타일 수용 가능성 판단 기준
- 이동 시 GridPosition이 변경되는 시점
- Storage/Router가 사용할 수 있는 Belt 이동 공개 계약

## 7. 코드 읽기 범위 제한

Storage 입고, Splitter/Merger의 라우팅, Building Output은 이 단계에서 평가하지 않는다.

---

# 단계 6 — Storage / Building Item Input·Output Pipeline

## 1. 평가 목적

- Belt와 Storage/생산 건물 사이의 입고·출고 흐름 전체를 확인한다.
- Decision → Reservation → StateApply → Ownership 전환의 책임 분리를 평가한다.
- 슬롯/스택/필터/동시 입고 경합의 정합성을 확인한다.
- Storage Buffer와 ItemOwnership의 관계가 Item Domain 계약을 준수하는지 확인한다.

## 2. 주요 대상 코드

### Component / Buffer

- `Storage`
- `StorageFilter`
- `FixedBitSet`
- `StoredItemElement`
- `ProductItemElement`
- `BuildingItemInputDecision`
- `BuildingItemOutputDecision`

### System

- `BuildingItemInputDecisionSystem`
- `BuildingStorageInputReservationSystem`
- `StorageItemOutputDecisionSystem`
- `ProductItemOutputDecisionSystem`
- `BuildingItemStorageApplySystem`

### 연결 지점

- `ItemOwnershipApplySystem`은 Stage 4 계약만 확인
- `BeltDestinationReservationSystem`은 **Building Output 후보가 공유 경합에 참가하는 인터페이스까지만 확인**한다. 전체 arbitration의 주 평가는 Stage 8에서 수행한다.

## 3. 확인할 데이터 흐름

입고:

```text
Belt End Item
→ BuildingItemInputDecisionSystem
→ BuildingItemInputDecision
→ BuildingStorageInputReservationSystem
→ TargetSlotIndex 확정
→ BuildingItemStorageApplySystem
→ StoredItemElement 추가
→ TransferOwnershipRequest
→ ItemOwnershipApplySystem
```

출고:

```text
StoredItemElement / ProductItemElement
→ StorageItemOutputDecisionSystem / ProductItemOutputDecisionSystem
→ BuildingItemOutputDecision
→ 공유 Belt 목적지 Reservation
→ BuildingItemStorageApplySystem
→ Buffer 제거 + World 이동 상태 복원
→ TransferOwnershipRequest(Entity.Null)
→ ItemOwnershipApplySystem
```

## 4. 핵심 평가 항목

- Storage와 Item Ownership의 책임 분리
- 슬롯 예약의 원자성
- 필터와 Stack 한도
- 여러 Item의 마지막 Slot 경합
- Input/Output Decision의 Enable/Disable 수명주기
- BufferLookup 쓰기 경합
- 단일 워커 Job이 필요한 이유와 병렬화 가능 범위
- 출고 대상 Belt 재검증
- Product buffer와 Storage buffer의 공통/차이 계약
- 공유 Belt 목적지 경합과 연결되는 경계
- 오류/대상 소멸 시 상태 복구

## 5. 선행 평가 단계

- 3단계 Spatial Index
- 4단계 Item Lifecycle / Ownership
- 5단계 Belt Movement

## 6. 다음 단계에 전달해야 할 정보

- Building Input/Output Decision의 Producer/Consumer
- Storage 슬롯 예약 Owner
- StoredItemElement / ProductItemElement의 의미 차이
- 입고/출고 시 ItemOwnership 전환 순서
- 생산 시스템이 재사용할 수 있는 Input/Output 계약
- Router와 공유하는 Belt 목적지 경합 입력 형식

## 7. 코드 읽기 범위 제한

Miner/Crafter가 Product buffer를 어떻게 만드는지는 Stage 7에서 평가한다. Splitter/Merger arbitration 내부는 Stage 8에서 평가한다.

---

# 단계 7 — Mining / Crafting Production Pipeline

## 1. 평가 목적

- Miner와 Crafter가 V2의 공통 생산 패턴을 얼마나 일관되게 따르는지 확인한다.
- 생산 조건 판단, 진행 상태, 재료 소비, 결과 생성, 외부 출력의 책임 경계를 확인한다.
- Recipe/Item Registry의 ReadOnly 데이터와 런타임 Persistent State의 역할을 구분한다.
- 생산 결과가 Item Lifecycle과 Storage Output 경계를 올바르게 재사용하는지 확인한다.

## 2. 주요 대상 코드

### Miner

- `MinerState`
- `MinerDecision`
- `MinerDecisionSystem`
- `MinerExecutionSystem`
- `ResourceNode` 및 ResourceSpatialIndex는 인터페이스만 확인

### Crafter

- `CrafterState`
- `CrafterDecision`
- `CrafterStateDecision`
- `ChangeCrafterRecipeRequest`
- `CrafterRecipeCommandSystem`
- `CrafterDecisionSystem`
- `CrafterExecutionSystem`
- `CrafterStateApplySystem`

### 공통 생산 데이터

- `ProductResult`
- `RecipeRegistry` / Recipe Blob 계약
- `ItemRegistry` 계약
- `ProductItemElement`
- `ItemLifecycleApplySystem`의 ProductResult 소비 부분
- `ProductItemOutputDecisionSystem`은 Stage 6 결과를 참조

## 3. 확인할 데이터 흐름

Miner:

```text
ResourceSpatialIndex + MinerState + Product Buffer
→ MinerDecisionSystem
→ MinerDecision
→ MinerExecutionSystem
→ MinerState Progress / Resource 상태 / ProductResult
→ ItemLifecycleApplySystem
→ ProductItemElement
→ Building Output Pipeline
```

Crafter:

```text
ChangeCrafterRecipeRequest
→ CrafterRecipeCommandSystem
→ CrafterState / StorageFilter / 잔여재료 처리

StoredItemElement + RecipeRegistry + CrafterState
→ CrafterDecisionSystem
→ CrafterDecision + CrafterStateDecision
→ CrafterExecutionSystem
→ 재료 소비 / Progress / ProductResult
→ CrafterStateApplySystem + ItemLifecycleApplySystem
→ ProductItemElement
→ Building Output Pipeline
```

## 4. 핵심 평가 항목

- Decision / Execution / StateApply 책임 분리
- Miner와 Crafter의 Persistent State Owner
- Crafter Command가 직접 수정해도 되는 상태 범위
- 재료 선소비와 DestroyItemRequest 계약
- ProductResult의 Frame Result 수명주기
- Output Backpressure
- Recipe 변경 중 진행 상태·잔여 재료 처리
- ResourceNode 변경/파괴 책임
- Job 병렬화 가능성 및 현재 단일 워커 사용 이유
- ComponentLookup / Buffer write 경합
- DeltaTime 정책
- 생산 시스템 간 패턴의 일관성과 불필요한 중복

## 5. 선행 평가 단계

- 2단계 Config / Registry
- 3단계 Resource Spatial 계약
- 4단계 Item Lifecycle
- 6단계 Building Item Transfer

## 6. 다음 단계에 전달해야 할 정보

- Miner / Crafter 각각의 State Owner
- ProductResult의 Producer/Consumer와 소비 시점
- 생산물이 Belt로 나가는 공통 경로
- Recipe 변경의 권위 있는 Command 경로
- 생산 과정에서 Item/Resource Domain에 요구하는 공개 계약

## 7. 코드 읽기 범위 제한

World Resource 생성 알고리즘은 Stage 9에서, Storage 슬롯 알고리즘은 Stage 6 결과를 사용하고 다시 전수 평가하지 않는다.

---

# 단계 8 — Splitter / Merger Routing 및 공유 Belt 목적지 경합

## 1. 평가 목적

- Splitter/Merger의 라우팅 상태와 Frame Decision의 책임 분리를 확인한다.
- Building Output과 Router가 같은 Belt 입구를 사용할 때 Reservation이 경합을 일관되게 해결하는지 확인한다.
- PlacementStamp 기반 연결 우선순위와 Cursor 순환 규칙의 결정론을 평가한다.
- Routing Apply가 Item 상태와 Router Persistent State를 어느 범위까지 수정하는지 확인한다.

## 2. 주요 대상 코드

### Component / Utility

- `SplitterRoutingState`
- `MergerRoutingState`
- `RoutingTransferDecision`
- `PlacementStamp`
- `RoutingDirectionUtility`

### System

- `SplitterDecisionSystem`
- `MergerDecisionSystem`
- `BeltDestinationReservationSystem` — **이 단계가 주 평가 단계**
- `RoutingApplySystem`

### 연결 지점

- `BuildingItemOutputDecision`
- Belt / Item Spatial Index 계약
- `BeltMovementState`
- Building Output의 상세 생성 규칙은 Stage 6 결과 사용

## 3. 확인할 데이터 흐름

```text
Belt / Item Spatial Index
+ SplitterRoutingState / MergerRoutingState
+ PlacementStamp
→ SplitterDecisionSystem / MergerDecisionSystem
→ RoutingTransferDecision
        │
BuildingItemOutputDecision ─┤
        ▼
BeltDestinationReservationSystem
→ 동일 Target Belt 후보 arbitration
→ 승인 Decision만 유지
→ RoutingApplySystem 또는 BuildingItemStorageApplySystem
→ Item 위치/이동 상태 반영
→ Router Cursor/기준 연결 상태 반영
→ Decision Disable
```

## 4. 핵심 평가 항목

- Router Persistent State Owner
- Decision이 Persistent Routing State를 수정하지 않는지
- shared destination arbitration의 단일 책임
- PlacementStamp와 tie-break의 결정론
- 같은 대상에 대한 후보 수집 자료구조
- 단일 워커 Reservation의 필요성과 확장성
- Work-conserving 라우팅 규칙
- Cursor 진행 시점
- 연결 변경/대상 소멸 처리
- ItemOwnership 침범 여부
- RoutingTransferDecision의 Enableable lifecycle
- Building Output과 Router 사이 정책 일관성

## 5. 선행 평가 단계

- 3단계 Spatial Index
- 4단계 Item Lifecycle
- 5단계 Belt Movement
- 6단계 Building Item Output

## 6. 다음 단계에 전달해야 할 정보

- Splitter/Merger 기준 Belt 선택 규칙
- Cursor 및 PlacementStamp 의미
- 공유 Target Belt arbitration 규칙
- Router가 직접 변경하는 Item 상태 범위
- Building Lifecycle이 Splitter/Merger 생성 시 반드시 부착해야 할 컴포넌트 목록

## 7. 코드 읽기 범위 제한

Belt 일반 이동 알고리즘과 Storage Output 선택 알고리즘은 각각 Stage 5/6 결과를 사용한다.

---

# 단계 9 — Chunk / Resource / Floor Generation Pipeline

## 1. 평가 목적

- Chunk 요청의 Pending → 생성 → 완료 확정 수명주기를 확인한다.
- Seed 기반 Resource/Floor 생성이 청크 로드 순서와 무관하게 결정론적인지 확인한다.
- Resource Entity 생성과 ResourceSpatialIndex 게시 시점을 확인한다.
- Persistent NativeContainer tracker와 ECB 기반 생성 사이의 경계를 평가한다.

## 2. 주요 대상 코드

### World / Resource Component

- `ResourceGenerationSettings`
- `ResourceGenerationConfigElement`
- `FloorGenerationSettings`
- `FloorBiomeElement`
- `FloorVariantElement`
- `ChunkLoadRequestQueue`
- `ChunkLoadRequestElement`
- `GeneratedChunkTracker`
- `GeneratedChunkReadyElement`
- `GeneratedChunkCompletedElement`
- `ResourceNode`
- `ResourceConfig`

### Utility

- `ChunkUtility`
- `ResourceGenerationUtility`
- `FloorBiomeSampler`

### System

- `InitialChunkLoadBootstrapSystem`
- `ChunkLoadCommandSystem`
- `ResourceGenerationCommandSystem`
- `ResourceSpatialSyncSystem`

### 데이터 공급 연결

- `WorldGenerationConfigLoader`
- `WorldGenerationConfigLoadSystem`
- `ResourcePrefabDatabaseAuthoring`
- Stage 2에서 이미 평가한 Managed/Baker 구조는 재평가하지 않는다.

## 3. 확인할 데이터 흐름

```text
Initial / External Chunk Request
→ ChunkLoadRequestElement
→ ChunkLoadCommandSystem
→ GeneratedChunkTracker.Pending
→ GeneratedChunkReadyElement
→ ResourceGenerationCommandSystem
→ EndStateApply ECB: Resource Entity Instantiate
→ GeneratedChunkCompletedElement
→ 다음 Command의 ChunkLoadCommandSystem
→ Pending 제거 + Generated Map 확정
→ ResourceSpatialSyncSystem
→ ResourceSpatialIndex
```

Floor:

```text
WorldSeed + FloorGenerationSettings + World Cell
→ FloorBiomeSampler
→ deterministic FloorTileSelection
```

## 4. 핵심 평가 항목

- Chunk lifecycle Owner
- Pending/Completed 상태의 중복 Source of Truth 여부
- ECB 실제 반영 전 완료 처리 방지
- NativeParallelHashSet 수명주기
- 청크 중복 요청 idempotency
- 생성 실패/Prefab 준비 대기
- 결정론 및 음수 좌표
- Resource 생성의 Structural Change 규모
- ResourceSpatialIndex 동기화 시점
- Floor sampler의 순수성
- 메인 스레드 의존성과 대량 청크 요청 확장성

## 5. 선행 평가 단계

- 1단계 실행/ECB 경계
- 2단계 World/Prefab 설정 공급
- 3단계 Resource Spatial Index

## 6. 다음 단계에 전달해야 할 정보

- 생성 완료 청크의 Source of Truth
- Pending 해제 시점
- ResourceNode 생성 및 파괴 후 Spatial 반영 시점
- Miner가 신뢰할 ResourceSpatialIndex 계약
- 월드 Seed 기반 결정론 규칙

## 7. 코드 읽기 범위 제한

카메라 기반 청크 요청, 실제 Floor 렌더링, Biome Presentation은 현재 런타임 구현 범위 밖이므로 평가하지 않는다. `V2FloorBiomePreview`는 필요 시 Floor sampler 확인용 Debug 도구로만 본다.

---

# 단계 10 — Construction / Building Lifecycle End-to-End

## 1. 평가 목적

- 건물 배치 요청부터 ConstructionSite, 자재 수령, 취소/완공, 최종 Building 생성까지 전체 lifecycle을 평가한다.
- PlacementStamp, 공간 점유, BuildingConfig, Prefab, Item material의 소유권 경계를 확인한다.
- 현재 통합된 ConstructionLifecycleApplySystem의 내부 순서와 원자성을 확인한다.
- 최종 건물 생성 경로가 타입별 필수 컴포넌트를 일관되게 초기화하는지 확인한다.

## 2. 주요 대상 코드

### Placement / Construction Component

- `BuildingPlacementRequest`
- `PlacementRequestCandidateElement`
- `PlacementFlags`
- `PlacementValidationCode`
- `PlacementValidationResult`
- `PlacementCandidate`
- `ConstructionSite`
- `ConstructionSiteFlags`
- `ConstructionMaterialRequirementElement`
- `PlacementStamp`

### Lifecycle Request

- `SpawnBuildingRequest`
- `SupplyConstructionMaterialRequest`
- `CancelConstructionRequest`

### Utility

- `BuildingPlacementValidationUtility`
- `BuildingLifecycleUtility`
- `BuildingConfigLookupUtility`
- `PrefabLookupUtility`

### System

- `BuildingPlacementCommandSystem`
- `ConstructionLifecycleApplySystem`
- `BuildingLifecycleApplySystem`
- `BuildingSpatialSyncSystem`은 Stage 3 계약 참조

### 데이터 계약

- `BuildingType`
- `BuildingFootprint`
- `BuildingConfigElement`
- `BuildingConstructionMaterialElement`
- 타입별 생성 대상:
  - Belt
  - Storage
  - Miner
  - Crafter
  - Splitter
  - Merger
  - 현재 단순 placeholder Building 타입

## 3. 확인할 데이터 흐름

배치:

```text
BuildingPlacementRequest + Candidate Buffer
→ BuildingPlacementCommandSystem
→ BuildingPlacementValidationUtility
→ Batch 승인/거부
→ ConstructionSite + PlacementStamp + Requirements + Stored Buffer
→ EndStateApply ECB
→ BuildingSpatialSync
```

자재:

```text
SupplyConstructionMaterialRequest
→ ConstructionLifecycleApplySystem
→ Requirement Delivered/Reserved 갱신
→ Site Progress
→ Item 보관 상태 + StoredItemElement
→ Request Consume
```

취소:

```text
CancelConstructionRequest
→ Cancelled 상태 확정
→ 도착 자재 반환
→ Site Destroy
→ Spatial Sync
```

완공:

```text
ConstructionSite
+ 모든 Requirement 충족
→ ConstructionLifecycleApplySystem
→ 자재 소비
→ BuildingLifecycleUtility.SpawnBuilding
→ 타입별 필수 Component 초기화
→ Site Destroy
→ EndStateApply Playback
→ BuildingSpatialSync
```

독립 Building Spawn 경로:

```text
SpawnBuildingRequest
→ BuildingLifecycleApplySystem
→ BuildingLifecycleUtility.SpawnBuilding
→ Final Building
```

## 4. 핵심 평가 항목

- Placement batch 원자성
- 동일 프레임 공간 선점
- BuildingSpatialIndex가 예약/점유 Source of Truth인지 파생 데이터인지
- PlacementStamp 발급과 보존
- ConstructionSite lifecycle Owner
- Cancel → Material → Completion 처리 우선순위
- 자재 Delivered/Reserved 정합성
- Construction이 ItemOwnership State Owner 계약을 어떻게 사용하는지
- 완료와 취소/자재 도착의 같은 프레임 경합
- SpawnBuildingRequest 경로와 Construction 직접 Spawn 경로의 일관성
- BuildingLifecycleUtility의 책임 크기와 타입 확장성
- Prefab 환경 Strict Fail / 테스트 Fallback
- Structural Change 원자성
- ECB Playback 전후 점유 공백 여부
- type-specific Component 누락·중복
- main-thread NativeArray 복사 및 대량 batch 확장성
- 미래 Building 타입 placeholder가 현재 코어를 불필요하게 복잡하게 하는지 여부

## 5. 선행 평가 단계

- 2단계 Config / Authoring
- 3단계 Spatial Index
- 4단계 Item Ownership
- 5단계 Belt 계약
- 6단계 Storage 계약
- 7단계 Miner/Crafter Component 계약
- 8단계 Splitter/Merger Component 계약

## 6. 다음 단계에 전달해야 할 정보

- Building Placement 및 Construction의 State Owner
- 공간 예약/점유가 확정되는 정확한 시점
- PlacementStamp의 생성→현장→최종 건물 전달 계약
- Construction material Item ownership 처리 방식
- 건물 생성의 단일/복수 진입 경로
- 각 현재 구현 Building 타입의 필수 Component 집합
- Lifecycle에서 ECB와 직접 Component write가 사용되는 경계

## 7. 코드 읽기 범위 제한

Power/Drone/Research의 런타임 동작은 평가하지 않는다. BuildingLifecycleUtility에서 해당 타입이 placeholder로 존재하는 사실만 확인한다.

---

# 단계 11 — Validation / Test Architecture 및 품질 안전망

## 1. 평가 목적

- 앞 단계에서 확인한 핵심 불변식이 자동 테스트나 Runtime/Development Invariant로 실제 보호되는지 확인한다.
- 테스트가 내부 구현을 과도하게 복제하거나 실제 ECS Phase를 우회하지 않는지 확인한다.
- Unit / Contract / Integration / End-to-End 테스트의 역할 분포를 확인한다.
- 테스트를 통과하더라도 발견하지 못할 구조적 공백을 식별한다.

## 2. 주요 대상 코드

### Validation

- `WorldInvariantValidationSystem`

### Test Support

- `EcsWorldTestFixture`
- `TestEntityFactory`
- `TestSimulationDriver`

### 주요 테스트 묶음

- Phase 1 Item Integration
- Phase 2 Belt Decision / Execution / Integration
- Phase 3 Storage / Building IO / Spatial / Config
- Phase 4 Miner / Resource / Chunk / End-to-End
- Phase 5 Crafter / Recipe / Recipe Change
- Phase 6 Routing / Reservation / Splitter / Merger / End-to-End
- Phase 7 Placement / Construction / Building Lifecycle

개별 테스트 파일은 해당 평가 질문과 직접 연결되는 경우에만 읽는다.

## 3. 확인할 데이터 흐름

테스트 관점:

```text
Test Setup
→ 실제 Component / Request 구성
→ 실제 V2 Phase 또는 대상 System 실행
→ ECB Playback / Sync
→ 최종 상태 및 Invariant 단언
```

Runtime 검증:

```text
모든 Phase 완료
→ SynchronizationGroup OrderLast
→ Spatial Fence 완료
→ WorldInvariantValidationSystem
→ Source of Truth ↔ Buffer/Index/Decision 정합성 확인
```

## 4. 핵심 평가 항목

- 테스트가 공개 계약을 검증하는지 내부 구현을 복제하는지
- 실제 Phase 순서 사용 여부
- 테스트 전용 추가 Playback으로 실제 결함이 숨겨질 가능성
- Request 소비 및 Enableable lifecycle 검증
- Source of Truth ↔ Spatial/Buffer 양방향 invariant
- 경합, 실패, 취소, 대상 소멸 테스트
- 결정론 테스트
- Job/ECB 완료 시점 테스트
- Construction 최신 통합 구조와 테스트의 일치
- WorldInvariantValidationSystem의 커버리지
- Validation 자체가 Development 성능을 과도하게 왜곡하는지와 제품 성능 문제의 구분
- 중요한 경계에 대한 회귀 테스트 누락 여부

## 5. 선행 평가 단계

- 1~10단계 전체

## 6. 다음 단계에 전달해야 할 정보

- 각 도메인에서 자동으로 보호되는 Invariant
- 테스트만 존재하고 Runtime Validation은 없는 계약
- Runtime Validation은 있지만 테스트가 없는 계약
- End-to-End로 실제 검증된 파이프라인
- 통합 평가 시 신뢰할 수 있는 검증 증거와 아직 미검증인 부분

## 7. 코드 읽기 범위 제한

테스트 파일 전체를 처음부터 모두 읽지 않는다. 앞 단계에서 나온 “핵심 계약 / 위험 지점 / 미확인 항목”을 기준으로 관련 테스트만 선택한다.

---

# 단계 12 — 전체 프로젝트 통합 아키텍처 평가

## 1. 평가 목적

앞 단계의 결과를 합쳐, 개별 파이프라인에서는 보이지 않는 프로젝트 전체 구조의 일관성과 확장성을 평가한다.

이 단계에서는 새로운 대규모 코드 탐색보다 **1~11단계 결과 비교와 충돌 검증**을 우선한다.

## 2. 주요 대상 코드

주 평가 대상은 코드 파일이 아니라 **1~11단계의 확정 결과와 handoff 정보**다.

코드는 아래 경우에만 필요한 부분을 다시 연다.

- 두 단계의 State Owner 결론이 충돌할 때
- 같은 Component를 서로 다른 Domain이 쓰는 것으로 확인될 때
- Phase/ECB 시점 해석이 단계별로 다를 때
- 동일 문제를 서로 다른 방식으로 처리하는 경로가 있을 때
- 성능 병목 후보가 여러 파이프라인의 상호작용으로 발생할 때

## 3. 확인할 전체 데이터 흐름

현재 구현된 대표 Vertical Slice를 연결한다.

```text
Config / Authoring
→ World / Resource 생성
→ Building Placement / Construction
→ Miner / Crafter
→ Item Spawn / Ownership
→ Belt Movement
→ Storage / Building IO
→ Splitter / Merger Routing
→ Synchronization / Spatial Index
→ 다음 프레임 Decision
```

## 4. 핵심 평가 항목

반드시 다음을 검토한다.

- 개별적으로 적절하지만 전체 구조에서 충돌하는 설계
- State Owner가 프로젝트 전체에서 일관적인지
- Source of Truth가 Domain 경계를 넘어 중복되지 않는지
- Decision / Reservation / Execution / StateApply 규칙의 일관성
- 동일한 종류의 Request가 서로 다른 lifecycle 규칙을 쓰는지
- IEnableableComponent의 의미와 사용 방식이 일관적인지
- 같은 문제를 서로 다른 System이 서로 다른 방식으로 해결하는지
- 직접적인 System 간 결합이 남아 있는지
- 공통화할 가치가 있는 반복 구조
- 지나친 공통화로 책임이 불명확해진 구조
- ComponentLookup / BufferLookup Write가 전역 병렬성을 막는 지점
- 단일 워커 Job이 실제 도메인 정합성 때문에 필요한지, 구현 편의상 남은 것인지
- EndStateApply ECB가 Structural Change를 일관되게 수용하는지
- 불필요한 Sync Point 또는 Main Thread 경로
- NativeContainer와 Fence 수명주기의 전역 일관성
- Spatial Index rebuild 전략의 전체 비용 특성
- Building/Item/Production/Construction 사이의 수명주기 충돌
- 결정론과 PlacementStamp 같은 우선순위 정책의 일관성
- 시스템/콘텐츠 수 증가 시 예상되는 확장 문제
- 테스트와 Invariant가 핵심 아키텍처 경계를 충분히 보호하는지

## 5. 선행 평가 단계

- 1~11단계 전체

## 6. 최종 산출물에 포함할 내용

- 현재 Architecture V2의 실제 구조 요약
- 잘 유지되고 있는 핵심 설계 원칙
- 구조적 문제 목록
- 문제별 영향 범위
- 문제의 원인 Domain과 실제 수정 책임 위치
- 단계 간 충돌로 발견된 문제
- 성능상 “코드 구조로 확인된 위험”과 “Profiler 확인이 필요한 가설”의 분리
- 확장성 위험
- 개선 우선순위 제안
- 수정 전 반드시 합의가 필요한 게임 규칙/아키텍처 결정
- 현재 구현 범위 밖이라 평가하지 않은 영역

이 단계에서도 **코드 수정은 수행하지 않는다.**

---

# 평가 단계 간 중복 방지 기준

같은 코드가 여러 파이프라인의 연결점인 경우 다음을 주 평가 단계로 지정한다.

| 코드/책임 | 주 평가 단계 | 다른 단계에서 보는 범위 |
| --- | --- | --- |
| V2 SystemGroup / EndStateApply ECB | 1 | 실행 순서 계약만 참조 |
| Config / Blob / Prefab publication | 2 | 소비 방식만 확인 |
| Spatial Index / Fence / Sync System | 3 | 조회 계약만 참조 |
| ItemLifecycleApply / ItemOwnershipApply | 4 | Domain이 요청을 올바르게 만드는지만 확인 |
| BeltMovement Decision/Execution | 5 | Belt 종단/입구 계약만 참조 |
| Storage Input/Output 및 BuildingItemStorageApply | 6 | 생산/Router의 연결 지점만 확인 |
| Miner/Crafter 내부 생산 로직 | 7 | Product buffer 결과 계약만 참조 |
| BeltDestinationReservation / Router | 8 | Building Output 참가 인터페이스만 Stage 6에서 확인 |
| Chunk lifecycle / Resource generation | 9 | Miner는 ResourceSpatial 공개 계약만 사용 |
| BuildingPlacement / Construction / BuildingLifecycle | 10 | 타입별 Component 계약은 이전 단계 결과 사용 |
| WorldInvariantValidation / TestSupport | 11 | 앞 단계에서는 관련 테스트 존재 여부만 기록 |
| 전역 비교·공통화 판단 | 12 | 세부 알고리즘 재평가 금지 |

---

# 각 Codex 세션에 전달할 최소 입력

후속 평가에서 토큰 낭비를 줄이기 위해 매 단계 세션에는 다음만 전달한다.

```text
현재 프로젝트의 PROJECT_EVALUATION_PLAN.md에서 "단계 N"을 수행하라.

규칙:
- 현재 체크아웃 코드만 기준으로 평가한다.
- 해당 단계의 주요 대상 코드부터 읽는다.
- 선행 단계 handoff가 있다면 먼저 읽는다.
- 필요하지 않은 프로젝트 전체 재탐색은 하지 않는다.
- 코드 수정은 하지 않는다.
- 구조적 품질을 우선 평가한다.
- 정상 설계는 정상이라고 명시한다.
- 성능 문제와 성능 가설을 구분한다.
- 마지막에 "다음 단계 전달 정보"를 별도 섹션으로 정리한다.
```

선행 평가 결과를 파일로 보관한다면 각 결과 문서의 마지막에는 아래 고정 형식을 권장한다.

```text
## 다음 단계 전달 정보

- 확정된 State Owner:
- 확정된 Source of Truth:
- Phase / 실행 순서:
- 공개 데이터 계약:
- 다른 단계에서 다시 평가하지 않아도 되는 사항:
- 후속 단계가 반드시 재확인해야 하는 사항:
- 미확인 사항:
```

이렇게 하면 다음 Codex 세션이 이전 단계의 전체 분석 내용을 다시 읽지 않고도 필요한 계약만 이어받을 수 있다.

---

# 평가 완료 기준

프로젝트 전체 평가는 다음 조건을 충족했을 때 완료로 본다.

1. 1~11단계가 각각 독립적으로 평가되었다.
2. 각 단계가 명확한 State Owner / Source of Truth / 데이터 흐름을 남겼다.
3. 기능별 Job/Burst/Lookup/ECB/Structural Change 특성이 해당 기능 맥락에서 검토되었다.
4. 테스트와 Invariant가 실제 핵심 계약과 대조되었다.
5. 12단계 통합 평가가 앞 단계 결과를 기반으로 수행되었다.
6. 미래 미구현 기능을 현재 품질 평가 결과에 섞지 않았다.
7. 실제 코드 근거가 없는 성능·확장성 추정을 사실처럼 기록하지 않았다.
8. 평가 과정에서 코드를 수정하지 않았다.
