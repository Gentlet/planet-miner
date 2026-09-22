# PlanetMiner Architecture V2 Plan

> 목적: 현재 PlanetMiner의 기능과 도메인 개념은 최대한 재사용하되,  
> **시스템 간 의존성·실행 순서 의존·상태 변경 경로의 복잡성**을 줄이는 방향으로 아키텍처를 새로 설계한다.

---

## 1. 배경

현재 PlanetMiner는 시스템별 역할 자체는 비교적 잘 분리되어 있다.

예를 들어:

- `ChunkMapSystem`은 공간 상태를 관리한다.
- `ItemStorageSystem`은 아이템 소유권 변경을 담당한다.
- Belt / Storage / Miner / Crafter는 각 건물의 동작 규칙을 담당한다.
- Drone은 Planning / Reservation / Scheduling / Dispatch / Movement / Transfer / Recovery 등으로 책임이 나뉘어 있다.

문제는 **책임 분리 자체보다 시스템들이 서로의 실행 시점과 내부 동작을 많이 알아야 한다는 점**이다.

현재 구조에서 특히 부담이 되는 부분:

- System이 다른 System의 public API를 직접 호출한다.
- 호출자가 상대 System의 실행 시점을 알아야 한다.
- `UpdateBefore`, `UpdateAfter`, 직접 호출, ECB playback 시점을 함께 이해해야 한다.
- 하나의 상태 전환이 여러 System의 데이터를 동시에 수정한다.
- 기능 하나를 수정할 때 여러 System을 따라가야 한다.
- Drone의 완료 / 취소 / 실패 경로가 여러 상태와 시스템에 걸쳐 있다.
- 어떤 데이터가 원본(Source of Truth)이고 어떤 데이터가 파생 상태인지 불명확해질 가능성이 있다.

Architecture V2의 목적은 이 문제를 구조적으로 줄이는 것이다.

---

# 2. Architecture V2 목표

## 2.1 핵심 목표

Architecture V2는 다음을 달성해야 한다.

### 1. 시스템 간 직접적인 결합 감소

한 System을 수정하기 위해 다른 여러 System의 내부 구현을 이해해야 하는 상황을 줄인다.

### 2. 실행 흐름 단순화

개별 System마다 복잡한 실행 순서를 외우는 대신 큰 처리 단계만 이해하면 되도록 한다.

예:

```text
Command
    ↓
Decision
    ↓
Reservation
    ↓
Execution
    ↓
State Apply
    ↓
Synchronization / Cleanup
```

### 3. 상태의 소유자 명확화

각 주요 상태에 대해 실제 변경 책임자를 하나로 정한다.

### 4. 상태 전환 중앙화

특히 Drone Task와 같이 여러 데이터를 함께 정리해야 하는 상태 전환을 한 곳에서 관리한다.

### 5. 변경 영향 범위 축소

예를 들어:

- 창고 규칙 변경 → Storage 영역
- 소유권 규칙 변경 → Item Ownership 영역
- Drone 실패 처리 변경 → Drone Lifecycle 영역
- 공간 점유 규칙 변경 → Spatial 영역

처럼 수정 위치를 예측할 수 있어야 한다.

### 6. DOTS/ECS의 병렬 처리 가능성 유지

구조 단순화를 위해 모든 처리를 중앙 System 하나로 몰지 않는다.

각 영역의 데이터 접근 범위를 명확하게 하여 이후 Job/Burst 최적화를 쉽게 만든다.

---

# 3. Non-Goals

Architecture V2에서 처음부터 해결하지 않을 것:

- 모든 System을 완전히 독립적으로 만들기
- 모든 데이터를 이벤트 기반으로 변경하기
- 모든 상태 변경을 ECB로 처리하기
- 모든 시스템을 병렬 Job으로 만들기
- 현재 기능 전체를 한 번에 V2로 이전하기
- 현재 코드 전체 삭제 후 처음부터 게임 다시 제작
- 성능 추측만으로 구조를 과도하게 최적화하기

Architecture V2의 1차 목표는 **성능 극대화가 아니라 구조적 복잡도 감소**이다.

---

# 4. 핵심 설계 원칙

## 4.1 상태에는 하나의 명확한 Owner가 있다

중요한 상태는 담당 영역을 하나만 둔다.

예:

| 상태 | 책임 영역 |
|---|---|
| Item Ownership | Item Ownership |
| Item Position | Item Movement / Spatial |
| Grid Occupancy | Spatial / ChunkMap |
| Drone Task State | Drone Task Lifecycle |
| Drone Reservation | Drone Reservation |
| Power Network | PowerGrid |
| Production State | 해당 Building Domain |

다른 System은 가능한 한 해당 상태를 직접 수정하지 않는다.

---

## 4.2 System은 다른 System의 lifecycle을 직접 제어하지 않는다

Architecture V2에서는 다음 패턴을 기본 협업 방식으로 사용하지 않는다.

```csharp
var system = World.GetExistingSystemManaged<SomeSystem>();
system.DoSomething();
```

이런 호출이 무조건 금지되는 것은 아니지만 다음 조건을 만족해야 한다.

- 단순한 stateless utility로 대체할 수 없는가?
- 데이터 기반 전달로 표현할 수 없는가?
- 호출자가 상대 System의 update 시점을 알아야 하지 않는가?
- 호출 방향이 순환 의존성을 만들지 않는가?

가능하면 System 간 협력은 ECS 데이터로 표현한다.

예:

```text
StorageSystem
    ↓
Store 의도/결과를 ECS 데이터에 기록
    ↓
Item Ownership 처리 단계
    ↓
실제 Ownership 변경
```

---

## 4.3 System과 Utility를 구분한다

모든 로직을 System으로 만들지 않는다.

### A. Active System

프레임의 특정 시점에 실제로 실행되어야 하는 로직.

예:

- Belt 이동 판단
- Drone movement
- Mining 진행
- State Apply

### B. Utility / Domain Logic

상태를 소유하지 않고 입력을 받아 결과를 계산하는 순수 로직.

예:

- 거리 계산
- 후보 점수 계산
- Recipe 검증
- Drone task suitability 계산

가능하면 static / struct / Burst-compatible 로직으로 유지한다.

### C. State Owner

특정 상태 변경의 정합성을 책임지는 영역.

예:

- Item Ownership
- Drone Reservation
- Drone Lifecycle
- Chunk Spatial State

`OnUpdate()`가 비어 있고 다른 System에서 public method를 호출하기 위해 존재하는 System은
이 세 분류 중 어디에 속해야 하는지 다시 검토한다.

---

# 5. 전체 실행 단계

Architecture V2에서는 세부적인 `A Before B Before C`보다 큰 Phase를 먼저 정의한다.

초안:

```text
Simulation
│
├─ 1. Command Phase
│
├─ 2. Decision Phase
│
├─ 3. Reservation Phase
│
├─ 4. Execution Phase
│
├─ 5. State Apply Phase
│
└─ 6. Synchronization / Cleanup Phase
```

---

## 5.1 Command Phase

외부 요청을 ECS가 처리할 수 있는 상태로 변환한다.

예:

- 건물 배치 요청
- 철거 요청
- Recipe 변경
- Drone task command
- 플레이어 입력

이 단계에서는 가능한 한 실제 복잡한 게임 상태 변경을 하지 않는다.

---

## 5.2 Decision Phase

각 기능 System이 자신의 게임 규칙만 판단한다.

예:

### Belt

```text
이 Item은 이 방향으로 이동할 수 있는가?
얼마나 이동할 수 있는가?
```

### Storage

```text
아이템을 받을 수 있는가?
아이템을 출력할 수 있는가?
어떤 아이템을 출력해야 하는가?
```

### Miner

```text
현재 채굴 가능한가?
생산 완료 조건을 만족했는가?
```

### Crafter

```text
재료가 충분한가?
제작 진행이 가능한가?
```

### Drone Planning

```text
어디에서 아이템을 가져올 것인가?
어디로 전달할 것인가?
```

Decision System은 가능한 한 다른 도메인의 내부 상태를 직접 변경하지 않는다.

---

## 5.3 Reservation Phase

여러 실행 주체가 동일 자원을 동시에 사용하는 것을 방지한다.

대표 대상:

- Drone item reservation
- Destination capacity reservation
- Construction material reservation
- 필요하다면 향후 다른 shared resource 예약

Reservation의 생성/해제/검증은 Reservation Owner가 담당한다.

---

## 5.4 Execution Phase

이미 결정된 작업을 실제로 진행한다.

예:

- Drone 이동
- Belt 이동
- 생산 진행
- Cargo pickup / delivery
- 건설 진행

가능하면 이 단계에서는 "무엇을 할지" 다시 판단하지 않는다.

Decision과 Execution의 책임이 섞이지 않도록 한다.

---

## 5.5 State Apply Phase

여러 도메인에서 요청한 중요한 상태 변경을 실제 ECS 상태에 적용한다.

대표적인 대상:

- Ownership
- Task State Transition
- Reservation update
- spawn / destroy request
- 중요 lifecycle 변경

핵심 원칙:

> 상태를 요청하는 System과 실제 정합성을 유지하며 적용하는 책임을 분리한다.

단, 너무 단순한 로컬 상태까지 무조건 Apply 단계로 보내지는 않는다.

---

## 5.6 Synchronization / Cleanup Phase

파생 데이터를 실제 상태와 맞춘다.

예:

- Item 위치와 spatial index 동기화
- expired reservation cleanup
- completed task cleanup
- lifecycle cleanup
- debug invariant validation

---

# 6. Source of Truth 설계

Architecture V2에서 반드시 명시해야 할 항목이다.

같은 정보를 여러 곳에 보관할 수 있지만 **원본은 하나만 있어야 한다.**

초기 설계안:

| 데이터 | Source of Truth 후보 | 파생/인덱스 후보 |
|---|---|---|
| Item 위치 | Item Position Component | ChunkMap item index |
| Item 소유권 | Ownership Component | Owner Buffer |
| Building 위치 | Building GridPosition | ChunkMap occupancy |
| Drone Task 상태 | DroneTask State | Scheduling candidate index |
| Drone Assignment | Task/Drone assignment state | Candidate cache |
| Drone Reservation | Reservation state | 예약량 합계 |
| Power network | PowerGrid state | range lookup/index |
| Resource 위치 | Resource entity position | Chunk resource index |

주의:

위 표는 **확정 설계가 아니라 V2 구현 전에 검증해야 하는 후보안**이다.

각 항목에서 반드시 결정해야 한다.

```text
1. 누가 쓴다?
2. 누가 읽는다?
3. 누가 수정할 수 있다?
4. 어떤 데이터가 원본인가?
5. 어떤 데이터는 재생성 가능한 cache/index인가?
```

---

# 7. Item Domain 설계

## 7.1 목표

아이템 관련 다른 기능들이 Item 내부 구현을 알지 않아도 되도록 한다.

예:

```text
Storage
Crafter
Miner
Drone
Construction
```

등은 Item Ownership의 실제 변경 절차를 직접 구현하지 않는다.

---

## 7.2 Item Ownership 책임

Item Ownership 영역은 다음 정합성을 책임진다.

예:

```text
Item Owner
Owner Buffer
World Item 여부
Reservation 상태와의 충돌
Consume
Transfer
Store
Restore
```

가능한 규칙:

> Item Ownership 관련 데이터를 직접 수정할 수 있는 곳을 제한한다.

---

## 7.3 Item Position 책임

Ownership과 Position을 같은 개념으로 취급하지 않는다.

예:

```text
World Item
    ↓
Position 존재
    ↓
Spatial index 등록

Stored Item
    ↓
World Position 의미 없음
```

이 관계를 명시적으로 정의한다.

---

# 8. Spatial / ChunkMap 설계

## 8.1 목표

ChunkMap은 공간 인덱스의 주인으로 유지한다.

단, 다른 Domain이 ChunkMap의 내부 자료구조를 알아야 하지 않도록 한다.

---

## 8.2 ChunkMap이 담당할 수 있는 것

- Building occupancy
- Resource spatial index
- World item spatial index
- construction reservation
- spatial range lookup

---

## 8.3 ChunkMap이 담당하지 않을 것

- 생산 진행
- Storage 정책
- Drone task progression
- Power 생산/소비 계산
- Item ownership

---

## 8.4 중요한 규칙

공간 데이터의 "원본"과 "index"를 명확히 구분한다.

예:

```text
Entity Position = Source of Truth
ChunkMap = Query Index
```

또는 필요한 경우 반대 구조를 선택할 수 있다.

단, 둘 다 원본이 되면 안 된다.

---

# 9. Drone Architecture V2

Drone은 V2에서 가장 중요한 리팩터링 대상이지만
첫 번째 구현 대상은 아니다.

현재 역할 분리는 최대한 유지한다.

```text
Planning
Reservation
Scheduling
Dispatch
Movement
Transfer
Recovery
```

문제는 이 역할 분리 자체가 아니라 **상태 전환과 cleanup의 분산**이다.

---

## 9.1 Drone Lifecycle Owner

Architecture V2에서는 Drone Task lifecycle 변경 책임을 명확히 둔다.

예:

```text
Pending
    ↓
Reserved
    ↓
Assigned
    ↓
PickingUp
    ↓
Delivering
    ↓
Completed
```

실패:

```text
Any Active State
    ↓
Failed
    ↓
Recovery / Cleanup
```

취소:

```text
Any Cancelable State
    ↓
Cancelled
    ↓
Cleanup
```

---

## 9.2 상태 전환 요청과 실제 정리 분리

예:

`DroneCargoTransferSystem`은 다음을 직접 모두 처리하지 않는다.

```text
Reservation 해제
Task 상태 변경
Drone assignment 제거
Cargo 초기화
Drone 복귀 상태 설정
Candidate index 수정
```

대신 의미 수준의 결과를 전달한다.

예:

```text
DeliveryCompleted
TaskFailed(reason)
TaskCancelled
PickupCompleted
```

Lifecycle Owner가 transition 규칙에 따라 필요한 정리를 수행한다.

---

## 9.3 Transition Table 작성

Drone 구현 전에 반드시 transition table을 작성한다.

예:

| Current | Event | Next | Side Effects |
|---|---|---|---|
| Assigned | ArrivedAtPickup | PickingUp | 없음 |
| PickingUp | PickupSuccess | Delivering | Cargo 설정 |
| PickingUp | PickupFailed | Failed | Reservation cleanup |
| Delivering | DeliverySuccess | Completed | Reservation 완료, Cargo 비움 |
| Delivering | DestinationInvalid | Failed | Recovery 시작 |
| Any Active | Cancel | Cancelled | Assignment / Reservation cleanup |

이 표를 테스트 기준으로 사용한다.

---

# 10. Structural Change 정책

DOTS 구조 변경은 명확한 지점에서 처리한다.

대상:

- Entity create/destroy
- Component add/remove
- Archetype 변경

원칙:

1. 즉시 structural change가 정말 필요한지 먼저 확인
2. 가능하면 ECB 사용
3. 임의의 System 여러 곳에서 `EntityManager` structural change를 수행하지 않음
4. Spawn / Destroy / Lifecycle 경계에서 처리 시점을 일관되게 유지
5. 일반 component 값 변경과 structural change를 구분

초기 방향:

```text
Simulation Logic
      ↓
ECB Recording
      ↓
정해진 Playback Point
```

단, 모든 state update를 ECB로 만들지는 않는다.

---

# 11. Invariant Validation

Architecture V2에서는 개발 단계에서 데이터 정합성을 자동 검증한다.

Development / Editor 전용 `WorldInvariantValidationSystem`을 고려한다.

예시 검사:

### Item

```text
Stored Item
→ Owner가 유효해야 함
→ 해당 Owner Buffer와 일치해야 함
```

```text
World Item
→ Spatial index에 존재해야 함
```

### Drone

```text
Drone.Task != Null
→ Task가 존재해야 함
→ Task.AssignedDrone == Drone
```

```text
Reserved Item
→ 유효한 Reservation / Task가 존재해야 함
```

### Building

```text
Occupied Building
→ ChunkMap occupancy와 일치
```

### Construction

```text
Reserved construction cell
→ 유효한 ConstructionSite가 존재
```

Invariant System은 게임 상태를 수정하지 않고 오류만 보고한다.

---

# 12. System Dependency 규칙

V2의 목표는 의존성 0이 아니다.

좋은 의존성:

```text
Crafter → Recipe Data
Crafter → Item Query
Crafter → Power Supply Data
```

문제가 되는 의존성:

```text
CrafterSystem
→ ItemStorageSystem 실행 시점을 알아야 함
→ DroneReservationSystem 내부 구조를 알아야 함
→ ChunkMap synchronization timing을 알아야 함
```

원칙:

> Domain은 다른 Domain의 public contract만 알고 내부 처리 순서는 모른다.

---

# 13. Migration Strategy

## 13.1 별도 브랜치

추천:

```text
main
└─ 현재 안정 버전

architecture-v2
└─ 새로운 구조
```

기존 버전은 reference implementation 역할을 한다.

---

## 13.2 기존 코드 전체 삭제 금지

가능하면 다음을 재사용한다.

- Config
- Scriptable/Object definition
- Component 중 책임이 명확한 것
- Grid coordinate
- Recipe 데이터
- Prefab
- Asset
- UI
- 테스트
- 게임 규칙
- 검증된 알고리즘
- Drone의 도메인 개념

다시 설계할 대상:

- System 간 communication
- 상태 ownership
- update phase
- lifecycle transition
- state apply 경계
- direct system call

---

# 14. Vertical Slice Migration

전체 기능을 한꺼번에 이전하지 않는다.

가장 단순한 플레이 가능한 흐름부터 V2를 검증한다.

추천 순서:

---

## Phase 0 — Architecture Skeleton

구현:

- V2 SystemGroup
- 기본 phase 정의
- validation framework
- 공통 naming rule
- Source of Truth 문서

완료 기준:

- 실행 단계가 코드 구조에 표현됨
- 각 phase의 목적을 문서로 설명 가능
- System 간 순환 의존성 없음

---

## Phase 1 — Grid + Item

구현:

- Item identity
- Item ownership 기본 구조
- Item position
- Spatial query/index
- Item spawn
- Item destroy

검증:

- World Item 생성
- 위치 이동
- 공간 조회
- 소유 상태 변경

완료 기준:

- Item 상태 주인이 명확함
- ChunkMap과 Item source of truth 관계가 명확함
- invariant test 통과

---

## Phase 2 — Belt

구현:

- Belt 이동 판단
- Item movement execution
- spatial synchronization

검증:

```text
Spawn
 ↓
Belt
 ↓
이동
```

완료 기준:

- Belt가 Item ownership 내부 구조를 몰라도 됨
- 이동 흐름을 2~3개 단계로 설명 가능
- 복잡한 cross-system direct call 없음

---

## Phase 3 — Storage

구현:

- input 판단
- output 판단
- Item Ownership transfer

검증:

```text
Belt
 ↓
Storage
 ↓
Store
```

그리고:

```text
Storage
 ↓
Output
 ↓
Belt
```

완료 기준:

- StorageSystem이 ownership 내부 변경을 직접 수행하지 않음
- Item transfer 실패 시 rollback/검증 규칙 존재
- owner buffer 정합성 test 존재

---

## Phase 4 — Mining

구현:

```text
Resource
 ↓
Mining
 ↓
Production
 ↓
Item Spawn
 ↓
Belt
 ↓
Storage
```

이 단계가 Architecture V2의 첫 핵심 Vertical Slice다.

완료 기준:

- 광물 생산부터 Storage까지 전체 흐름 플레이 가능
- 기능 흐름을 한 페이지 안에서 추적 가능
- 하나의 규칙 변경이 unrelated System 수정으로 확산되지 않음

---

## Phase 5 — Crafter

추가:

- Recipe
- input collection
- production state
- output

검증:

- 여러 input
- output
- recipe change
- 부족 재료
- production cancel/reset

---

## Phase 6 — Splitter / Merger

목표:

복수 경로 routing을 V2 movement 구조에서 검증한다.

---

## Phase 7 — Construction

추가:

- placement
- reservation
- material requirement
- completion
- cancel
- destroy

이 단계부터 lifecycle 복잡도가 크게 증가한다.

---

## Phase 8 — Power

추가:

- topology
- producer
- consumer
- supply ratio

검증:

다른 생산 Domain이 PowerGrid 내부 구조를 알지 않고
supply data만 소비할 수 있어야 한다.

---

## Phase 9 — Drone

마지막 대형 이식 대상.

순서:

```text
Task Definition
↓
Planning
↓
Reservation
↓
Scheduling
↓
Dispatch
↓
Movement
↓
Cargo Transfer
↓
Lifecycle / Recovery
```

Drone lifecycle transition table을 먼저 확정한 후 구현한다.

---

## Phase 10 — Research / Remaining Systems

나머지 시스템 이전.

---

# 15. 테스트 전략

Architecture V2에서는 테스트를 기능 검증뿐 아니라
**아키텍처 경계 검증 도구**로 사용한다.

---

## 15.1 Unit / Domain Logic Test

대상:

- Recipe validation
- candidate scoring
- reservation 계산
- transition 규칙
- movement 계산

---

## 15.2 ECS Integration Test

대상:

```text
System A 결과
→ 데이터 생성
→ System B 처리
→ 최종 상태
```

직접 System method 호출에 의존하지 않는 흐름을 검증한다.

---

## 15.3 Invariant Test

예:

```text
Ownership ↔ Owner Buffer
Task ↔ Assigned Drone
Reservation ↔ Task
Position ↔ Spatial Index
```

---

## 15.4 Vertical Slice Test

최종적으로 실제 게임 흐름을 검증한다.

첫 목표:

```text
Resource
→ Miner
→ Item
→ Belt
→ Storage
```

---

# 16. Architecture V2 Acceptance Criteria

Architecture V2가 성공했다고 보기 위한 조건.

## 구조

- System의 역할을 한두 문장으로 설명할 수 있다.
- 각 주요 데이터의 Source of Truth가 명확하다.
- 다른 System의 lifecycle을 직접 호출하는 코드가 예외적으로만 존재한다.
- 중요한 상태는 명확한 Owner가 변경한다.
- update order는 세부 System보다 Phase 단위로 이해할 수 있다.

## 유지보수

다음 질문에 빠르게 답할 수 있어야 한다.

```text
아이템 소유권 변경?
→ Item Ownership

Storage 입출력 정책?
→ Storage

Drone 실패 처리?
→ Drone Lifecycle

공간 점유?
→ Spatial / ChunkMap

전력 연결?
→ PowerGrid
```

## 변경 영향

한 Domain의 규칙 수정이 unrelated Domain까지 수정 요구하지 않아야 한다.

## 디버깅

잘못된 상태가 생겼을 때 invariant validation으로 빠르게 탐지할 수 있어야 한다.

## 성능

구조 정리 후 Profiler를 통해 병목을 측정한다.

Architecture V2 설계 자체만으로 성능 향상을 가정하지 않는다.

---

# 17. 구현 전 반드시 결정할 사항

코드를 작성하기 전에 다음 결정을 문서화한다.

### Item

- Item Ownership Source of Truth는 무엇인가?
- Owner Buffer는 원본인가, 파생 데이터인가?
- Stored Item은 Position을 유지하는가?

### Spatial

- Entity GridPosition과 ChunkMap 중 무엇이 원본인가?
- WorldItem index update 시점은 언제인가?

### Phase

- Decision과 Execution 사이에서 사용하는 데이터 표현은 무엇인가?
- Apply가 필요한 상태와 직접 변경 가능한 상태의 기준은 무엇인가?

### Drone

- Task의 canonical lifecycle은 무엇인가?
- Reservation 해제 책임은 어디에 있는가?
- 실패와 취소의 공통 cleanup 범위는 무엇인가?

### Structural Change

- ECB playback point는 어디인가?
- Spawn / Destroy는 어느 phase가 소유하는가?

---

# 18. V2 개발 규칙 초안

1. 새 System을 만들기 전에 어떤 상태를 읽고 쓰는지 먼저 작성한다.
2. 쓰는 상태가 다른 Domain 소유라면 직접 변경하지 않는다.
3. 다른 System의 public method 호출이 필요하면 먼저 Utility 또는 데이터 기반 전달 가능성을 검토한다.
4. `UpdateBefore/After`를 추가할 때 왜 필요한지 기록한다.
5. 새로운 cache/index를 만들 때 Source of Truth를 기록한다.
6. 상태 transition이 2개 이상의 데이터를 정리한다면 lifecycle owner를 검토한다.
7. 구조 변경이 필요하면 ECB 사용 여부를 먼저 검토한다.
8. 새 기능에는 최소 하나의 invariant 또는 integration test를 추가한다.
9. 성능 최적화는 profiler 결과가 있을 때 진행한다.
10. Architecture V2에서 기존 코드를 그대로 복사하지 말고 책임 경계에 맞춰 재배치한다.

---

# 19. 첫 번째 실제 작업

Architecture V2의 첫 구현 작업은 Drone이 아니다.

먼저 다음 Slice를 만든다.

```text
Grid
  ↓
Item
  ↓
Belt
  ↓
Storage
  ↓
Mining
```

최종 검증 흐름:

```text
Resource
   ↓
Mining
   ↓
Item Spawn
   ↓
Belt Movement
   ↓
Storage Input
   ↓
Ownership Transfer
```

이 작은 흐름으로 다음을 검증한다.

- Phase 구조가 실제로 단순한가?
- System 간 직접 호출 없이 구현 가능한가?
- Source of Truth가 명확한가?
- 수정 위치를 쉽게 예측할 수 있는가?
- 디버깅하기 쉬운가?
- Job/Burst로 확장하기 쉬운가?

이 Slice가 만족스럽지 않다면 Drone 이전으로 넘어가지 않는다.

---

# 20. 최종 방향

Architecture V2는 기존 PlanetMiner의 좋은 도메인 분리를 버리는 작업이 아니다.

유지:

```text
Spatial
Item
Building
Production
Power
Drone
Research
```

새로 설계:

```text
State Ownership
System Communication
Execution Phase
Lifecycle Transition
Structural Change Boundary
Synchronization
```

Architecture V2의 최종 목표는 다음과 같다.

> **각 System이 자기 책임만 알고도 동작할 수 있으며,  
> 하나의 기능을 수정할 때 전체 게임의 실행 순서를 머릿속으로 추적하지 않아도 되는 구조.**

이 목표를 만족하는 것을 성능 최적화보다 우선한다.
