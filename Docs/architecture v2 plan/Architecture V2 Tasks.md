# PlanetMiner Architecture V2 - Implementation Tasks & Roadmap

> 본 문서는 `Architecture V2 Plan_0.2.md`를 바탕으로, 실제 구현을 위해 단계별 세부 작업(Task)과 진행 상태를 추적하는 체크리스트 문서입니다.
> 
> **핵심 원칙**: 
> - 글로벌 네임스페이스 및 `V2` 접두사 배제 규칙 준수
> - 6단계 실행 Phase(`Command` → `Decision` → `Reservation` → `Execution` → `StateApply` → `Synchronization`) 엄수
> - 시뮬레이션 코어의 Unmanaged/Burst 호환 데이터 설계
> - `Consume-on-Apply` 수명주기 및 명확한 상태 소유권(State Owner) 확립

---

## 📊 전체 마일스톤 개요

```text
[Phase 0] Skeleton (기존 코어 완료 / Authoring 공통 계약 추가)
   ↓
[Phase 1] Grid + Item (기존 코어 완료 / 아이템 프리팹 연결 추가)
   ↓
[Phase 2] Belt
   ↓
[Phase 3] Storage (기본 건물 설정 로딩 추가)
   ↓
[Phase 4] Mining ───★ 기존 코어 Slice 완료 / 월드 로딩·광맥 생성·자원 스폰·바이옴 후속 추가
   ↓
[Phase 5] Crafter
   ↓
[Phase 6] Splitter / Merger
   ↓
[Phase 7] Construction Core (실제 드론 운반·철거 연결은 Phase 9)
   ↓
[Phase 8] Power Grid
   ↓
[Phase 9] Drone System ─── 기반 → 계획·예약·배차 → 정상 운반 → 취소·복구·통합 → 게임 시작 초기화
   ↓
[Phase 10] 10A Research → 10B Input / UI / Presentation → 10C Integration / Performance
```

---

## 후속 Task 운영 기준

- 이 문서의 Phase 0~10은 개발 마일스톤이다. 런타임의 6대 실행 Group과 구분한다.
- 기존 완료 Task의 내용과 체크는 보존한다. Phase 0·1·3·4에 추가한 런타임 연결 후속 Task와 Phase 6~10의 미완료 작업은 별도로 추적한다. 기존 코어의 완료 기록이 새 후속 Task의 완료를 뜻하지 않는다.
- 각 Task는 **선행 조건 / 구현 범위·상태 소유자 / 후속 연결 / 검증 / 완료 기준**을 기록한다. 작업 번호보다 명시된 의존성을 우선한다.
- Task는 구현·검증 단위다. Task마다 새 시스템이나 상태 소유자를 만들지 않으며 기존 V2 컴포넌트·소유권 경계·공간 인덱스를 재사용한다. 아래의 소유자 표현은 책임을 나타내며 새 클래스명을 확정하는 것은 아니다.
- 각 Task에서 정상·실패 경로와 관련 불변식을 함께 검증한다. 마지막 통합 Task는 도메인 간 상호작용과 부하를 검증하며, 최초의 오류 처리 검증 단계로 사용하지 않는다.
- 그룹 순서와 ECB 재생 시점을 검증하는 테스트는 실제 `GameSimulationGroup` 실행을 사용한다. 테스트 편의를 위한 추가 Playback으로 실제 프레임 경계를 바꾸지 않는다.
- 아이템 선점과 목적지 용량 선점처럼 함께 성공해야 하는 상태 변경은 하나의 원자적 작업으로 유지한다. 획득과 해제·완료·복구를 나누되, 반쪽 예약을 성공 상태로 게시하지 않는다.
- C#·에셋 변경 시 관련 컴파일과 범위에 맞는 검증을 수행한다. 컴파일/EditMode, 실제 플레이·입력·시각 결과, 성능 측정의 증거를 구분한다. 수동 작동 검증은 사용자가 수행하며, 별도 요청 없이 Play Mode 검증을 자동 실행하지 않는다.

### 도메인 착수 시 이식 범위 대조

여기서 **미이식 기능 목록**은 기존 게임 기능 중 현재 V2 코드 또는 Task에 연결되지 않은 기능을 확인하는 목록이다. 새 기능을 추가하거나, 전체 프로젝트 조사를 모든 작업의 선행 조건으로 만드는 절차가 아니다.

- 각 도메인 착수 시 관련 기존 소스·설정·상세 문서만 대조한다. 레거시 문서는 탐색 자료로 사용하고 실제 동작은 원본 코드로 확인한다.
- 누락을 발견하면 `기존 기능 / 확인 근거 / 현재 V2 대응 / 담당 Task / 확인이 필요한 규칙`을 해당 Phase에 기록한다. 구현 필요성이 확인된 범위만 명시적인 하위 Task로 추가한다.
- 시작 시설 생성, 프리팹 Authoring, 설정 로딩, 월드·자원 생성에서 확인된 누락은 아래 담당표의 미완료 Task로 등록한다. 실제 구현·이식 여부는 해당 Task의 검증으로 확인한다.
- 새 게임 규칙이 필요하거나 기존 규칙이 불명확하면 해당 선택지를 확인한 뒤 구현한다. 범위 대조 자체를 이유로 기존 동작을 변경하지 않는다.
- Task 10C.1에서는 앞 단계에서 작성한 대응 목록과 완료 증거를 최종 대조한다.

### 확인된 미이식 기능의 담당 Task

| 기능 | 구현 담당 Task | 실행·표시 연결 |
| --- | --- | --- |
| 게임 시작 초기화·초기 지급 | Task 9.21 설정 로딩, Task 9.22 주 시설·전력·정거장·초기 지급 구성 | Task 10C.2 실제 시작 흐름 검증 |
| 초기·카메라 주변 청크 로딩 | Task 4.5 초기 영역 설정, Task 4.6 청크 요청·초기 로딩 | Task 10B.5 카메라 기반 로딩 |
| 광맥 생성·자원 스폰 | Task 4.5 설정, Task 4.7 생성, Task 4.8 자원 베이킹·스폰 | Task 10B.5·10C.2 월드 확장·실행 검증 |
| 바닥·바이옴 생성 및 렌더링 | Task 4.9 바닥 설정·바이옴 생성 | Task 10B.6 청크 바닥 렌더링 |
| Authoring·프리팹 DB·렌더 엔티티 생성 | Task 0.6 공통 계약, Task 1.7 아이템, Task 4.8 자원, Task 7.3.1 건물, Task 9.3.1 드론 | 각 생성 소유자의 스폰 경로 및 Task 10C.2 |
| 기본 건물·시작·월드 설정 연결 | Task 3.5 기본 건물, Task 4.5 월드·자원, Task 4.9 바닥, Task 9.21 초기 지급 | 기존 레시피·스택 로더 재사용; 공사·전력·드론·연구 설정은 각 도메인 Task에서 처리 |

위 표는 담당 배정이며 완료 목록이 아니다. 월드·자원 기반은 Phase 4, 실제 건물·드론 생성 연결은 해당 도메인, 입력·바닥 표시는 10B에서 구현한다. 주 시설의 전체 초기화는 전력·드론 기반이 갖춰진 Task 9.22에서 완료한다.

### 기존 구현의 보완 검증

다음 항목은 정적 점검 및 개선 피드백(`PlanetMiner_architecture-v2_feedback.md`)에서 발견된 검증 공백과 아키텍처 불변식 준수 여부를 추적·반영한 항목이다.

- [x] **보완 검증 F1: Crafter의 실제 Group / ECB 순서 검증**
  - 선행 조건: 현재 Crafter·Item lifecycle·StateApply 실행 순서 확인.
  - 구현 범위·상태 소유자: `CrafterDecisionSystem`의 영속 상태 직접 수정을 배제하고, `CrafterStateDecision` 컴포넌트 도입으로 `DecisionGroup`과 `StateApplyGroup`의 책임을 명확히 분리. 생산물은 `ProductResult` 버퍼를 거쳐 `ItemLifecycleApplySystem`에서 안전하게 스폰 및 소유권 반영.
  - 후속 연결: Phase 6 생산물 출력 통합, Phase 8 전력 기반 제작, Phase 10A 연구의 재료 소비 검증.
  - 검증: 테스트 내부 임의의 추가 ECB 재생 없이 `Phase5CrafterExecutionTests` (Test01~Test08) 전수 통과 확인.
  - 완료 기준: 실제 프레임 경계에서 소유권·요청 수명주기 불변식을 만족하고 관련 회귀 테스트가 통과한다.

- [x] **보완 검증 F2: Crafter 레시피 해제·변경 경계 검증**
  - 선행 조건: 현재 레시피 변경 및 `StorageFilter` 계약 확인.
  - 구현 범위·상태 소유자: `CrafterRecipeCommandSystem`을 구축하여 `CommandGroup`에서 레시피 변경 및 `StorageFilter`를 원자적으로 갱신. 변경 시 잔여 재료의 Byproduct 버퍼(Slot 1+) 이관 및 `WaitingForByproductOutput` 상태 진입을 통해 불일치 재료 입고 원천 차단.
  - 후속 연결: Task 10B.3 제작기 설정 UI.
  - 검증: `Phase5RecipeChangePipelineTests` (Test01~Test02)를 통해 레시피 변경 직후 같은 프레임 입고 차단 및 버퍼 비움 후 복귀 전수 통과 확인.
  - 완료 기준: 확인된 레시피 변경 규칙과 필터 동작이 일치하고 아이템 유실·중복이 없다.

- [x] **보완 검증 F3: DestroyItemRequest 발행 전 Owner Buffer 선제거 계약 및 Invariant 검증 (피드백 6)**
  - 선행 조건: `DestroyItemRequest` 수명주기 및 `StoredItemElement`/`ProductItemElement` 소유권 계약 확인.
  - 구현 범위·상태 소유자: 아이템 파괴를 유발하는 시스템(Crafter, 철거, 연구 등)이 `DestroyItemRequest`를 활성화하기 전 소유 버퍼에서 해당 엔티티를 반드시 선제거(`RemoveAt`)하도록 Producer 책임 확정.
  - 후속 연결: Phase 7 Construction 철거, Phase 9 Drone 화물 소비, Phase 10 Research 소비.
  - 검증: 버퍼 미제거 채 엔티티 파괴 시 `WorldInvariantValidationSystem`이 Fail-Fast로 감출하도록 구현. `Phase1ItemIntegrationTests` (Test11, Test12) 통과.
  - 완료 기준: 댕글링 엔티티 참조 방지 및 버퍼-파괴 수명주기 불변식을 보장한다.

- [x] **보완 검증 F4: Simulation DeltaTime 정책 일원화 및 상한 클램핑 검증 (피드백 7)**
  - 선행 조건: `GameConstants.MaxSimulationDeltaTime` (0.1f) 공통 상수 확인.
  - 구현 범위·상태 소유자: Belt, Crafter에 이어 `MinerExecutionSystem`에도 `math.min(dt, 0.1f)` 클램핑을 일원화 적용하여 프레임 hitch 발생 시 도메인 간 시뮬레이션 시간 왜곡 방지.
  - 후속 연결: Phase 8 Power, Phase 9 Drone 이동/충전 파이프라인.
  - 검증: `Phase4MinerPipelineTests`에 대형 DeltaTime 입력 시 진행도 0.1f 제한 검증(`Test10`) 통과.
  - 완료 기준: 모든 시뮬레이션 시스템이 단일 DeltaTime 정책을 준수한다.

- [x] **보완 검증 F5: MaxStorageSlots 제한 실제 강제 및 Invariant 검증 (피드백 8)**
  - 선행 조건: `GameConstants.MaxStorageSlots = 120` 제약 확인.
  - 구현 범위·상태 소유자: 조용한 clamp로 인한 데이터 오염을 방지하기 위해 `WorldInvariantValidationSystem`에서 `Storage.SlotCount <= 0 || Storage.SlotCount > MaxStorageSlots`를 직접 검사하여 조기 감출(Fail-Fast). Burst 안전망(Reservation 클램프)은 보조 유지.
  - 후속 연결: Phase 3 기본 건물 설정 로딩, Task 7.3 건물 초기화.
  - 검증: `Phase3StorageComponentTests`에 상한 초과(200), 하한 위반(0), 경계값 정상(120) 검증 테스트 3종(`Test04`, `Test05`, `Test06`) 통과.
  - 완료 기준: 저장소 슬롯 경계값 위반이 조기에 탐지되고 메모리 오염을 차단한다.

- [x] **보완 검증 F6: BuildingSpatialIndex 동적 용량 확장 및 전수 인덱싱 검증 (피드백 10)**
  - 선행 조건: `BuildingSpatialIndex` NativeParallelMultiHashMap 구조 확인.
  - 구현 범위·상태 소유자: 기본 용량(1024)을 초과하는 대규모/다중 타일 건물 배치 시 인덱스 동적 용량 확장 알고리즘 개선.
  - 후속 연결: Phase 7 Construction Core 대규모 배치.
  - 검증: `Phase3BuildingSpatialIndexTests`에 3x3 건물 150개(총 1350타일) 생성 시 동적 용량 확장 및 전수 인덱싱 검증(`Test05`) 통과.
  - 완료 기준: 대규모 타일 건물 등록 시 공간 인덱스 용량 부족으로 인한 누락이 없다.

- [x] **보완 검증 F7: SpawnItemRequest의 ItemSpawnDestination 목적지 명시 (피드백 5)**
  - 선행 조건: `SpawnItemRequest`의 `TargetOwner` 버퍼 중복 모호성 확인.
  - 구현 범위·상태 소유자: `ItemSpawnDestination` (World, Storage, Product) enum 정의 및 요청 시 목적지 명시 확정.
  - 후속 연결: Phase 7 공사 자재 스폰, Phase 9 드론 아이템 스폰, Save/Load.
  - 완료 기준: 스폰 요청 대상 버퍼 우선순위 모호성이 제거되고 의도한 버퍼/월드로 정확히 배정된다.

- [x] **보완 검증 F8: Crafter 다중 부산물 처리 통일 및 All-or-Nothing 출력 검증 (피드백 3)**
  - 선행 조건: Recipe Blob의 다중 `Outputs` 모델 확인.
  - 구현 범위·상태 소유자: Crafter가 단일 부산물이 아닌 다중 부산물 전체를 순회 처리하도록 계약을 확정하고, 하나의 부산물 슬롯이라도 만석이면 배출을 대기하는 All-or-Nothing 정책 적용.
  - 후속 연결: Phase 6 Splitter/Merger 복합 부산물 물류 처리.
  - 검증: `Phase5CrafterExecutionTests` (`Test07`, `Test08`)를 통해 다중 부산물 슬롯별 배출 및 All-or-Nothing 백프레셔 검증 통과.
  - 완료 기준: 레시피 정의와 런타임 부산물 배출 동작이 완전히 일치한다.

---

## 🧱 [Phase 0] Architecture Skeleton 구축 (기반 뼈대)

- [x] **Task 0.1: 레거시 코드 완전 백업 및 작업 공간 격리**
  - [x] `Assets/Scripts`, `Assets/Editor`, `Assets/UI`를 `Backup Scripts/` 폴더로 이동하여 컴파일 에러 없는 클린 상태 확보
  - [x] 신규 `Assets/Scripts/Phases/` 폴더 구성
- [x] **Task 0.2: 6대 Phase ComponentSystemGroup 정의**
  - [x] `CommandGroup` (Phase 1: 입력 및 외부 명령 변환)
  - [x] `DecisionGroup` (Phase 2: 도메인별 게임 규칙 판단)
  - [x] `ReservationGroup` (Phase 3: 공유 자원 예약 및 경합 방지)
  - [x] `ExecutionGroup` (Phase 4: 확정된 작업 진행/계산)
  - [x] `StateApplyGroup` (Phase 5: 상태 정합성 및 소유권 실제 반영)
  - [x] `SynchronizationGroup` (Phase 6: 공간 인덱스 동기화 및 임시 데이터 정리)
- [x] **Task 0.3: 최상위 시뮬레이션 그룹 구축 및 정렬**
  - [x] `GameSimulationGroup`을 Unity의 `SimulationSystemGroup` 내에 등록
  - [x] 6대 Phase 그룹 간 `[UpdateInGroup]`, `[UpdateAfter]` 순서 보장 검증
- [x] **Task 0.4: Invariant Validation (데이터 무결성 검증) 프레임워크 뼈대 구축**
  - [x] `WorldInvariantValidationSystem` 생성 (Editor/Development 빌드 전용)
  - [x] 데이터 불일치/고아 엔티티 탐지 시 `Logs/InvariantErrors/` 파일 기록 및 `Debug.Break()` 에디터 일시정지 구조 마련 (콘솔 에러 제외)
- [x] **Task 0.5: Transient Command/Event 스탠다드 및 템플릿 정의**
  - [x] `Consume-on-Apply` 가이드라인에 맞춘 1회성 Request 컴포넌트 작성 규칙 수립 (`Architecture V2 Command Event Standard.md`)
  - [x] 하이브리드 Request 모델(`IRequestComponent`, `IEnableableRequest`) 마커 인터페이스 구축 및 ECB 사용 가이드 정리

### 후속 런타임 연결

- [ ] **Task 0.6: Authoring·프리팹 데이터베이스 공통 계약 정의**
  - 선행 조건: 기존 6대 실행 Group과 Item·건물·자원 데이터 계약, 백업 Authoring 코드 확인.
  - 구현 범위·상태 소유자: 장면·SubScene Authoring이 게시할 정의·프리팹 참조·초기 설정, 베이킹된 데이터의 준비 조건, 실제 생성 소유자의 초기화 책임을 정의한다. 개별 도메인의 런타임 상태는 Baker가 중복 소유하지 않는다.
  - 후속 연결: Task 1.7·4.8·7.3.1·9.3.1에서 대상별 Baker·프리팹 연결을 구현한다. 이 Task를 이유로 범용 프레임워크를 새로 만들지 않는다.
  - 검증: 타입 식별자·필수 컴포넌트·TransformUsage·중복 컴포넌트 추가와 프리팹 누락 처리의 계약을 대조한다.
  - 완료 기준: 각 도메인이 사용할 베이킹 입력·출력과 스폰 시점의 책임이 명확하며 기존 소유권 경계를 보존한다.

---

## 📦 [Phase 1] Grid + Item 도메인 이식

- [x] **Task 1.1: Item Core Unmanaged Component 설계**
  - [x] `ItemIdentity` (아이템 고유 타입 식별, `ItemTypeEnum` 100% 호환 계승)
  - [x] `GridPosition` (공간 2D 좌표 - Blittable struct, 건물/자원/아이템 공통)
  - [x] `ItemOwnership` (단일 원본 소유권 - `Owner == Entity.Null` 월드 아이템, `Owner != Entity.Null` 수납 아이템)
  - [x] World Item과 Stored Item의 상태 구분 정의 (소유권 단일 원본 기반 판별로 불필요한 토글 제거)
- [x] **Task 1.2: Item 상태 제어용 1회성 Request 정의**
  - [x] `SpawnItemRequest` (독립 엔티티 방식 - 모델 A: 월드 및 수납 스폰 통합 지원, `ItemSpawnDestination` 목적지 명시)
  - [x] `DestroyItemRequest` (대상 부착형 - 모델 B: `IEnableableRequest` 소모/파괴 마킹, 소유 버퍼 선제거 계약 확정)
  - [x] `TransferOwnershipRequest` (대상 부착형 - 모델 B: `IEnableableRequest` 순수 소유권 이전 전담)
  - [x] 8대 메타데이터 주석 규약(Producer, Consumer, Consume Phase 등) 명시 및 `Components/Item/` 폴더화 완료
- [x] **Task 1.3: Item Ownership State Owner 시스템 구현**
  - [x] `ItemOwnershipApplySystem` (`StateApplyGroup`에 배치): `TransferOwnershipRequest` 소비 및 소유권 갱신 (`ItemOwnership.WorldItem` / `ItemOwnership.Stored`)
  - [x] `ItemLifecycleApplySystem` (`StateApplyGroup`에 배치): `SpawnItemRequest` 및 `DestroyItemRequest` 전담 처리 (하이브리드 프리팹/아키타입 스폰 및 ECB 엔티티 파괴)
- [x] **Task 1.4: Spatial Index (ChunkMap) 기초 구조 연동**
  - [x] 공간 쿼리용 데이터 구조 정의 (NativeParallelMultiHashMap 기반 ItemSpatialIndex Unmanaged 싱글톤)
  - [x] `ItemSpatialSyncSystem` (`SynchronizationGroup`에 배치, ISystem + Burst)
  - [x] World Item 위치 이동 결과를 공간 인덱스에 최종 동기화 (Clear & Rebuild 메커니즘)
- [x] **Task 1.5: Item & Spatial Invariant Validation 작성**
  - [x] World Item은 반드시 유효한 공간 인덱스에 등록되어 있어야 함을 검증 (양방향 정합성)
  - [x] Stored Item의 Owner Entity는 실제로 존재하는 유효 엔티티여야 함을 검증
  - [x] 프레임 종료 시 미소비(Unconsumed) 활성 Request 잔류 감시 안전망 연동
  - [x] `DestroyItemRequest` 활성화 전 소유 버퍼 미제거 시 Invariant Fail-Fast 감출 검증 (`Phase1ItemIntegrationTests` Test11, Test12 Pass)
- [x] **Task 1.6: Phase 1 통합 검증 (Integration Test)**
  - [x] 아이템 생성 → 위치 변경 → 공간 조회 → 소유권 이전 → 파괴 흐름 테스트 통과 (Phase1ItemIntegrationTests 회귀 테스트 포함 전수 통과)

### 후속 런타임 연결

- [ ] **Task 1.7: 아이템 Authoring·프리팹 DB 및 렌더 스폰 연결**
  - 선행 조건: Task 0.6 및 기존 Item lifecycle·ownership·spatial 계약.
  - 구현 범위·상태 소유자: 아이템 정의·렌더 프리팹을 베이킹하고 기존 아이템 생성 소유자가 이를 사용하도록 연결한다. 검증용 임시 아키타입 경로와 런타임 프리팹 경로를 구분하며 월드·수납 표시 상태는 소유권에서 파생한다.
  - 후속 연결: Task 4.8 자원 스폰, Task 7.3 건물 출력, Task 9.22 초기 지급 및 Task 10C.2 실제 표시 검증.
  - 검증: 월드·수납 스폰, 수납·방출 후 표시 데이터, 프리팹 누락, 중복 컴포넌트와 ECB 재생 후 공간 등록을 검사한다. 시각 결과는 수동 검증으로 구분한다.
  - 완료 기준: 유효한 프리팹으로 생성된 아이템이 기존 소유권·요청 수명주기·공간 계약을 만족하고 렌더 데이터가 연결된다.

---

## 🛤️ [Phase 2] Belt (컨베이어 벨트 이동)

- [x] **Task 2.1: Belt 관련 컴포넌트 정의**
  - [x] `BeltComponent` (벨트 건물 엔티티: 속도, 방향은 공통 Direction 재사용)
  - [x] `BeltMovementState` (아이템 엔티티: IEnableableComponent, 진행도 Progress [0~1], PlannedMovement, IsBlocked)
- [x] **Task 2.2: Belt Decision System 구현**
  - [x] `BeltMovementDecisionSystem` (`DecisionGroup`에 배치)
  - [x] 아이템이 벨트를 따라 전진 가능한지 판단 (간격, 앞선 아이템 유무)
  - [x] *주의: Request Entity 폭증 방지를 위해 Persistent Component 값 갱신 사용*
- [x] **Task 2.3: Belt Execution System 구현**
  - [x] `BeltMovementExecutionSystem` (`ExecutionGroup`에 배치)
  - [x] 결정된 진행 거리만큼 실제 `ItemPosition` 업데이트 (Burst/Job 병렬 처리)
- [x] **Task 2.4: 공간 동기화 및 Invariant 검증**
  - [x] 벨트 상의 아이템 위치 변화가 `SynchronizationGroup`에서 정상 반영되는지 검증
  - [x] 벨트 위 아이템 겹침(충돌) 방지 invariant test 작성

---

## 🏭 [Phase 3] Storage (창고 입출력 및 보관)

- [x] **Task 3.1: Storage 컴포넌트 정의**
  - [x] `Storage` (슬롯 칸수) 및 `StorageFilter` / `FixedBitSet` (필터 분리)
  - [x] `StoredItemElement` (`DynamicBuffer<T>`, SlotIndex 포함)
- [x] **Task 3.2: Storage/Building 입출력 Decision System 구현**
  - [x] **Task 3.2.1: 건물 메타데이터 및 BuildingSpatialIndex 공간 인덱스 인프라 구축**
    - [x] `BuildingTypeEnum`, `BuildingFootprint` 컴포넌트 정의 (다중 타일 2x2, 3x3 등 지원)
    - [x] `BuildingInfo`, `BuildingSpatialIndex`, `BuildingSpatialIndexFence` 구조체 정의 (Resource-Level JobHandle Fence 패턴)
    - [x] `BuildingSpatialSyncSystem` (`SynchronizationGroup`, Phase 6) 구현: 다중 타일 일괄 등록 및 비동기 Clear/Populate 체인
    - [x] `BuildingSpatialIndex` 대규모 다중 타일 건물 수용을 위한 동적 Capacity 확장 및 안전 인덱싱 검증 (`Phase3BuildingSpatialIndexTests` Test05 Pass)
  - [x] **Task 3.2.2: 아이템별 스택 설정 인프라 (ItemConfig) 구축**
    - [x] `ItemConfig` 컴포넌트 및 싱글톤 버퍼 정의
    - [x] `ItemTypeEnum`별 `MaxStack`(광석 50, 완제품 100, 드론 1 등) 설정 제공
  - [x] **Task 3.2.3: 입출력 의사결정 컴포넌트 정의 (상태-의사결정 분리)**
    - [x] `BuildingItemInputDecision` (아이템 엔티티 부착, Enableable: TargetBuilding, TargetSlotIndex, CanDeposit)
    - [x] `BuildingItemOutputDecision` (건물 엔티티 부착, Enableable: CanOutput, ItemToOutput, TargetBeltPosition)
  - [x] **Task 3.2.4: BuildingItemInputDecisionSystem 구현 (입고 판단)**
    - [x] `DecisionGroup` (Phase 2)에 배치
    - [x] 벨트 종단(Progress >= 1.0f - Epsilon) 아이템 감지
    - [x] `BuildingSpatialIndex`로 다음 타일의 건물 및 종류 O(1) 식별
    - [x] `StorageFilter` 마스크 및 보관 가능 여부 검사 (중복 슬롯 검사 배제 및 Reservation 단계 위임)
    - [x] 입고 가능 시 `CanDeposit = true`, 불가 시 벨트 끝 대기(`IsBlocked = true`)
  - [x] **Task 3.2.5: BuildingItemOutputDecisionSystem 구현 (출고 판단)**
    - [x] `DecisionGroup` (Phase 2)에 배치
    - [x] 보관 아이템이 있는 건물(`buffer.Length > 0`) 탐색
    - [x] 건물 둘레의 외향 벨트 탐색 및 벨트 입구(Progress == 0.0f) 간격(`ItemSpacing`) 확보 확인
    - [x] 방출 가능 시 FIFO 0번 아이템(`buffer[0]`)을 대상으로 `CanOutput = true` 결정
  - [x] **Task 3.2.6: Task 3.2 단위 및 파이프라인 검증 테스트 작성**
    - [x] `Phase3StorageDecisionTests.cs` (다중 타일 탐색, 필터링, 슬롯 만석 차단, FIFO 출고 및 벨트 정체 대기 검증)
- [x] **Task 3.3: Storage 소유권 이전 연동**
  - [x] 벨트 아이템 → 창고 적재: `TransferOwnershipRequest` 발행
  - [x] 창고 아이템 → 벨트 방출: World Item 전환 및 소유권 해제
  - [x] `ItemOwnershipApplySystem`에서 트랜잭션 무결성 검증
- [x] **Task 3.4: Storage Buffer Invariant 검증**
  - [x] `ItemOwnership.Owner == StorageEntity` ↔ `Storage DynamicBuffer에 아이템 등록` 양방향 무결성 검증
  - [x] `Storage.SlotCount <= GameConstants.MaxStorageSlots` (120) 경계값 초과 차단 Invariant 검증 (`Phase3StorageComponentTests` Test04~06 Pass)

### 후속 런타임 연결

- [ ] **Task 3.5: 기본 건물 런타임 설정 로딩 및 게시**
  - 선행 조건: 기존 Belt·Storage·Miner·Crafter 컴포넌트와 백업 `BuildingRuntimeConfig` 스키마 확인.
  - 구현 범위·상태 소유자: 건물 기본 속도·저장 칸수 등 기존 설정을 검증해 타입별 정의 데이터로 게시한다. 설정 로더는 정의만 소유하고 인스턴스 진행도·소유 버퍼는 변경하지 않는다. 기존 레시피·아이템 스택 레지스트리는 재사용한다.
  - 후속 연결: Task 7.3.1의 건물 정의 연결과 Task 7.3의 인스턴스 초기화에서 소비한다. 공사 비용·전력·드론·연구 설정은 해당 도메인이 담당한다.
  - 검증: 누락·중복 타입, 잘못된 속도·용량, 로딩 실패 시 부분 데이터 게시, 기존 레시피·스택 설정과의 역할 중복을 검사한다.
  - 완료 기준: 검증된 기본 건물 설정의 단일 조회 경로가 있고 인스턴스 생성에 사용할 수 있다.

---

## ⛏️ [Phase 4] Mining (1차 핵심 Vertical Slice 완성)

- [x] **Task 4.1: Resource 및 Miner 컴포넌트 정의**
  - [x] `ResourceNode` (타입, 매장량, 그리드 위치)
  - [x] `MinerComponent` (채굴 속도, 진행도, 배출 방향)
- [x] **Task 4.2: Miner Decision & Execution System 구현**
  - [x] 채굴 조건 판단 (하부 자원 유무, 내부 출력 버퍼 여유 검사 및 지연 스폰을 고려한 Pending Capacity 검사)
  - [x] 채굴 진행도 누적 및 아이템 스폰 트리거 (채굴 완료 시 채굴기 버퍼로 SpawnItemRequest 발행, 자원 차감/고갈 파괴, 기존 BuildingItemOutput 출고 파이프라인 100% 재사용)
  - [x] `GameConstants.MaxSimulationDeltaTime` (0.1f) 정책 일원화 및 대형 DeltaTime 입력 클램핑 안전망 검증 (`Phase4MinerPipelineTests` Test10 Pass)
- [x] **Task 4.3: 자원 채굴 → 스폰 → 벨트 → 창고 파이프라인 완성**
  - [x] 광물 스폰(`Command/StateApply`) → 벨트 이송(`Decision/Execution`) → 창고 적재(`Decision/Reservation/StateApply`)
  - [x] 시스템 간 직접 호출 없이 오직 GameSimulationGroup 6대 Phase 데이터 파이프라인만으로 전체 게임 루프 동작 완주
  - [x] 단일 완주 루프, 연속 스트림 흐름, 창고 만석 역류 정체(Backpressure) 3대 시나리오 NUnit 자동화 통합 테스트 및 불변식 0건 검증 완료 (Phase4EndToEndPipelineTests 3종 통과, 총 78/78 테스트 100% 통과)
- [x] **Task 4.4: 1차 핵심 Vertical Slice 완료 판정**
  - [x] 한 화면 안에서 광물 채굴부터 보관까지의 데이터 흐름 추적 가능 여부 확인
  - [x] 아키텍처 V2 Acceptance Criteria 1차 평가

### 후속 월드·자원 연결

Task 4.1~4.4의 코어 검증과 실제 월드 생성 경로를 구분한다. 아래 Task는 테스트에서 미리 만든 자원을 넘어, 설정·청크 요청에서 자원 엔티티가 생성되는 런타임 경로를 담당한다.

- [ ] **Task 4.5: 초기 영역·월드 시드·자원 생성 설정 연결**
  - 선행 조건: Task 0.6 및 기존 Resource 데이터 계약, 백업 초기 영역·자원 설정 Authoring 확인.
  - 구현 범위·상태 소유자: 초기 로드 영역, 월드 시드, 자원별 광맥·매장량 설정을 검증해 ECS 정의 데이터로 게시한다. 기존 베이킹·로드 시점과 설정 의미를 확인하고 생성 진행 상태와 분리한다.
  - 후속 연결: Task 4.6에서 초기 청크를 요청하고 Task 4.7에서 자원 배치를 계산한다.
  - 검증: 같은 시드·설정, 잘못된 영역·품목·범위 값, 설정 누락·재초기화와 중복 게시를 검사한다.
  - 완료 기준: 초기 로딩과 자원 생성이 동일한 검증된 설정을 읽으며 설정 부재를 정상 초기화로 처리하지 않는다.

- [ ] **Task 4.6: 청크 요청 수명주기 및 초기 영역 로딩 구현**
  - 선행 조건: Task 4.5 및 기존 격자·청크 좌표 규칙 확인.
  - 구현 범위·상태 소유자: 청크 로드 요청, 준비·생성 상태의 소유자를 정의하고 초기 영역 요청·중복 병합·요청 소비를 구현한다. 기존 개별 공간 인덱스의 상태를 다시 소유하지 않는다.
  - 후속 연결: Task 4.7~4.8의 자원 생성·스폰 결과와 생성 완료를 연결하고, Task 10B.5에서 카메라 요청을 추가한다.
  - 검증: 중복 요청, 겹치는 초기 영역, 음수 좌표, 생성 실패·재시도와 이미 생성된 청크 재요청을 검사한다. 기존에 없는 청크 언로드 정책은 추가하지 않는다.
  - 완료 기준: 요청과 청크 진행 상태가 일치하고 완료된 청크의 자원이 중복 생성되지 않는 계약이 마련된다.

- [ ] **Task 4.7: 시드 기반 광맥 배치 및 자원 생성 요청 구현**
  - 선행 조건: Task 4.5~4.6 및 기존 광맥 생성 알고리즘 확인.
  - 구현 범위·상태 소유자: 자원 생성 단계가 월드 시드·청크·품목 설정으로 배치를 계산하고 자원 스폰 요청을 게시한다. 생성 원본과 재생성 가능한 공간 인덱스를 구분한다.
  - 후속 연결: Task 4.8에서 요청을 실제 자원 엔티티로 적용한다. 게임플레이용 채굴 로직을 다시 구현하지 않는다.
  - 검증: 동일 시드 재현, 청크 로드 순서 변경, 경계를 넘는 광맥, 품목 간 중복 셀 및 재요청을 검사한다.
  - 완료 기준: 기존 생성 규칙에 맞는 재현 가능한 요청을 만들고 생성되지 않은 자원을 완료 상태로 게시하지 않는다.

- [ ] **Task 4.8: 자원 Authoring·프리팹 DB 및 스폰·공간 등록 연결**
  - 선행 조건: Task 0.6·4.6~4.7 및 기존 ResourceNode·ResourceSpatialIndex·Miner 계약.
  - 구현 범위·상태 소유자: 자원 프리팹을 베이킹하고 자원 생성 소유자가 스폰 요청을 소비하여 타입·매장량·위치·렌더 데이터를 초기화한다. Synchronization의 기존 자원 인덱스와 청크 생성 결과를 연결한다.
  - 후속 연결: 기존 채굴·고갈 처리 및 Task 10B.5·10C.2에서 실제 월드 확장과 시각 결과를 확인한다.
  - 검증: 요청→스폰→공간 조회→채굴→고갈 흐름, 중복 생성, 프리팹·설정 오류와 부분 스폰 실패를 실제 그룹 경계에서 검사한다.
  - 완료 기준: 설정과 청크 요청으로 생성한 자원이 기존 채굴 파이프라인에 참여하고 실패 시 고아 요청·중복 자원·잘못된 청크 완료 상태가 없다.

- [ ] **Task 4.9: 바닥 설정 로딩 및 바이옴 생성 계약 구현**
  - 선행 조건: Task 4.5~4.6의 월드·청크 계약, 백업 FloorGenerationConfig·FloorBiomeGeneration 확인.
  - 구현 범위·상태 소유자: 바닥 설정과 기존 바이옴·변형 선택 규칙을 연결하고 렌더러가 조회할 생성 데이터·결과 계약을 마련한다. 바닥 표현이 자원·건물 점유의 원본을 소유하지 않도록 한다.
  - 후속 연결: Task 10B.6에서 청크 메쉬·머티리얼·표시 수명주기를 구현한다.
  - 검증: 동일 설정·좌표의 재현성, 청크 경계, 자원 셀과의 기존 바닥 규칙, 잘못된 설정·시각 변형 참조를 검사한다.
  - 완료 기준: 확인된 바닥 생성 규칙에 따른 결과를 렌더 계층이 사용할 수 있고 월드 시뮬레이션 상태와 책임이 분리된다.

---

## ⚙️ [Phase 5] Crafter (제작기 및 레시피)

- [x] **Task 5.1: Recipe 데이터 구조 정의 (BlobAsset / Unmanaged Struct)**
- [x] **Task 5.2: Crafter 재료 수집 및 제작 진행 시스템 구현**
  - [x] 입력 버퍼 재료 확인 → 소모 처리 → 제작 진행도 누적 → 결과물 출력 (선소비 모델 및 정책 B 만석 대기 완주)
  - [x] `CrafterStateDecision` 컴포넌트 분리 도입: `CrafterDecisionSystem`의 영속 상태 직접 수정 제거 및 `CrafterStateApplySystem`에서 상태 전이 일괄 반영
  - [x] 다중 부산물(Byproducts) 분배 및 All-or-Nothing 출력 공간 검증 (`Phase5CrafterExecutionTests` Test07, Test08 Pass)
  - [x] `ProductResult` 전용 버퍼를 통한 지연 스폰 및 수명주기 정리 (`ItemLifecycleApplySystem` 연결)
- [x] **Task 5.3: 제작 취소 및 레시피 변경 롤백 규칙 구현**
  - [x] `CrafterRecipeCommandSystem` 구현: `CommandGroup`에서 레시피 변경 및 `StorageFilter` 원자적 동기화 완료
  - [x] 레시피 변경 시 진행 중 제작 롤백 및 잔여 재료의 `ProductItemElement` (Slot 1+ 부산물 슬롯) 배출 이관
  - [x] `WaitingForByproductOutput` 상태 진입 및 출력물 잔류 시 새 레시피 재료 입고 원천 차단 검증 (`Phase5RecipeChangePipelineTests` Test01, Test02 Pass)

---

## 🔀 [Phase 6] Splitter / Merger (분배기 및 합류기)

- [x] **Task 6.1: 라우팅 규칙 및 데이터 계약 정의**
  - 선행 조건: Phase 2 벨트 및 Phase 3 입출력 계약 확인, 기존 Splitter/Merger 라우팅 규칙 대조 완료.
  - 구현 범위·상태 소유자: 기존 유연한 우회 분배(Work-conserving) 동작을 유지하도록 `SplitterRoutingState`, `MergerRoutingState`, 슬림화된 `RoutingTransferDecision(Item, SourceBelt, TargetBelt)`, 범용 `PlacementStamp` 계약 정의. Splitter는 가장 먼저 설치된 입력 벨트를 기준으로 `forward -> right -> left`, Merger는 가장 먼저 설치된 출력 벨트를 기준으로 `back -> left -> right` 순환. 성공한 전달에만 cursor 진행. 아이템 위치·소유권은 기존 Item 계약 유지.
  - 배치 순서 계약: `PlacementStamp(Tick, Order)`는 권위 있는 건물 배치 요청이 확정될 때 결정하고 공사 현장 및 실제 건물 엔티티까지 그대로 전달. 같은 Tick의 `Order`는 요청 입력의 결정적 순서에서 산출하며 전역 Sequence Singleton 증가나 `Entity.Index/Version`을 순서 원본으로 사용하지 않음. Stamp 미할당은 값 센티널이 아니라 컴포넌트 부착 여부로 구분.
  - 데이터 책임: 영속 라우팅 상태는 StateApply 소유, DecisionGroup은 읽기 및 프레임 `RoutingTransferDecision` 산출. `RoutingTransferDecision`은 고빈도 Frame Decision으로 `TargetBeltPosition`, `PortDirection`, `PortIndex`를 배제하고 `(Item, SourceBelt, TargetBelt)`만 소유. 실제 나간 포트 인덱스는 `TargetBelt` 위치와 `ForwardDirection`의 상대 각도로 실시간 역산(`RoutingDirectionUtility.GetSplitterPortIndex`).
  - 연결 변경 및 인계 계약: 아이템은 입력 벨트 종단(`Progress=1.0`)에서 출력 벨트 시작점(`Progress=0.0`)으로 원자적 직결 인계(Direct Hand-off)되므로 분배기 타일 내부 체류/갇힘 상태가 없으며 `SplitterRetainedItemElement` 버퍼는 배제. 선택된 입출력 벨트 소멸 시 남은 후보 중 가장 먼저 설치된 벨트로 기준선을 재선택(Task 6.5).
  - 후속 연결: Task 6.2~6.5에서 경합·이동·연결 변경 구현. `PlacementStamp` 발급과 요청 → 현장 → 건물 전달은 Task 7.2~7.5의 권위 있는 건설 경로에서 연결.
  - 검증: `Phase6RoutingContractTests`에서 4방향 상대 포트 순서, 입출력 벨트 방향 판정, cursor 순환, Frame Decision 활성 상태, 포트 역산 유틸리티, `PlacementStamp`의 Tick/Order 비교 계약 검증 통과. 전체 EditMode 124/124 Pass.
  - 완료 기준: 기존 게임 규칙, 쓰기 책임, 실행 Phase, 후속 소비 경계가 데이터 계약으로 고정되고 Task 6.2~6.5 구현에 필요한 모호성 제거.

- [x] **Task 6.2: 공유 목적지 경합 해결**
  - 선행 조건: Task 6.1.
  - 구현 범위·상태 소유자: `ReservationGroup` (Phase 3)에서 실행되는 `BeltDestinationReservationSystem` 구현. 외부 건물 출고(`BuildingItemOutputDecision`)와 분배기/합류기 라우팅(`RoutingTransferDecision`)의 공유 대상 벨트 입구(`Progress = 0.0f`) 진입 경합을 단일 원자적으로 조율.
  - 경합 규칙: (1) 벨트 자체 이동은 `BeltMovement` 파이프라인에 전담시켜 전체 벨트 풀 스캔 제거 및 O(외부 후보 수)의 초경량화 달성, (2) 대상 벨트 입구에 `ItemSpacing(0.25f)` 여유 공간 및 정원(4개) 여유가 확인된 경우에만 진입 허용, (3) 동일 대상 벨트를 노리는 외부 후보들 간에는 `PlacementStamp(Tick, Order)` 순서로 단 1개만 승인.
  - 승인·거부 계약: 거부된 후보는 `IEnableableComponent` 비활성화(`SetComponentEnabled=false`) 및 `CanOutput = false`로 즉시 처리하여 `StateApplyGroup` 실행 및 미소비 결정을 원천 차단. `WorldInvariantValidationSystem`에 `RoutingTransferDecision` 활성 잔류 감시 불변식 추가.
  - 후속 연결: Splitter·Merger의 구체적인 후보 선택 및 cursor 갱신은 Task 6.3~6.4에서 연결.
  - 검증: `Phase6BeltDestinationReservationTests` 6개 단위 테스트 전수 통과 (빈 벨트 단일 출고 승인, 다중 건물 출고 PlacementStamp 경합, 건물 출고 vs 라우팅 전달 경합, 공간 부족 시 거부, 공간 충분 시 정상 승인, 무요청 시 조기 반환). 전체 EditMode 130/130 Pass.
  - 완료 기준: 승인된 이동만 반영되며 기존 물류와 함께 최소 간격을 완벽히 유지.

- [x] **Task 6.3: Splitter 분배 구현**
  - 선행 조건: Task 6.1~6.2.
  - 구현 범위·상태 소유자: `SplitterDecisionSystem` (`DecisionGroup`) 구현으로 입력 벨트 종단 아이템 감지 및 Forward -> Right -> Left 순환 포트 탐색(Work-conserving 우회) 처리. `RoutingApplySystem` (`StateApplyGroup`) 구현으로 승인된 이동 적용, 대상 벨트 시작점(Progress 0.0f) 인계 및 실제 배출 포트 기준 `OutputCursor` 전진(`AdvanceCursor`). 영속 상태와 의사결정 완벽 분리.
  - 후속 연결: 연결 소멸과 잔류 아이템은 Task 6.5에서 검증한다.
  - 검증: `Phase6SplitterPipelineTests` 5개 단위 테스트 전수 통과 (정상 3방향 라운드로빈 순환, 차단 포트 우회 분배, 전체 차단 시 아이템 보존 및 커서 동결, 차단 해제 시 분배 재개, PlacementStamp 우선순위 기준선 자동 선택). 전체 EditMode 135/135 Pass.
  - 완료 기준: 확인된 분배 순서를 지키며 실패한 이동에서 아이템과 순환 상태가 잘못 전진하지 않는다.

- [x] **Task 6.4: Merger 합류 구현**
  - 선행 조건: Task 6.1~6.2.
  - 구현 범위·상태 소유자: `MergerDecisionSystem` (`DecisionGroup`) 구현으로 단일 출력 벨트 수용 공간 사전 검사(정체 시 커서 동결 및 대기) 및 Back(0) -> Left(1) -> Right(2) 순환 포트 탐색(Work-conserving 우회) 처리. `RoutingApplySystem` (`StateApplyGroup`) 확장으로 승인된 합류 이동 적용, 대상 벨트 시작점(Progress 0.0f) 인계 및 실제 유입 포트 기준 `InputCursor` 전진(`AdvanceCursor`). 영속 상태와 의사결정 완벽 분리.
  - 후속 연결: 연결 소멸은 Task 6.5, 복합 경합은 Task 6.6에서 검증한다.
  - 검증: `Phase6MergerPipelineTests` 5개 단위 테스트 전수 통과 (동시 도착 3방향 순환, 비어있는 포트 우회 합류, 출력 정체 시 대기 및 커서 동결, 정체 해제 시 합류 재개, PlacementStamp 기준 출력선 자동 선택). 전체 EditMode 140/140 Pass.
  - 완료 기준: 기존 우선순위·순환 규칙과 일치하고 중복 전달·규칙 위반 대기가 없다.

- [x] **Task 6.5: 연결 변경 및 잔류 아이템 처리**
  - 선행 조건: Task 6.3~6.4.
  - 구현 범위·상태 소유자: V2 직결 인계(Direct Hand-off) 원칙에 따라 라우터 타일 자체 버퍼를 배제하고 연결 벨트 소멸 시 입력 벨트 종단 아이템 안전 보존 확립. `SplitterDecisionSystem` 및 `MergerDecisionSystem`에 기준선(InputBelt/OutputBelt) 소멸 시 `PlacementStamp` 우선순위 자동 재바인딩 및 커서 0 리셋(`startCursor = 0`) 반영. `RoutingApplySystem`에 대상 벨트 및 아이템 소멸 시 결정을 안전하게 드롭하는 Fail-safe 계약 적용.
  - 후속 연결: 실제 철거 요청과의 연결은 Phase 7에서 완료한다.
  - 검증: `Phase6ConnectionChangeTests` 6개 단위 테스트 전수 통과 (Splitter 입력 벨트 파괴 시 기준선 전환 및 커서 리셋, Splitter 전체 입력 소멸 시 대기 및 재연결 시 라우팅 재개, Splitter 출력 포트 소멸 시 우회 분배, Merger 출력 벨트 파괴 시 기준선 전환 및 커서 리셋, Merger 전체 출력 소멸 시 아이템 보존 및 재연결 시 합류 재개, 결정 후 적용 전 벨트/아이템 소멸 시 안전 무효화). 전체 EditMode 146/146 Pass.
  - 완료 기준: 연결 변경으로 아이템 유실·중복·유효하지 않은 포트 참조가 남지 않는다.

- [x] **Task 6.6: 전체 물류 파이프라인 통합 검증**
  - 선행 조건: Task 6.2~6.5 및 생산물 출력에 사용하는 기존 구현의 보완 검증.
  - 구현 범위·상태 소유자: 채굴(Miner)·제작(Crafter)·창고(Storage)·벨트(Belt)·분배(Splitter)·합류(Merger)를 실제 최상위 `GameSimulationGroup` 루트 루프(Phase 1~6 전체 하위 그룹)로 결합하여 종합 검증. 별도의 검증용 상태 소유자 생성 없이 순수 V2 데이터 계약만으로 파이프라인 무결성 입증.
  - 후속 연결: 설치·철거 중 물류 변화는 Phase 7의 수명주기 검증에 연결한다.
  - 검증: `Phase6EndToEndPipelineTests` 4개 통합 테스트 전수 통과 (End-to-End 전체 생산·물류·분배 순환 흐름, Splitter vs Storage 단일 벨트 경합 PlacementStamp 중재, Merger 출력 벨트 정체 시 백프레셔 전파 및 정체 해제 시 자동 복구, 100틱 연속 시뮬레이션 동안 WorldInvariantValidationSystem 0 Violation 유지). 전체 EditMode 150/150 Pass.
  - 완료 기준: 관련 통합 테스트가 통과하고 각 경로의 실행·동기화 계약이 확인된다.

---

## 🏗️ [Phase 7] Construction Core (배치, 건설 현장, 철거)

이 Phase의 완료 범위는 건설 코어다. 테스트에서 유효한 자재 도착·철거 실행 결과를 전달하여 검증하고, 실제 드론 운반·사용자 철거는 Task 9.17·9.20에서 연결한다. 테스트용 결과 주입을 즉시 건설·즉시 철거 같은 런타임 게임 규칙으로 추가하지 않는다.

- [ ] **Task 7.1: 공간 점유·예약의 소유권 계약 정의**
  - 선행 조건: 현재 `BuildingFootprint`, `BuildingSpatialIndex`, `BuildingSpatialSyncSystem`과 Phase 6 연결 계약 확인.
  - 구현 범위·상태 소유자: 기존 크기·위치 데이터를 재사용하고 권위 있는 점유·예약 상태와 파생 조회 인덱스의 역할, 등록·해제 시점을 정의한다.
  - 후속 연결: 실제 예약과 현장 생성은 Task 7.2에서 구현한다. 레거시 `ChunkMapSystem`을 전제로 새로 만들지 않는다.
  - 검증: 다중 셀·회전·음수 좌표·동일 프레임 충돌의 예약 및 점유 상태 전이를 대조한다.
  - 완료 기준: 원본 상태, 수정 권한, 인덱스 반영 시점이 하나의 계약으로 정리된다.

- [ ] **Task 7.2: 원자적 배치 예약 및 공사 현장 생성**
  - 선행 조건: Task 7.1, 관련 건물 정의·공사 비용 데이터.
  - 구현 범위·상태 소유자: 여러 후보 footprint를 전부 예약한 뒤 건설 요청을 현장으로 전환한다. 권위 있는 배치 요청에서 `PlacementStamp(Tick, Order)`를 확정하고 현장에 그대로 전달한다. 공간 예약과 현장 상태는 각 소유자가 처리한다.
  - 후속 연결: 플레이어 입력·미리보기·복사는 Task 10B.2에서 연결한다.
  - 검증: 일부 셀 충돌, 잘못된 정의, 중복 요청, 생성 실패 시 모든 임시 예약의 롤백을 검사한다.
  - 완료 기준: 부분 현장·고아 예약 없이 요청이 성공하거나 전체 취소된다.

- [ ] **Task 7.3.1: 건물 Authoring·프리팹 DB 및 정의 연결**
  - 선행 조건: Task 0.6·3.5 및 기존 건물 타입·footprint 계약.
  - 구현 범위·상태 소유자: 건물 타입·프리팹·크기·렌더 데이터를 베이킹하고 기본 건물 설정과 연결한다. 정의 데이터는 생성 입력이며 런타임 점유·예약은 공간 소유자가 관리한다.
  - 후속 연결: 이 하위 작업을 먼저 완료한 뒤 Task 7.3에서 실제 인스턴스 생성에 사용한다. 전력·정거장·연구 타입은 각 도메인 구현 시 정의를 확장한다.
  - 검증: 잘못된 타입·중복 항목·프리팹 누락·footprint와 회전·시각 중심 및 베이킹/스폰의 컴포넌트 중복을 검사한다.
  - 완료 기준: 현재 이식 대상의 건물 정의·프리팹이 유효하게 게시되고 생성 소유자가 일관되게 조회할 수 있다.

- [ ] **Task 7.3: 공통 건물 생성 경로 구현**
  - 선행 조건: Task 7.1·7.3.1 및 현재 이식된 건물 타입의 데이터 계약.
  - 구현 범위·상태 소유자: 건물 생성 소유자가 베이킹된 프리팹과 Task 3.5의 기본 설정으로 공통 공간·방향·타입 데이터와 타입별 필수 컴포넌트를 초기화한다. 생성 요청 또는 공사 현장의 `PlacementStamp`를 실제 건물 엔티티에 그대로 전달한다.
  - 후속 연결: 아직 미이식인 전력·정거장·연구 건물의 초기화는 해당 Phase에서 확장한다.
  - 검증: 타입별 필수 상태, 생성 실패, 점유 등록 전에 미완성 인스턴스가 관찰되는지 검사한다.
  - 완료 기준: 현재 이식 대상 건물이 일관된 초기 상태로 생성되고 실패 시 잔류 엔티티·점유가 없다.

- [ ] **Task 7.4: 공사 자재 요구량·수령 계약 구현**
  - 선행 조건: Task 7.2 및 Item 소유권·저장 계약.
  - 구현 범위·상태 소유자: 현장이 요구량·도착량을 소유하고 아이템 소유권 경계를 통해 자재를 수령한다. 운송 예약 중인 수량과 도착한 자재를 구분하는 계약을 정의한다.
  - 후속 연결: 실제 아이템·목적지 용량 예약과 드론 운송은 Task 9.6·9.12·9.20에서 구현·연결한다.
  - 검증: 부분 도착, 초과·중복 전달, 잘못된 품목, 취소된 현장으로의 도착을 검사한다.
  - 완료 기준: 자재 수량·품목·소유권이 일치하며 수령 성공 여부를 운반 측에 전달할 수 있다.

- [ ] **Task 7.5: 공사 완료 및 건물 전환 구현**
  - 선행 조건: Task 7.3~7.4.
  - 구현 범위·상태 소유자: 현장 완료 소유자가 요구량과 예약 정리를 확인하고 자재 소비·건물 생성·점유 인계를 요청한다. 공간 소유자는 최종 등록까지 예약을 유지한다.
  - 후속 연결: 드론 예약 상태와의 실제 연동은 Task 9.20에서 검증한다.
  - 검증: 마지막 자재 도착, 중복 완료, 생성 실패, 예약 미해제, 같은 프레임 다른 배치와의 경쟁을 검사한다.
  - 완료 기준: 중복 소비·중복 생성·점유 공백 없이 현장에서 건물로 전환된다.

- [ ] **Task 7.6: 공사 취소 및 도착 자재 반환 구현**
  - 선행 조건: Task 7.2·7.4~7.5.
  - 구현 범위·상태 소유자: 현장 취소 전이, 도착 자재 반환, 공간 예약 해제와 후속 운송 취소 결과 계약을 구현한다.
  - 후속 연결: 운송 중 취소·화물 복구는 Task 9.15~9.16·9.20에서 연결한다.
  - 검증: 빈 현장·부분 도착·완료 직전 취소, 반복 취소 및 같은 프레임 완료 요청을 검사한다.
  - 완료 기준: 확인된 취소 우선순위를 지키고 자재·현장·공간 예약이 중복 정리되거나 남지 않는다.

- [ ] **Task 7.7: 건물 철거 코어 및 내용물 반환 구현**
  - 선행 조건: Task 7.3, Item 소유권 경계 및 Phase 6 연결 변경 처리.
  - 구현 범위·상태 소유자: 유효한 철거 실행 결과를 받아 건물 소유 아이템·건설 재료를 반환하고 공간을 해제한다. 건물 제거 소유자가 완료·실패 경계를 조정한다.
  - 후속 연결: 전력 등록 해제는 Task 8.7, 드론 철거 실행은 Task 9.17, 정거장 드론 재배치는 Task 9.19에서 완료한다.
  - 검증: 빈 건물·내용물 보유 건물·중복 철거·무효 대상·파괴 불가 대상과 인접 물류 연결을 검사한다.
  - 완료 기준: 현재 지원하는 건물의 내용물·공간·참조를 일관되게 정리하고 후속 도메인 연결 범위를 명시한다.

- [ ] **Task 7.8: 건설 코어 수명주기 통합 검증**
  - 선행 조건: Task 7.2~7.7.
  - 구현 범위·상태 소유자: 생성·수령·완료·취소·철거를 실제 그룹으로 연결하고 각 소유자의 결과를 검증한다.
  - 후속 연결: Phase 8·9의 전력·운송·정거장 연동 검증은 별도로 추적한다.
  - 검증: 테스트용 유효 도착 결과를 사용해 성공 흐름과 동일 프레임 취소·완료·철거 경쟁을 재현한다.
  - 완료 기준: 코어 통합 테스트와 공간·아이템 불변식이 통과한다. 실제 운반·플레이 검증 완료로 기록하지 않는다.

---

## ⚡ [Phase 8] Power Grid (전력 네트워크)

- [ ] **Task 8.1: 전력 설정·컴포넌트·상태 소유권 정의**
  - 선행 조건: 현재 생산·공간·건물 생성 계약과 기존 전력 설정 확인.
  - 구현 범위·상태 소유자: 전신주·발전기·소비자·망 상태를 정의하고 토폴로지, 공급 범위 조회, 외부 공급 데이터의 책임을 구분한다.
  - 후속 연결: 망 구성·연결·배분·연료는 Task 8.2~8.6에서 구현한다.
  - 검증: 설정의 누락·잘못된 값과 도메인 간 데이터 읽기·쓰기 계약을 검사한다.
  - 완료 기준: 생산 도메인이 전력망 내부 구조를 몰라도 공급 결과를 소비할 수 있는 계약이 마련된다.

- [ ] **Task 8.2: 전신주 추가 및 망 생성·병합 구현**
  - 선행 조건: Task 8.1.
  - 구현 범위·상태 소유자: 전력 토폴로지 소유자가 connection range에 따라 전신주 연결과 망 생성·병합을 처리한다.
  - 후속 연결: 제거·분리는 Task 8.3, 건물 공급 연결은 Task 8.4에서 구현한다.
  - 검증: 독립 망, 연결 다리 추가, 범위 경계, 공급 범위만 겹치는 사례를 검사한다.
  - 완료 기준: 연결 규칙에 맞는 망만 병합되며 중복 등록·유효하지 않은 망 참조가 없다.

- [ ] **Task 8.3: 전신주 제거 및 망 분리 구현**
  - 선행 조건: Task 8.2.
  - 구현 범위·상태 소유자: 전력 토폴로지 소유자가 제거된 연결을 정리하고 남은 연결 집합으로 망을 재구성한다.
  - 후속 연결: 철거 요청에 의한 실제 제거는 Task 8.7에서 연결한다.
  - 검증: 끝 전신주·연결 다리·마지막 전신주 제거, 반복 제거와 재설치를 검사한다.
  - 완료 기준: 분리된 망과 남은 전신주의 소속이 일치하고 사라진 연결이 남지 않는다.

- [ ] **Task 8.4: 공급 범위 및 건물의 망 연결 구현**
  - 선행 조건: Task 8.2~8.3 및 건물 footprint 계약.
  - 구현 범위·상태 소유자: 공간 조회와 전력 참가자 연결 책임을 구분하고, footprint 전체에서 기존 공급 선택 규칙을 적용한다.
  - 후속 연결: 연결된 참가자의 발전·수요 계산은 Task 8.5에서 구현한다.
  - 검증: 다중 셀·회전 건물, 공급 범위 중첩·경계, 전신주 제거 시 재연결·단절을 검사한다.
  - 완료 기준: 공급 범위와 망 토폴로지를 혼동하지 않고 발전기·소비자의 소속이 갱신된다.

- [ ] **Task 8.5: 발전량·수요 집계 및 공급률 게시 구현**
  - 선행 조건: Task 8.4.
  - 구현 범위·상태 소유자: 전력 배분 소유자가 가용 발전량과 수요를 집계하고 소비자별 공급 결과를 게시한다. 연료 기반 가용량·실제 사용량 계약도 정의한다.
  - 후속 연결: 실제 석탄 연료 반영은 Task 8.6, 생산 소비는 Task 8.7에서 연결한다.
  - 검증: 발전 없음·수요 없음·공급 부족·공급 충분·복수 망의 배분을 검사한다.
  - 완료 기준: 공급 결과가 유효 범위에 있으며 망 간 발전·수요가 섞이지 않는다.

- [ ] **Task 8.6: 석탄 입고 및 연료 소비 구현**
  - 선행 조건: Task 8.5 및 Item·Storage 입고·소비 계약.
  - 구현 범위·상태 소유자: 기존 저장 경계로 석탄을 받고 연료 상태 소유자가 가용 에너지와 실제 발전에 사용한 에너지를 관리한다.
  - 후속 연결: 건물 생성·철거와 생산 정지는 Task 8.7에서 통합 검증한다.
  - 검증: 연료 부족·소진·재공급·수요 감소에서 아이템 소비량과 실제 발전량을 대조한다.
  - 완료 기준: 연료로 뒷받침되지 않는 공급이나 중복·불필요한 연료 소비가 없다.

- [ ] **Task 8.7: 생산 및 건설 수명주기 통합**
  - 선행 조건: Task 8.2~8.6, Task 7.3·7.7 및 관련 Crafter 보완 검증.
  - 구현 범위·상태 소유자: 채굴·제작이 공급 데이터만 소비하도록 연결하고 설치·철거에 따른 전력 등록·해제를 완료한다.
  - 후속 연결: 드론 충전은 Task 9.14, 연구 전력 소비는 Task 10A.3에서 연결한다.
  - 검증: 공급 중단·복구에 따른 진행량, 발전기·소비자·전신주 철거, 타입별 초기화를 검사한다.
  - 완료 기준: 생산이 전력 내부 API의 실행을 제어하지 않고 게시된 결과를 사용하며 철거 후 전력 참조가 남지 않는다.

---

## 🛸 [Phase 9] Drone System (대형 도메인 이식)

중간 마일스톤은 **9A 기반(9.1~9.3 및 9.3.1) → 9B 계획·예약·배차(9.4~9.9) → 9C 정상 운반·귀환·충전(9.10~9.14) → 9D 취소·복구·통합(9.15~9.20) → 9E 게임 시작 초기화(9.21~9.22)**이다. 9.9까지 배차 확정, 9.14까지 정상 반복 루프, 9.20까지 실패 복구와 건설 연결, 9.22까지 시작 시설과 초기 지급의 실제 구성 경로를 완료한다.

### 9A: 상태 계약과 정거장 기반

- [ ] **Task 9.1: 상태 전이표 및 Lifecycle Owner 계약 확정**
  - 선행 조건: 기존 드론 작업·운반·복구 규칙과 Item·Construction 계약 확인.
  - 구현 범위·상태 소유자: 작업·드론·예약 상태를 구분하고 정상·취소·실패 전이, 결과 이벤트, 부수 효과와 적용 책임을 정의한다. `Pending → Reserved → Assigned → PickingUp → Delivering → Completed`를 기존 수량·동시 운반 규칙과 대조한다.
  - 후속 연결: 각 Task가 필요한 전이를 이 계약 아래 구현하고, 복구 전이는 Task 9.15~9.16에서 연결한다.
  - 검증: 허용·금지 전이, 중복·지연 이벤트와 부분 완료의 기대 결과를 상태표로 대조한다.
  - 완료 기준: 작업 종료, 예약 정리, 할당 해제, 화물 처리의 책임과 순서가 명확하다.

- [ ] **Task 9.2: 정거장 등록·범위·네트워크 구현**
  - 선행 조건: Task 9.1 및 건물 공간 계약.
  - 구현 범위·상태 소유자: 정거장 네트워크 소유자와 공간 범위 조회를 구분해 등록·병합·분리·해제를 구현한다.
  - 후속 연결: 작업 범위 판단은 Task 9.5, 정거장 파괴의 드론 처리는 Task 9.19에서 연결한다.
  - 검증: 겹치는 범위, 다중 셀 정거장, 연결 정거장 제거, 범위 밖 대상을 검사한다.
  - 완료 기준: 네트워크와 범위 인덱스가 일치하고 다른 도메인의 상태를 소유하지 않는다.

- [ ] **Task 9.3.1: 드론 Authoring·프리팹 DB 및 활성 엔티티 생성 연결**
  - 선행 조건: Task 0.6·9.1 및 기존 드론 프리팹·Identity 계약 확인.
  - 구현 범위·상태 소유자: 드론 렌더 프리팹과 초기 컴포넌트를 베이킹해 Identity 전환 경계가 활성 드론을 만들 수 있도록 한다. 보관·충전·이동 표시 데이터의 출처를 정하며 표시 객체가 lifecycle을 소유하지 않는다.
  - 후속 연결: 이 하위 작업을 먼저 완료한 뒤 Task 9.3에서 Identity 전환에 사용하고 Task 9.10·9.14·10B.4에서 이동·충전·보관 표시를 연결한다.
  - 검증: 프리팹 누락, 중복 생성·컴포넌트 추가, 생성 실패 시 아이템과 활성 드론의 중복 Identity를 검사한다.
  - 완료 기준: 유효한 드론 프리팹을 통해 활성 엔티티를 생성할 수 있고 실패가 기존 아이템·Identity 상태를 훼손하지 않는다.

- [ ] **Task 9.3: 드론 Identity 전환 및 정거장 수용량 구현**
  - 선행 조건: Task 9.1~9.2·9.3.1 및 Item 소유권·건물 생성 계약.
  - 구현 범위·상태 소유자: 기존 규칙에 필요한 드론 아이템과 활성 드론의 전환, 정거장 보관 상태·수용량 계산을 담당 경계에서 구현한다.
  - 후속 연결: 귀환·보관은 Task 9.13, 충전은 Task 9.14에서 연결한다.
  - 검증: 전환 성공·실패·반복 요청, 일반 아이템과 드론 수용량 경합, 유효하지 않은 정거장을 검사한다.
  - 완료 기준: 중복 Identity·고아 소유권 없이 전환되고 실제 보관 상태와 수용량이 일치한다.

### 9B: 작업 계획·예약·배차

- [ ] **Task 9.4: 작업 생성·변경·취소 요청 경로 구현**
  - 선행 조건: Task 9.1~9.3.
  - 구현 범위·상태 소유자: 작업 명령을 권위 있는 작업 데이터로 전환하고 기존 우선순위·중단·재개·취소 명령의 검증과 소비 경로를 마련한다.
  - 후속 연결: 대상별 계획은 Task 9.5, 활성 작업의 취소 부수 효과는 Task 9.15에서 연결한다.
  - 검증: 무효 대상·중복 명령·종료 작업에 대한 변경과 요청 잔류를 검사한다.
  - 완료 기준: 명령이 유효한 작업 또는 명시적 거부 결과로 소비되고 미지원 경로를 완료로 처리하지 않는다.

- [ ] **Task 9.5: 공급원·목적지 탐색 및 작업 계획 구현**
  - 선행 조건: Task 9.2·9.4와 저장·공사 수령 계약.
  - 구현 범위·상태 소유자: 계획 단계가 네트워크·품목·대상 유효성을 읽어 공급원·목적지 후보를 게시한다. 실제 아이템·용량은 선점하지 않는다.
  - 후속 연결: 선점은 Task 9.6, 후보 우선순위와 배차는 Task 9.8~9.9에서 처리한다.
  - 검증: 공급 부족, 목적지 만석, 범위 변경, 대상 소멸 시 계획 갱신을 검사한다.
  - 완료 기준: 유효한 계획만 게시되고 계획 작성만으로 소유권·예약 상태가 변경되지 않는다.

- [ ] **Task 9.6: 아이템·목적지 용량의 원자적 예약 구현**
  - 선행 조건: Task 9.5 및 목적지 수령·용량 계약.
  - 구현 범위·상태 소유자: 예약 소유자가 실제 아이템과 목적지 용량을 함께 검증·선점하고 완성된 예약만 게시한다. 실패 시 아이템·용량·작업 예약량을 함께 롤백한다.
  - 후속 연결: 게시 이후의 해제·완료·무효화는 Task 9.7에서 구현한다.
  - 검증: 동일 아이템·마지막 용량 경쟁, 중간 실패, 필터·대상 변화, 여러 드론의 부분 수량 예약을 검사한다.
  - 완료 기준: 반쪽 예약·과다 예약·중복 선점이 없고 실패한 획득이 원상복구된다.

- [ ] **Task 9.7: 예약 완료·해제·무효화 구현**
  - 선행 조건: Task 9.6 및 Task 9.1의 전이 계약.
  - 구현 범위·상태 소유자: 예약 소유자가 완료·취소·무효화 결과에 따라 아이템 선점, 목적지 용량, 작업 예약량을 정확히 정리한다.
  - 후속 연결: 전달 결과는 Task 9.12, 실패·취소 결과는 Task 9.15~9.16에서 연결한다.
  - 검증: 중복 해제·완료, 소멸한 아이템·소유자·목적지, 이미 부분 완료한 작업을 검사한다.
  - 완료 기준: 한 예약의 효과가 한 번만 정리되고 음수 수량·잔류 선점이 없다.

- [ ] **Task 9.8: 작업 후보 구성 및 우선순위 선택 구현**
  - 선행 조건: Task 9.5~9.7.
  - 구현 범위·상태 소유자: 스케줄링 단계가 작업·예약에서 파생 후보를 만들고 기존 우선순위·거리·배터리 가능성 규칙에 따라 선택한다. 후보 인덱스는 원본 상태를 소유하지 않는다.
  - 후속 연결: 권위 있는 claim과 할당은 Task 9.9에서 수행한다.
  - 검증: 동률, 처리 불가능한 상위 후보, 취소·소멸된 후보, 후보 갱신 순서를 검사한다.
  - 완료 기준: 소비자가 별도 준비 API를 호출하지 않아도 최신 유효 후보를 선택할 수 있다.

- [ ] **Task 9.9: 배차 확정 및 작업·드론 할당 구현**
  - 선행 조건: Task 9.3·9.7~9.8.
  - 구현 범위·상태 소유자: 배차 후보를 권위 있게 claim하고 할당 상태를 전이 계약에 따라 반영한다. 후보 선택과 실제 획득 성공을 구분한다.
  - 후속 연결: 이동은 Task 9.10, 종료 후 재배차·취소 연결은 Task 9.12~9.15에서 완성한다.
  - 검증: 여러 드론의 동일 후보 경쟁, claim 직전 대상 무효화, 귀환 에너지 부족을 검사한다.
  - 완료 기준: 하나의 예약이 중복 배차되지 않고 실패한 claim이 할당·예약을 남기지 않는다.

### 9C: 정상 운반·귀환·충전

- [ ] **Task 9.10: 이동·도착 판정 및 배터리 소비 구현**
  - 선행 조건: Task 9.9.
  - 구현 범위·상태 소유자: 이동 단계가 확정된 경로의 위치·배터리를 갱신하고 도착·이동 실패 결과를 게시한다. 작업 완료와 예약 정리를 직접 처리하지 않는다.
  - 후속 연결: 픽업·전달은 Task 9.11~9.12, 귀환은 Task 9.13, 복구는 Task 9.15~9.16에서 연결한다.
  - 검증: 정상 도착, 큰 DeltaTime, 중복 도착 이벤트, 목표 무효화·배터리 부족을 검사한다.
  - 완료 기준: 이동량·에너지·도착 결과가 일치하고 실패를 정상 완료로 처리하지 않는다.

- [ ] **Task 9.11: 픽업 및 화물 소유권 이전 구현**
  - 선행 조건: Task 9.6~9.7·9.10 및 Item 소유권 계약.
  - 구현 범위·상태 소유자: 운반 단계가 예약된 아이템의 픽업을 요청하고 Item 소유자가 출발지에서 드론으로 이전한다. 성공·실패 결과로 다음 전이를 결정한다.
  - 후속 연결: 전달은 Task 9.12, 실패 시 화물·예약 처리는 Task 9.15~9.16에서 연결한다.
  - 검증: 품목·예약 불일치, 출발지 제거, 중복 픽업, 이전 실패를 검사한다.
  - 완료 기준: 실제 이전 성공 전에는 전달 단계로 진행하지 않고 아이템이 한 소유자에게만 속한다.

- [ ] **Task 9.12: 전달 및 정상 작업 완료 구현**
  - 선행 조건: Task 9.7·9.11 및 목적지 수령 계약.
  - 구현 범위·상태 소유자: Item 소유자가 화물을 목적지로 이전하고 결과 이벤트를 게시한다. Lifecycle Owner는 결과에 따라 작업 전이와 예약 완료·할당 정리를 조정한다.
  - 후속 연결: 재배차·귀환은 Task 9.13, 전달 실패 복구는 Task 9.15~9.16, 공사 완주는 Task 9.20에서 연결한다.
  - 검증: 정상·부분 수량 완료, 중복 결과, 목적지 만석·소멸·수령 거부를 검사한다.
  - 완료 기준: 수령 성공한 수량만 완료되고 화물·작업량·예약량이 일치한다.

- [ ] **Task 9.13: 귀환 목적지 선택 및 정거장 보관 구현**
  - 선행 조건: Task 9.2~9.3·9.9~9.12.
  - 구현 범위·상태 소유자: 기존 연속 배차·귀환 규칙에 따라 다음 행동을 결정하고 정거장 수용량 계약으로 드론을 보관한다.
  - 후속 연결: 충전은 Task 9.14, 정거장 파괴 중 재배치는 Task 9.19에서 연결한다.
  - 검증: 다음 작업 존재, 정거장 만석·소멸, 대체 정거장, 수용 가능한 곳이 없는 경우를 검사한다.
  - 완료 기준: 중복 보관·용량 초과 없이 재배차·귀환·대기 상태가 기존 규칙과 일치한다.

- [ ] **Task 9.14: 전력 기반 충전 및 재배차 상태 구현**
  - 선행 조건: Task 9.13 및 Task 8.5~8.7.
  - 구현 범위·상태 소유자: 충전 상태 소유자가 수요와 실제 공급 결과를 통해 배터리를 갱신하고 충전·보관·배차 가능 상태를 게시한다.
  - 후속 연결: 충전과 보관의 시각 표현은 Task 10B.4에서 연결한다.
  - 검증: 무전력·부분 공급·완충, 충전 중 배차, 공급 복구를 검사한다.
  - 완료 기준: 배터리와 전력 사용량이 일치하고 정상 운반→귀환→충전→재배차 루프가 완주한다.

### 9D: 취소·복구·직접 작업 및 통합

- [ ] **Task 9.15: 취소·실패 전이 및 할당·예약 정리 구현**
  - 선행 조건: Task 9.1·9.7 및 Task 9.9~9.14의 결과 경로.
  - 구현 범위·상태 소유자: Lifecycle Owner가 취소·실패 결과를 소비하고 단계별로 할당·예약·후속 행동을 전이한다. 화물을 가진 실패는 복구 요청으로 연결한다.
  - 후속 연결: 화물의 실제 복구는 Task 9.16, 공사 취소 연결은 Task 9.20에서 완료한다.
  - 검증: 배차 전·이동 중·픽업 후·전달 직전 취소, 완료와 취소의 경합, 중복·지연 이벤트를 검사한다.
  - 완료 기준: 상태표에 따라 정리가 한 번만 적용되며 화물 복구가 필요한 상태를 종료로 누락하지 않는다.

- [ ] **Task 9.16: 운반 화물 회수·복원 구현**
  - 선행 조건: Task 9.11·9.13·9.15 및 Item 저장·월드 복원 계약.
  - 구현 범위·상태 소유자: 복구 단계가 기존 대체 저장·월드 복원 규칙에 따라 처리를 요청하고 결과를 Lifecycle Owner에 전달한다. 실제 아이템 이전은 Item 경계가 담당한다.
  - 후속 연결: 복원된 월드 아이템의 후속 회수는 Task 9.18에서 연결한다.
  - 검증: 목적지 소멸, 대체 저장소 만석, 복원 실패·재시도, 반복 복구 결과를 검사한다.
  - 완료 기준: 화물 유실·복제·고아 소유권 없이 복구되고 실패 시 재시도 가능한 상태가 명확하다.

- [ ] **Task 9.17: 드론 철거 작업 및 건설 코어 연결**
  - 선행 조건: Task 7.7, Task 9.4~9.10·9.15.
  - 구현 범위·상태 소유자: 직접 철거 작업을 계획·배차·실행하고 결과를 건물 제거 소유자에 전달한다. 건물 제거 성공·보류·실패를 작업 결과와 연결한다.
  - 후속 연결: 정거장 특수 처리는 Task 9.19, 입력은 Task 10B.2에서 연결한다.
  - 검증: 유효 대상 도착, 대상 소멸, 중복 철거, 취소, 건물 제거 보류를 검사한다.
  - 완료 기준: 유효한 드론 실행 결과로만 사용자 철거가 진행되고 작업과 건물 결과가 일치한다.

- [ ] **Task 9.18: 월드 아이템 회수 작업 구현**
  - 선행 조건: Task 9.5~9.16 및 월드 아이템 공간 조회 계약.
  - 구현 범위·상태 소유자: 월드 아이템의 회수 계획·선점·픽업·전달을 기존 예약·운반 경로와 연결한다. 공간 인덱스는 조회용으로 사용한다.
  - 후속 연결: 사용자 회수 요청과 표시 연결은 Task 10B.2·10B.4에서 완료한다.
  - 검증: 동일 아이템 경쟁, 아이템 이동·저장 전환·소멸, 범위 밖 대상, 목적지 용량 부족을 검사한다.
  - 완료 기준: 유효한 월드 아이템만 한 번 회수되고 소유권 변화 후 오래된 작업·예약이 남지 않는다.

- [ ] **Task 9.19: 정거장 철거 및 보관 드론 재배치 구현**
  - 선행 조건: Task 7.7, Task 9.2~9.3·9.13~9.17.
  - 구현 범위·상태 소유자: 건물 제거·정거장 수용량·드론 lifecycle 경계를 통해 기존 보관 드론 재배치와 철거 보류 규칙을 구현한다.
  - 후속 연결: 표시·사용자 피드백은 Task 10B.3~10B.4에서 연결한다.
  - 검증: 충분한 대체 수용량, 일부만 수용 가능, 대체 정거장 소멸, 충전 중 철거, 반복 요청을 검사한다.
  - 완료 기준: 재배치할 수 없는 드론이 있는 상태에서 정거장이 부당하게 제거되지 않고 드론 Identity·수용량이 보존된다.

- [ ] **Task 9.20: 건설 운반 포함 전체 흐름·경합·부하 검증**
  - 선행 조건: Task 9.1~9.19, Phase 7~8 코어 검증.
  - 구현 범위·상태 소유자: 공사 자재 공급, 건물 입출고, 철거, 회수, 충전과 취소·복구를 실제 실행 그룹에서 연결한다. 각 소유자의 상태를 검증한다.
  - 후속 연결: 플레이·UI 검증과 전체 Profiler 비교는 Phase 10에서 수행한다.
  - 검증: 다중 드론·다중 작업·마지막 용량 경쟁, 공사 취소·완료 경합, 운송 중 대상 소멸과 반복 부하를 검사한다.
  - 완료 기준: 아이템·예약·할당·네트워크 불변식과 통합 테스트가 통과하고 부하 조건·측정 결과·미검증 범위가 기록된다.

### 9E: 게임 시작 초기화

- [ ] **Task 9.21: 시작 아이템 설정 로딩 및 초기 지급 계약 구현**
  - 선행 조건: 기존 ItemType·스택·소유권 계약과 백업 StartingItemConfig 스키마 확인.
  - 구현 범위·상태 소유자: 기존 품목·수량 목록을 검증하여 초기 지급 정의로 게시한다. 정의 로더는 아이템을 직접 생성하지 않고 실제 지급은 부트스트랩과 기존 Item 생성 경계에서 처리한다.
  - 후속 연결: Task 9.22에서 주 시설 생성 후 초기 아이템·드론 아이템을 지급한다.
  - 검증: 잘못된 품목, 음수·중복 항목, 빈 목록, 설정 누락 및 재로딩의 기존 처리 규칙을 검사한다.
  - 완료 기준: 초기 지급 정의가 유효한 상태로 한 번 게시되고 설정 로딩만으로 중복 지급이 발생하지 않는다.

- [ ] **Task 9.22: 주 시설·초기 전력·정거장·시작 아이템 부트스트랩 구현**
  - 선행 조건: Task 1.7·4.5~4.8·7.3·8.7·9.2~9.3·9.14·9.21.
  - 구현 범위·상태 소유자: 필수 정의·프리팹·설정 준비 후 기존 시작 위치·footprint·파괴 불가 규칙으로 주 시설을 한 번 구성한다. 건물·공간·전력·정거장·아이템 소유자에 각 상태 반영을 맡기고 초기 지급과 드론 전환을 연결한다. 원본 구현의 준비 순서와 초기 망 규칙을 보존한다.
  - 후속 연결: Task 10C.2에서 실제 시작 장면과 사용자 흐름을 검증한다. 연구 초기 해금은 Task 10A.1의 설정 준비와 연결하되 연구 상태를 부트스트랩이 중복 소유하지 않는다.
  - 검증: 빈 월드 시작, 설정·프리팹 준비 지연 또는 실패, 부분 초기화 후 재시도, 반복 업데이트에서 시설·지급·망 등록의 중복과 고아 상태를 검사한다.
  - 완료 기준: 테스트용 직접 엔티티 구성 없이 런타임 초기화 경로로 주 시설·초기 전력·정거장·지급품이 일관되게 준비되고 반복 실행으로 복제되지 않는다.

---

## 🔬 [Phase 10] Research / Input·UI·Presentation / Integration·Performance

10A·10B·10C를 각각 독립된 완료 단위로 추적한다. 최소한의 상태 관찰 화면은 앞 단계에서 연결할 수 있다. 설정·Authoring·월드 생성·부트스트랩은 담당표의 Task에서 구현하고, 10B에서는 입력·표시를 연결한다. UI가 추가 상태 소유자가 되지 않도록 한다.

### 10A: Research

- [ ] **Task 10A.1: 연구 설정·선행 관계·해금·보상 데이터 정의**
  - 선행 조건: 기존 연구 설정과 현재 건물·레시피 식별자 확인.
  - 구현 범위·상태 소유자: 연구 설정을 검증·게시하고 전역 연구 상태, 건물 로컬 진행, 해금·보상의 원본을 구분한다.
  - 후속 연결: 선택·진행·완료는 Task 10A.2~10A.4에서 구현한다.
  - 검증: ID 중복, 선행 관계 순환, 누락된 건물·레시피, 잘못된 재료·보상을 검사한다.
  - 완료 기준: 유효한 설정만 공개되고 실패한 로딩이 부분 상태를 게시하지 않는다.

- [ ] **Task 10A.2: 연구 선택·변경 및 전역 상태 구현**
  - 선행 조건: Task 10A.1.
  - 구현 범위·상태 소유자: 전역 연구 소유자가 선택·변경 요청과 선행 조건을 검증하고 기존 진척도 보존·로컬 주기 초기화 규칙을 적용한다.
  - 후속 연결: 실제 주기 진행은 Task 10A.3, UI 입력은 Task 10B.4에서 연결한다.
  - 검증: 잠긴 연구, 완료 연구, 반복 선택, 진행 중 변경 및 잘못된 요청을 검사한다.
  - 완료 기준: 연구 원본이 중복되지 않고 확인된 선택·변경 규칙과 요청 수명주기를 만족한다.

- [ ] **Task 10A.3: 연구 재료 소비·로컬 진행·전역 진척도 반영**
  - 선행 조건: Task 10A.2, Item 입고·소비 경계, Task 8.7 및 관련 보완 검증.
  - 구현 범위·상태 소유자: 연구건물의 재료 수령·주기 진행과 전력 사용을 연결하고 주기 결과를 전역 연구 소유자에게 전달한다.
  - 후속 연결: 최종 완료·보상은 Task 10A.4에서 구현한다.
  - 검증: 재료 부족, 무전력, 복수 연구건물, 연구 변경 중 주기 완료와 재료 소비 시점을 검사한다.
  - 완료 기준: 재료·전력·주기 결과가 일치하고 전역 진척도가 중복 반영되지 않는다.

- [ ] **Task 10A.4: 연구 완료·해금·보상 및 생산·건설 연동**
  - 선행 조건: Task 10A.3 및 건설·제작의 권위 있는 요청 검증 경로.
  - 구현 범위·상태 소유자: 전역 연구 소유자가 완료·보상을 한 번 적용하고 건설·레시피·속도 소비자가 공개 데이터를 읽도록 연결한다.
  - 후속 연결: 해금 상태 표시와 입력 제어는 Task 10B.2~10B.4에서 연결한다.
  - 검증: 동시 완료, 완료 후 잔여 로컬 주기, 보상 중복, UI를 우회한 잠긴 건물·레시피 요청을 검사한다.
  - 완료 기준: 해금이 실제 요청 처리에서 강제되고 완료·보상·진행 정리가 일관된다.

### 10B: Input / UI / Presentation

각 Task 착수 시 기존 화면·사용자 흐름을 대조하고 독립적으로 검증할 수 있는 하위 작업으로 나눈다. 입력·렌더 검증은 관련 컴파일/EditMode 결과와 별도로 기록한다.

- [ ] **Task 10B.1: 카메라·월드 입력·UI 입력 차단 연결**
  - 선행 조건: 현재 장면·입력 계층과 V2 월드 좌표·상태 조회 계약.
  - 구현 범위·상태 소유자: 카메라와 포인터 좌표 변환, 월드 입력 전달, UI 위 입력 차단을 연결한다. 시뮬레이션 상태는 ECS가 소유한다.
  - 후속 연결: 건설·설정·연구 명령은 Task 10B.2~10B.4, 카메라 기반 청크 요청은 Task 10B.5에서 연결한다.
  - 검증: 좌표 변환과 요청 생성은 가능한 범위에서 자동 검증하고 실제 카메라·입력·UI 차단은 사용자 수동 검증으로 구분한다.
  - 완료 기준: 합의된 입력 흐름이 올바른 명령으로 전달되고 UI 조작이 월드 명령과 중복되지 않는다.

- [ ] **Task 10B.2: 건설·복사·취소·철거 입력 및 미리보기 연결**
  - 선행 조건: Task 10B.1, Phase 7, Task 9.17~9.20 및 연구 해금 계약.
  - 구현 범위·상태 소유자: ① 배치·회전·미리보기 ② 복사·묶음 배치 ③ 공사 취소 ④ 범위 철거·회수의 하위 흐름을 나눠 기존 요청 경로에 연결한다. 프리뷰는 점유 원본을 소유하지 않는다.
  - 후속 연결: 작업 진행 표시는 Task 10B.4에서 완료한다.
  - 검증: 각 하위 흐름의 요청 값·해금·중복 입력을 검사하고 실제 입력·미리보기는 사용자 수동 검증한다.
  - 완료 기준: 모든 하위 흐름의 완료 증거가 있고 표시와 실제 예약·공사·철거 결과가 일치한다.

- [ ] **Task 10B.3: 건물 정보·설정·아이템 작업 UI 연결**
  - 선행 조건: Storage·Crafter·Power·Drone의 조회·명령 계약, Task 10B.1 및 보완 검증 F2.
  - 구현 범위·상태 소유자: ① 정보·버퍼 표시 ② 레시피·설정 변경 ③ 아이템 반입·반출 명령을 구분하여 연결한다. UI는 원본 상태를 읽고 요청을 발행한다.
  - 후속 연결: 도메인 전체 플레이 흐름은 Task 10C.2에서 확인한다.
  - 검증: 선택 대상 제거, 패널 재활성화, 반복 명령, 콜백 등록·해제와 실제 표시·조작을 구분해 검사한다.
  - 완료 기준: 대상·표시·명령이 일치하고 UI에 별도 게임 상태나 중복 콜백이 남지 않는다.

- [ ] **Task 10B.4: 연구 UI 및 작업·드론 상태 표시 연결**
  - 선행 조건: Phase 9, Phase 10A 및 Task 10B.1.
  - 구현 범위·상태 소유자: ① 연구 선택·진척·해금 표시 ② 공사·철거·회수 작업 표시 ③ 드론 이동·충전·보관 표시를 독립 하위 흐름으로 연결한다. 표시 객체는 원본 상태의 관찰자다.
  - 후속 연결: 장면 전체의 실제 표시 결과는 Task 10C.2에서 확인한다.
  - 검증: 완료·취소·대상 소멸 시 표시 제거, 충전/보관 구분, 화면 재진입을 검사하고 시각 검증 결과를 별도 기록한다.
  - 완료 기준: 각 하위 흐름의 UI·표시가 원본 수명주기를 따르며 고아 표시·오래된 정보가 남지 않는다.

- [ ] **Task 10B.5: 카메라 주변 청크 로딩 연결**
  - 선행 조건: Task 4.6~4.8 및 Task 10B.1.
  - 구현 범위·상태 소유자: 카메라 주변의 기존 로드 범위를 계산해 청크 요청을 발행한다. 카메라는 관찰 범위와 입력을 담당하고 실제 로딩·생성 완료 상태는 Task 4.6의 소유자가 관리한다.
  - 후속 연결: Task 10B.6에서 생성된 청크의 바닥을 표시하고 Task 10C.2에서 실제 월드 확장을 확인한다.
  - 검증: 카메라 청크 경계 통과·되돌아오기·빠른 이동·음수 좌표, 초기 로딩과의 중복 요청을 검사한다. 실제 카메라 입력 결과는 수동 검증으로 구분한다.
  - 완료 기준: 카메라 이동에 따라 필요한 청크·자원이 준비되고 재방문 시 중복 생성이나 불필요한 요청 잔류가 없다.

- [ ] **Task 10B.6: 청크 바닥·바이옴 렌더링 연결**
  - 선행 조건: Task 4.6·4.9 및 Task 10B.1·10B.5.
  - 구현 범위·상태 소유자: 생성된 청크와 바이옴 결과로 기존 바닥 메쉬·머티리얼·정렬·가시 범위를 연결하고 렌더 자원의 생성·해제를 관리한다. 표현용 가시성 처리와 시뮬레이션 청크 제거를 구분한다.
  - 후속 연결: Task 10C.2 실제 시각 검증, Task 10C.3 렌더링 부하 측정.
  - 검증: 청크 생성 후 표시, 경계 이음, 카메라 이동·재진입, 설정·소재 누락과 렌더 자원 누수를 검사한다. 시각 결과는 사용자 수동 검증으로 별도 기록한다.
  - 완료 기준: 기존 바닥·바이옴 표현이 생성된 월드와 일치하고 중복 렌더 객체·누수 없이 표시 수명주기가 관리된다.

### 10C: Integration / Performance

- [ ] **Task 10C.1: 이식 대응 목록 및 기능별 완료 증거 대조**
  - 선행 조건: 각 도메인의 범위 대응, Phase 0·1·3·4에 추가된 런타임 연결 Task 및 Phase 6~10B 결과.
  - 구현 범위·상태 소유자: 기존 기능과 담당 Task, 현재 코드, 검증 증거를 대조한다. 누락은 해당 도메인의 하위 Task로 배정하며 이 항목에 구현을 몰아넣지 않는다.
  - 후속 연결: 누락 작업과 증거 공백을 정리한 뒤 Task 10C.2의 실제 흐름을 확정한다.
  - 검증: 부트스트랩·Authoring·설정·월드/자원·입력/UI 등 실제 유지 대상이 명시적인 대응을 갖는지 확인한다.
  - 완료 기준: 미구현·검증 대기·완료가 구분되고 담당 없는 유지 대상이 없다. 의도적인 범위 제외는 사용자 확인을 기록한다.

- [ ] **Task 10C.2: 실제 실행 환경의 전체 흐름 검증**
  - 선행 조건: Task 10C.1에서 확인한 구현 누락 해소, Task 9.22의 시작 초기화, Task 10B.5~10B.6의 월드 표시 연결 및 관련 자동 검증.
  - 구현 범위·상태 소유자: 시작 월드→건설→채굴·생산·물류→전력→드론→연구의 사용자 흐름과 필요한 장면·에셋 연결을 확인한다.
  - 후속 연결: 재현되는 성능 문제와 대표 부하는 Task 10C.3에서 측정한다.
  - 검증: 사용자 수동 플레이·입력·시각 결과와 자동 테스트 증거를 나누어 기록한다. 별도 실행 요청 없는 Play Mode 자동 검증은 수행하지 않는다.
  - 완료 기준: 실제 흐름의 검증 결과가 있으며 미수행 항목을 통과로 처리하지 않는다.

- [ ] **Task 10C.3: 대표 부하 시나리오 및 Profiler 기준 측정**
  - 선행 조건: Task 10C.2의 실행 가능한 흐름과 실제 측정 실행 범위 확인.
  - 구현 범위·상태 소유자: 아이템·건물·드론 규모와 환경을 명시한 시나리오에서 프레임 시간, Job 대기, ECB·공간 동기화, 할당·메모리를 측정한다.
  - 후속 연결: 확인된 병목만 Task 10C.4의 개별 최적화 대상으로 선정한다.
  - 검증: 측정 환경·규모·반복 조건을 기록하고 추정 비용과 실제 관찰 결과를 구분한다.
  - 완료 기준: 재현 가능한 기준 결과와 우선순위가 있으며 V2 구조만으로 성능 향상을 가정하지 않는다.

- [ ] **Task 10C.4: 측정된 병목별 최적화 및 전후 비교**
  - 선행 조건: Task 10C.3의 측정 근거.
  - 구현 범위·상태 소유자: 병목마다 별도 하위 Task로 범위를 정해 수정하고 기존 소유권·실행 순서 계약을 유지한다.
  - 후속 연결: 변경된 도메인의 검증·문서에 결과를 반영한다. 추가 최적화는 새로운 근거가 있을 때만 확장한다.
  - 검증: 동일 조건의 전후 측정과 관련 회귀 테스트를 수행한다.
  - 완료 기준: 각 변경의 효과·비용·잔여 한계가 확인된다. 최적화 대상이 없으면 측정 근거와 함께 변경 불필요로 종료할 수 있다.
