# Architecture V2 Authoring & Prefab Database Common Contract

**문서 버전**: 1.0  
**상태**: 확정 (Approved)  
**적용 대상**: SubScene Authoring, Baker, Prefab Database, 런타임 스폰 시스템 (Item, Building, Resource, Drone)

---

## 1. 목적과 기본 원칙

본 문서는 Unity SubScene 베이킹 환경과 Architecture V2 런타임 ECS 환경을 연결하는 **Authoring 및 프리팹 데이터베이스의 공통 규약(Common Contract)**을 정의한다.

1. **상태 소유권의 엄격한 분리 (Baker vs Spawn Owner)**:
   - Baker는 정적 불변 데이터(정적 타입 식별자, Footprint 크기 등)만 엔티티 프리팹에 베이킹한다.
   - 런타임 가변 상태(소유권 `ItemOwnership`, 공간 좌표 `GridPosition`, 이동 진행도 `BeltMovementState`, 1회성 Request 컴포넌트 등)는 Baker가 미리 부착하지 않으며, 실제 생성을 소유하는 런타임 시스템이 인스턴스화 직후 초기화한다.
2. **도메인별 모듈화 (Domain-Separated Prefab Databases)**:
   - 모든 프리팹을 하나의 거대한 엔티티에 섞지 않고, 도메인별 독립 엔티티(`Building`, `Item`, `Resource`, `Drone`)로 베이킹하여 관심사를 분리하고 불필요한 컴포넌트 결합을 방지한다.
3. **무중단 안전성 (Safe Fallback Policy)**:
   - 특정 타입의 프리팹이 미등록/누락된 상태에서 스폰 요청이 발생해도 시뮬레이션 전체가 중단되거나 댕글링 포인터가 생기지 않도록, 순수 엔티티 아키타입(무렌더링 시뮬레이션 전용 엔티티)으로 안전하게 대체 생성한다.

---

## 2. 도메인별 프리팹 데이터베이스 구조

SubScene 베이킹을 통해 월드에 생성되는 4대 프리팹 데이터베이스 구조는 다음과 같다:

### ① `BuildingPrefabElement` (`BuildingPrefabDatabase`)
- **역할**: 건물 배치/건설 시 참조할 원본 프리팹 엔티티와 물리적 Footprint 크기 정보 보관.
- **버퍼 요소**:
  ```csharp
  [InternalBufferCapacity(16)]
  public struct BuildingPrefabElement : IBufferElementData
  {
      public BuildingTypeEnum Type;
      public Entity Prefab;
      public int2 FootprintSize;
  }
  ```

### ② `ItemPrefabElement` (`ItemPrefabDatabase`)
- **역할**: 월드 아이템 스폰 시 참조할 시각 렌더 프리팹 엔티티 매핑.
- **버퍼 요소**:
  ```csharp
  [InternalBufferCapacity(16)]
  public struct ItemPrefabElement : IBufferElementData
  {
      public ItemTypeEnum Type;
      public Entity Prefab;
  }
  ```

### ③ `ResourcePrefabElement` (`ResourcePrefabDatabase`)
- **역할**: 맵 자원 노드 생성 시 참조할 자원 노드 프리팹 엔티티 매핑.
- **버퍼 요소**:
  ```csharp
  [InternalBufferCapacity(8)]
  public struct ResourcePrefabElement : IBufferElementData
  {
      public ItemTypeEnum ResourceType;
      public Entity Prefab;
  }
  ```

### ④ `DronePrefab` (`DronePrefabDatabase`)
- **역할**: 활성 비행 드론 스폰 시 참조할 단일 드론 프리팹 싱글톤.
- **컴포넌트**:
  ```csharp
  public struct DronePrefab : IComponentData
  {
      public Entity Prefab;
  }
  ```

---

## 3. 상태 소유권 및 생명주기 경계

| 구분 | Baker (SubScene Authoring) | Spawn Owner System (런타임) |
| :--- | :--- | :--- |
| **소유 책임** | 정적 에셋 변환, 프리팹 DB 엔티티/버퍼 게시 | 엔티티 인스턴스화, 런타임 상태 주입, 생명주기 관리 |
| **포함 컴포넌트** | `Prefab`, `BuildingType`, `ItemIdentity`, `BuildingFootprint`, 렌더러 컴포넌트 | `GridPosition`, `Direction`, `ItemOwnership`, `BeltMovementState`, 버퍼, `IRequestComponent` |
| **배제 컴포넌트** | `ItemOwnership`, `BeltMovement*`, `StoredItemElement`, 각종 Request 등 | Baker가 설정한 정적 프리팹 원본 데이터 |
| **스폰 주체** | - | `ItemLifecycleApplySystem` (Item)<br>`ConstructionApplySystem` (Building)<br>`ResourceMapGeneratorSystem` (Resource)<br>`DroneSpawnSystem` (Drone) |

---

## 4. `TransformUsageFlags` 표준 가이드라인

Unity Entities 1.0+의 하이브리드 트랜스폼 베이킹 정책에 따라, 엔티티 특성에 맞는 `TransformUsageFlags`를 명확히 지정한다:

1. **`TransformUsageFlags.Dynamic` (이동 엔티티)**:
   - **적용 대상**: `Item` (벨트 이동), `Drone` (자유 비행).
   - **이유**: 매 프레임 시뮬레이션 또는 렌더링에 의해 월드 좌표/로컬 트랜스폼(`LocalTransform`)이 갱신되어야 함.
2. **`TransformUsageFlags.Renderable` (정적 렌더링 엔티티)**:
   - **적용 대상**: `Building` (건물), `ResourceNode` (자원 노드), `Belt` (벨트 타일).
   - **이유**: 배치가 완료되면 월드 좌표가 고정되므로 동적 트랜스폼 연산 오버헤드를 제거하고 렌더링에 필요한 최소 트랜스폼만 유지.
3. **`TransformUsageFlags.None` (순수 데이터 엔티티)**:
   - **적용 대상**: 설정(Config) 엔티티, 프리팹 데이터베이스 홀더, 관리 싱글톤.

---

## 5. 프리팹 누락 처리 및 Safe Fallback 계약

1. **프리팹 데이터베이스 조회 실패 처리**:
   - 런타임 시스템이 특정 `ItemType` 또는 `BuildingType`의 프리팹을 조회했을 때 해당 항목이 없거나 `Prefab == Entity.Null`인 경우:
   - `UnityEngine.Debug.LogError`를 통해 누락 사실을 에디터/로그에 명확히 기록.
2. **Safe Fallback 스폰 동작**:
   - 시뮬레이션 연속성을 보장하기 위해 스폰 요청을 일방적으로 버리거나 예외로 멈추지 않고, **순수 시뮬레이션 아키타입(Fallback Archetype)**을 사용하여 엔티티를 생성한다.
   - Fallback 엔티티는 렌더링 메시/스프라이트가 누락될 수 있으나, ECS 시뮬레이션 상태(`ItemIdentity`, `ItemOwnership`, `GridPosition`, 이동, 버퍼 등)를 완벽히 보유하여 시스템 루프를 무결하게 완주한다.
3. **후속 도메인별 구현 연결**:
   - Task 1.7: Item 프리팹 베이킹 및 `ItemPrefabDatabaseAuthoring`
   - Task 4.8: Resource 프리팹 베이킹 및 `ResourcePrefabDatabaseAuthoring`
   - Task 7.3.1: Building 프리팹 베이킹 및 `BuildingPrefabDatabaseAuthoring`
   - Task 9.3.1: Drone 프리팹 베이킹 및 `DronePrefabDatabaseAuthoring`
