# Architecture V2 Authoring & Prefab Database Common Contract

> 현재 코드와의 관계 (2026-10-06): 이 문서는 설계 배경/규약 또는 개발 마일스톤 기록이다. 현재 실행은 Command→Building→Drone→Commit→Synchronization이며 세 ECB 경계를 사용한다. 본문의 전역 6페이즈·이전 심볼·완료 표시는 당시 설계/작업 범위로 읽고, 구현 판단은 [AGENTS.md](../../AGENTS.md)와 [현재 컴포넌트 계약](../CodeMemory/Components/README.md)을 우선한다. 개발 Phase 번호와 런타임 그룹을 구분한다.

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
3. **시작 검증과 명시적 중단**:
   - 필수 프리팹 DB 검증을 통과하기 전에는 게임 시뮬레이션을 실행하지 않는다. 프리팹 DB 부재 fallback은 사용하지 않는다. 현재 정책의 기준은 `AGENTS.md`의 베이킹·프리팹 규칙이다.

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

## 5. 프리팹 누락 처리 및 시작 검증

현재 정책은 [AGENTS.md](../../AGENTS.md)의 베이킹·프리팹 규칙을 따른다. 2026-09-30 사용자 결정으로 DB 부재 시 테스트용 fallback을 포함한 대체 생성 경로를 제거했다.

- 실제 시작은 요청된 SubScene 로딩과 필수 DB 검증 이후 허용한다. 실패는 오류로 중단하며 자동 복구하지 않는다.
- 격리 테스트는 테스트용 프리팹 DB를 명시적으로 구성한다. 시작 관문 테스트와 개별 Phase 테스트의 입력 전제를 구분한다.
- 공통 건물 Spawn의 Null 반환 시 완료 caller는 현장·도착 자재를 보존한다. non-Null은 ECB 기록 결과이며 재생 완료/전체 rollback 보장이 아니다.
- 자원은 원래 fallback이 없었다. 기존 청크별 프리팹 준비 검사는 유지하고, 게임 시작 전 공통 관문에서도 활성 자원의 등록을 검증한다.

