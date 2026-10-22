# PlanetMiner Architecture V2 - Implementation Tasks & Roadmap

> 현재 코드와의 관계 (2026-10-15): 이 문서는 설계 배경/규약 또는 개발 마일스톤 기록이다. 현재 실행은 Command→Preparation→Power→Building→Drone→Commit→Synchronization이며 EndCommand·EndPower·EndBuilding·EndSimulation의 네 ECB 경계를 사용한다. 본문의 전역 6페이즈·이전 심볼·과거 검증은 당시 설계/작업 범위로 읽고, 구현 판단은 [AGENTS.md](../../AGENTS.md)와 [현재 컴포넌트 계약](../CodeMemory/Components/README.md)을 우선한다. 개발 Phase 번호와 런타임 그룹을 구분한다.

> 본 문서는 `Architecture V2 Plan_0.2.md`를 바탕으로, 실제 구현을 위해 단계별 세부 작업(Task)과 진행 상태를 추적하는 체크리스트 문서입니다.
> 
> **핵심 원칙**: 
> - 글로벌 네임스페이스 및 `V2` 접두사 배제 규칙 준수
> - 현재 런타임 순서(`Command → Preparation → Power → Building → Drone → Commit → Synchronization`)와 각 도메인의 Decision/Reservation/Execution/StateApply 책임 준수
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
[Phase 4] Mining ───★ 코어·월드 로딩·광맥 생성·자원 스폰·바이옴 코드 구현 / 실제 바닥 렌더링은 10B.6
   ↓
[Phase 5] Crafter
   ↓
[Phase 6] Splitter / Merger
   ↓
[Phase 7] Construction Core (실제 드론 운반·철거 연결은 Phase 9)
   ↓
[Phase 8] Power Grid
   ↓
[Phase 9] Drone System ─── 공급/회수 관리·인계·재배정 부분 구현 / 실제 수행부·정거장·시작 초기화 후속
   ↓
[Phase 10] 10A Research → 10B Input / UI / Presentation → 10C Integration / Performance
```

---

## 후속 Task 운영 기준

- 이 문서의 Phase 0~10은 개발 마일스톤이다. 현재 Command/Preparation/Power/Building/Drone/Commit/Synchronization 런타임 그룹과 구분한다.
- 기존 완료 Task의 체크와 당시 검증 기록은 보존하되 현재 설명은 최신 소스에 맞춘다. Phase 0~7의 기존 코어·설정/월드 연결 및 Phase 8 완료와 Phase 9~10의 미구현/부분 구현은 별도로 추적한다. 기존 코어의 완료 기록이 새 후속 Task의 완료를 뜻하지 않는다.
- `[x]`는 완료 범위의 기록, `[ ]`는 해당 Task의 남은 범위가 있다는 뜻이다. 각 `현재 진행`에 적힌 대조 날짜와 코드 확인·부분 구현·자료/계약 기반·실행 미확인을 함께 읽는다. 초기 문서 대조에서는 새로 컴파일·테스트·Play Mode·Profiler를 실행하지 않았다. 후속 Task의 새 컴파일/선택 EditMode는 해당 완료·검증 절에 별도로 기록하며 기존 성공 건수와 현재 전체 프로젝트 통과 수로 합산하지 않는다.
- 각 Task는 **선행 조건 / 구현 범위·상태 소유자 / 후속 연결 / 검증 / 완료 기준**을 기록한다. 작업 번호보다 명시된 의존성을 우선한다.
- Task는 구현·검증 단위다. Task마다 새 시스템이나 상태 소유자를 만들지 않으며 기존 V2 컴포넌트·소유권 경계·공간 인덱스를 재사용한다. 아래의 소유자 표현은 책임을 나타내며 새 클래스명을 확정하는 것은 아니다.
- 각 Task에서 정상·실패 경로와 관련 불변식을 함께 검증한다. 마지막 통합 Task는 도메인 간 상호작용과 부하를 검증하며, 최초의 오류 처리 검증 단계로 사용하지 않는다.
- 그룹 순서와 ECB 재생 시점을 검증하는 테스트는 실제 `GameSimulationGroup` 실행을 사용한다. 테스트 편의를 위한 추가 Playback으로 실제 프레임 경계를 바꾸지 않는다.
- 승인된 예약 정책에서 함께 성공해야 하는 상태 변경은 하나의 원자적 작업으로 유지한다. 획득과 해제·완료·복구를 나누되 반쪽 예약을 성공 상태로 게시하지 않는다. 이 원칙을 이유로 현재 제공하지 않는 실물 선점이나 목적지 영속 예약을 새로 만들지 않는다.
- C#·에셋 변경 시 관련 컴파일과 범위에 맞는 검증을 수행한다. 컴파일/EditMode, 실제 플레이·입력·시각 결과, 성능 측정의 증거를 구분한다. 수동 작동 검증은 사용자가 수행하며, 별도 요청 없이 Play Mode 검증을 자동 실행하지 않는다.

### 현재 소스 대조 결과 (2026-10-12 대조, 2026-10-22 Task 9.2/9.3.1/9.3 코드/검증 반영)

- 최상위 Task 93개 중 기존 완료 기록 49개, Task 8.1~8.7과 Task 9.1~9.2/9.3.1/9.3을 완료하여 현재 체크는 60개이며 미완료는 33개다. Task 9.1은 상태 전이표/Lifecycle Owner 계약의 문서 완료이며 정거장/이동/충전의 실행 완료가 아니다. Phase 0~7의 코어와 필수 설정/프리팹/월드 준비는 유지한다. Power는 Preparation → Power(D/R/E/S/EndPower) → Building → Drone 구조와 배정량 확정 소비, 석탄 입고/전환/차감과 Miner/Crafter 공급 반영을 구현했다. 직전 Writer 경계 정리의 Unity 컴파일/관련 선택 EditMode 174/174은 그 코드 변경의 별도 기록이다. Task 9.1은 당시 문서만 변경했다. 2026-10-22 Task 9.2의 정거장 등록/단일 Range/정사각형/별도 드론 망과 공개 인덱스를 구현하고 Unity 컴파일/관련 선택 EditMode 99/99를 별도로 확인했다. Task 9.3.1의 드론 DB/생성 초기화는 관련 선택 56/56과 격리 Baker probe로 확인했다. Task 9.3은 같은 Entity 전환/정박/수납/전용 및 공유 용량/설정 초기화를 구현하고 최종 관련 선택 EditMode 340/340 및 실제 Baker 격리 전환을 확인했다. 연구/충전/실제 플레이/Player 빌드/성능은 후속이며 과거 실행과 합산하지 않는다.
- 미완료 Task 중 **12개는 부분 구현**이다: 9.4~9.9, 9.11~9.12, 9.15~9.16, 9.18, 9.20. 현장 공급/회수의 생성·후보·예약·배정·실물 인계·무효화·적재품 재배정과 제한된 통합 테스트 정의가 구현되어 있으므로 새 소유자를 만들지 않고 재사용한다. 전체 정거장/운송 유형·수행부까지 완료된 것은 아니므로 해당 체크는 닫지 않는다.
- **계약/진단 기반 2개**는 9.10(관측/경로/행동 계약, 배터리 최초 초기화만 있고 실제 이동/소비 없음)과 10B.6(바닥 샘플러/색상 미리보기, 제품 청크 렌더러 없음)이다. **문서 대조 일부 진행 1개**는 10C.1이다. 이 세 Task도 전체 범위 완료가 아니다.
- 나머지 **18개는 대상 기능 또는 요구된 실행/성능 검증이 남아 있다**. Task 8.1~8.7의 전력/석탄/Miner/Crafter 검증을 연구/충전이나 제품 전체 실행/성능의 증거로 확대하지 않는다. 각 Task의 구현/검증 범위를 구분한다.
- 드론 통합 fixture는 수행자·관측·경로 결과·행동 신호를 직접 제공한다. 현재 `DroneLifecycleIntegrationTests`의 6개 사례는 이번 Task 9.2 관련 선택 실행에서 6/6 통과했다. 실제 자동 이동·충전이나 새 실행 성공을 증명하지 않는다.
- 다음 연결의 공통 선행은 제품 수행자 생성/등록, 관측/경로/행동 Producer와 공통 적재량 초기화다. 용량 초기화는 [검토안](../Specifications/DroneCapacityInitializationPlan.md)의 미확정 정책을 먼저 확인한다. 정거장 기반은 9.2에서 구현했고 수행자·귀환·충전·연구의 제품 연결은 별도 후속이며 완료된 전력 코어와 관리 계층을 다시 만들지 않는다.

### 도메인 착수 시 이식 범위 대조

여기서 **미이식 기능 목록**은 기존 게임 기능 중 현재 V2 코드 또는 Task에 연결되지 않은 기능을 확인하는 목록이다. 새 기능을 추가하거나, 전체 프로젝트 조사를 모든 작업의 선행 조건으로 만드는 절차가 아니다.

- 각 도메인 착수 시 관련 기존 소스·설정·상세 문서만 대조한다. 레거시 문서는 탐색 자료로 사용하고 실제 동작은 원본 코드로 확인한다.
- 누락을 발견하면 `기존 기능 / 확인 근거 / 현재 V2 대응 / 담당 Task / 확인이 필요한 규칙`을 해당 Phase에 기록한다. 구현 필요성이 확인된 범위만 명시적인 하위 Task로 추가한다.
- 시작 시설 생성, 프리팹 Authoring, 설정 로딩, 월드·자원 생성에서 확인된 누락은 아래 담당표의 미완료 Task로 등록한다. 실제 구현·이식 여부는 해당 Task의 검증으로 확인한다.
- 새 게임 규칙이 필요하거나 기존 규칙이 불명확하면 해당 선택지를 확인한 뒤 구현한다. 범위 대조 자체를 이유로 기존 동작을 변경하지 않는다.
- Task 10C.1에서는 앞 단계에서 작성한 대응 목록과 완료 증거를 최종 대조한다.

2026-10-12 레거시 CodeMemory 정리에서 미이식 게임 동작만 아래 관련 Task의 검토 항목으로 보존했다. 현재 원본·소유권·프레임 계약은 AGENTS와 컴포넌트 문서를 우선하고, 연구의 확정 규칙은 연구 명세를 따른다. 레거시 검토 항목은 기존 동작의 확인 자료이며 현재 V2의 지원 기능·정책 확정·구현 승인·실행 성공을 뜻하지 않는다. 상충하는 규칙은 선택지를 확인한 뒤 구현하고 과거 심볼·캐시·예약·partial 시스템을 그대로 복제하지 않는다.

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

다음 항목은 이전 정적 점검 및 개선 피드백에서 발견된 검증 공백과 아키텍처 불변식 준수 여부를 추적·반영한 기록이다. 완료된 원본 피드백 문서는 정리했으며, 남은 성능 검토는 Task 10C.3~10C.4에서 추적한다. 아래 완료 표시와 실행 수치는 당시 범위이며 현재 구조의 새 실행 결과가 아니다.

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
  - 선행 조건: 현재 RecipeRegistry의 RecipeOutputElement 버퍼와 다중 출력 모델 확인.
  - 구현 범위·상태 소유자: Crafter가 단일 부산물이 아닌 다중 부산물 전체를 순회 처리하도록 계약을 확정하고, 하나의 부산물 슬롯이라도 만석이면 배출을 대기하는 All-or-Nothing 정책 적용.
  - 후속 연결: Phase 6 Splitter/Merger 복합 부산물 물류 처리.
  - 검증: `Phase5CrafterExecutionTests` (`Test07`, `Test08`)를 통해 다중 부산물 슬롯별 배출 및 All-or-Nothing 백프레셔 검증 통과.
  - 완료 기준: 레시피 정의와 런타임 부산물 배출 동작이 완전히 일치한다.

---

## 🧱 [Phase 0] Architecture Skeleton 구축 (기반 뼈대)

- [x] **Task 0.1: 레거시 코드 완전 백업 및 작업 공간 격리**
  - 현재 진행(2026-10-12): **당시 작업 기록 유지**. 현재 프로젝트 루트와 부모의 `Backup Scripts/`는 없으며 다른 백업 위치·Git 복구 경로는 이번 소스 대조에서 확인하지 않았다. 아래 체크를 현재 백업 폴더 존재 보장으로 읽지 않는다.
  - [x] `Assets/Scripts`, `Assets/Editor`, `Assets/UI`를 `Backup Scripts/` 폴더로 이동하여 컴파일 에러 없는 클린 상태 확보
  - [x] 신규 `Assets/Scripts/Phases/` 폴더 구성
- [x] **Task 0.2: 실행 Group 정의 및 건물·드론 도메인 분리**
  - 현재 진행(2026-10-12): **코드 구현 확인**. 초기 전역 6페이즈에서 현재 도메인 그룹으로 바뀌었으며 이번에는 속성·부모/순서 선언을 확인했다. 새 실행 검증은 하지 않았다.
  - [x] `CommandGroup`과 마지막 `EndCommandEntityCommandBufferSystem`
  - [x] `BuildingSimulationGroup` 내부 Decision/Reservation/Execution/StateApply와 마지막 EndBuilding
  - [x] `DroneSimulationGroup` 내부 Decision/Reservation/Execution/StateApply
  - [x] `SimulationCommitGroup`과 마지막 EndSimulation
  - [x] 최상위 마지막 `SynchronizationGroup`, 내부 마지막 WorldInvariantValidation
- [x] **Task 0.3: 최상위 시뮬레이션 그룹 구축 및 정렬**
  - [x] `GameSimulationGroup`을 Unity의 `SimulationSystemGroup` 내에 등록
  - [x] 현재 그룹의 부모·`UpdateAfter`·`OrderLast` 선언은 Command→Building→Drone→Commit→Synchronization이다. GameSimulationGroup은 DB/필수 설정 준비와 Fatal을 틱 시작에 검사한다. 그룹 순서와 Job/Fence 완료는 별개다.
- [x] **Task 0.4: Invariant Validation (데이터 무결성 검증) 프레임워크 뼈대 구축**
  - [x] `WorldInvariantValidationSystem` 생성 (Editor/Development 빌드 전용)
  - [x] 데이터 불일치/고아 엔티티 탐지 시 `Logs/InvariantErrors/` 파일 기록 및 `Debug.Break()` 에디터 일시정지 구조 마련 (콘솔 에러 제외)
- [x] **Task 0.5: Transient Command/Event 스탠다드 및 템플릿 정의**
  - [x] `Consume-on-Apply` 가이드라인에 맞춘 1회성 Request 컴포넌트 작성 규칙 수립 (`Architecture V2 Command Event Standard.md`)
  - [x] 하이브리드 Request 모델(`IRequestComponent`, `IEnableableRequest`) 마커 인터페이스 구축 및 ECB 사용 가이드 정리

### 후속 런타임 연결

- [x] **Task 0.6: Authoring·프리팹 데이터베이스 공통 계약 정의**
  - 현재 진행(2026-10-12): **규약과 건물·아이템·자원 연결 구현**. 필수 DB 준비/시작 관문과 정적 Baker·런타임 Spawn 책임이 있다. 이 규약 Task와 별도로 Task 9.3.1에서 드론 Authoring/DB/생성 초기화를 연결했다. 실제 출고/수납·배차·이동은 후속 범위다.
  - 선행 조건: 기존 6대 실행 Group과 Item·건물·자원 데이터 계약, 백업 Authoring 코드 확인.
  - 구현 범위·상태 소유자: 장면·SubScene Authoring이 게시할 정의·프리팹 참조·초기 설정, 베이킹된 데이터의 준비 조건, 실제 생성 소유자의 초기화 책임을 정의한다. 개별 도메인의 런타임 상태는 Baker가 중복 소유하지 않는다.
  - 후속 연결: Task 1.7·4.8·7.3.1·9.3.1에서 대상별 Baker·프리팹 연결을 구현한다. 이 Task를 이유로 범용 프레임워크를 새로 만들지 않는다.
  - 검증: 타입 식별자·필수 컴포넌트·TransformUsage·중복 컴포넌트 추가와 프리팹 누락 처리의 계약을 대조한다.
  - 완료 기준: 각 도메인이 사용할 베이킹 입력·출력과 스폰 시점의 책임이 명확하며 기존 소유권 경계를 보존한다. (155/155 EditMode 테스트 100% 통과)

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
  - [x] 요청의 Producer/Consumer·소비/공개 단계와 수명을 기록한다. 현재 아이템 선언 경로는 `Components/Items/`다.
- [x] **Task 1.3: Item Ownership State Owner 시스템 구현**
  - [x] `ItemOwnershipApplySystem` (BuildingStateApply): 일반 Transfer 소비 및 Owner 갱신. 드론은 같은 소유자의 TryTransferItem을 재사용하며 Lifecycle이 실제 성공량을 정산한다.
  - [x] `ItemLifecycleApplySystem` (BuildingStateApply): Spawn/Destroy·ProductResult 소비와 EndBuilding 실체화. 프리팹 생성만 사용하고 무DB 아키타입 fallback은 제공하지 않는다.
- [x] **Task 1.4: 도메인별 Spatial Index 기초 구조 연동**
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

- [x] **Task 1.7: 아이템 Authoring·프리팹 DB 및 렌더 스폰 연결**
  - 선행 조건: Task 0.6 및 기존 Item lifecycle·ownership·spatial 계약.
  - 구현 범위·상태 소유자: ItemAuthoring·아이템 프리팹 DB와 공통 SpawnPrefabItem을 기존 Item Lifecycle에 연결한다. 무DB fallback은 없으며 격리 테스트도 명시적인 DB를 준비한다. 월드·수납 표시 상태는 소유권에서 파생한다.
  - 후속 연결: Task 4.8 자원 스폰, Task 7.3 건물 출력, Task 9.22 초기 지급 및 Task 10C.2 실제 표시 검증.
  - 검증: 월드·수납 스폰, 수납·방출 후 표시 데이터, 프리팹 누락, 중복 컴포넌트와 ECB 재생 후 공간 등록을 검사한다. 시각 결과는 수동 검증으로 구분한다. (161/161 EditMode 테스트 100% 통과)
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

- [x] **Task 3.5: 기본 건물 런타임 설정 로딩 및 게시**
  - 현재 진행(2026-10-12): **코드 구현 확인**. 현재 BuildingConfig는 종류/티어·displayName·명시적 양수 Footprint·속도/용량과 비용을 게시한다. 설정/필수 DB 실패는 미게시·Fatal로 시작을 차단한다. 아래 초기 도입 기록의 기본값/fallback을 현재 계약으로 재사용하지 않는다.
  - 선행 조건: 기존 Belt·Storage·Miner·Crafter 컴포넌트와 백업 `BuildingRuntimeConfig` 스키마 확인.
  - 구현 범위·상태 소유자: 건물 기본 속도·저장 칸수 등 기존 설정을 검증해 타입별 정의 데이터로 게시한다. 설정 로더는 정의만 소유하고 인스턴스 진행도·소유 버퍼는 변경하지 않는다. 기존 레시피·아이템 스택 레지스트리는 재사용한다.
  - 후속 연결: Task 7.3.1의 건물 정의 연결과 Task 7.3의 인스턴스 초기화에서 소비한다. 공사 비용·전력·드론·연구 설정은 해당 도메인이 담당한다.
  - 검증: 누락·중복 타입, 잘못된 속도·용량, 로딩 실패 시 부분 데이터 게시, 기존 레시피·스택 설정과의 역할 중복을 검사한다. (167/167 EditMode 테스트 100% 통과)
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

- [x] **Task 4.5: 초기 영역·월드 시드·자원 생성 설정 연결**
  - 선행 조건: Task 0.6 및 기존 Resource 데이터 계약, 백업 초기 영역·자원 설정 Authoring 확인.
  - 구현 범위·상태 소유자: 초기 로드 영역, 월드 시드, 자원별 광맥·매장량 설정을 검증해 ECS 정의 데이터로 게시한다. 기존 베이킹·로드 시점과 설정 의미를 확인하고 생성 진행 상태와 분리한다. 초경량 무한 맵 아키텍처에 따라 불필요한 청크 영역 설정 엔티티를 배제하고, `WorldGenerationConfig.json`에서 `worldSeed`와 광석 4종 설정(`configs`)을 파싱하여 `ResourceGenerationSettings` 싱글톤 및 DynamicBuffer로 1회 부트스트랩 게시(`WorldGenerationConfigLoadSystem`). 완전 중단(All-or-Nothing Fail-Fast) 검증을 적용.
  - 후속 연결: Task 4.6에서 초기 청크를 요청하고 Task 4.7에서 자원 배치를 계산한다.
  - 검증: 같은 시드·설정, 잘못된 영역·품목·범위 값, 설정 누락·재초기화와 중복 게시를 검사한다. `Phase4WorldGenerationConfigTests` 6종 단위/통합 테스트 전수 통과 (173/173 EditMode 테스트 100% Pass).
  - 완료 기준: 초기 로딩과 자원 생성이 동일한 검증된 설정을 읽으며 설정 부재를 정상 초기화로 처리하지 않는다.

- [x] **Task 4.6: 청크 요청 수명주기 및 초기 영역 로딩 구현**
  - 현재 진행(2026-10-12): **코드 구현 확인**. 초기 N×N Queue, Pending 중복 방지, EndCommand 생성 뒤의 Completed 알림과 다음 Command의 완료 Map 갱신이 있다. 카메라 주변 Producer는 Task 10B.5에 남는다.
  - 선행 조건: Task 4.5 및 기존 격자·청크 좌표 규칙 확인.
  - 구현 범위·상태 소유자: 청크 로드 요청, 준비·생성 상태의 소유자를 정의하고 초기 영역 요청·중복 병합·요청 소비를 구현한다. 기존 개별 공간 인덱스의 상태를 다시 소유하지 않는다. 초경량 무한 맵 아키텍처에 맞춰 `ChunkUtility`(음수 좌표 FloorDiv 변환), `GeneratedChunkTracker`의 완료/대기 집합 및 `ChunkLoadRequestElement` 요청 큐 버퍼를 구축. `InitialChunkLoadBootstrapSystem`이 설정된 N×N(기본 3×3) 초기 청크 요청을 인큐하고, `ChunkLoadCommandSystem`(`CommandGroup`)이 중복을 O(1)로 제거(Idempotent Drop)하여 신규 청크를 `GeneratedChunkReadyElement`로 인계한다. 미처리 요청은 보존하고, EndStateApply의 완료 알림을 다음 Command에서 소비한 뒤에만 완료 집합에 등록한다.
  - 후속 연결: Task 4.7~4.8의 자원 생성·스폰 결과와 생성 완료를 연결하고, Task 10B.5에서 카메라 요청을 추가한다.
  - 검증: 중복 요청, 겹치는 초기 영역, 음수 좌표, 생성 실패·재시도와 이미 생성된 청크 재요청을 검사한다. `Phase4ChunkLifecycleTests` 6종 단위/통합 테스트 전수 통과 (179/179 EditMode 테스트 100% Pass).
  - 완료 기준: 요청과 청크 진행 상태가 일치하고 완료된 청크의 자원이 중복 생성되지 않는 계약이 마련된다.

- [x] **Task 4.7: 시드 기반 광맥 배치 및 자원 생성 요청 구현**
  - 현재 진행(2026-10-12): **코드 구현 확인**. ResourceGenerationCommandSystem이 준비 청크·설정/DB를 읽고 자원 스폰과 완료 알림을 같은 EndCommand ECB에 기록하며 공간 등록은 Synchronization이다. 초기 문서의 EndStateApply 표기는 현재 실행 경계가 아니다.
  - 선행 조건: Task 4.5~4.6 및 기존 광맥 생성 알고리즘 확인.
  - 구현 범위·상태 소유자: `ChunkUtility.IsInsideChunk` 헬퍼 메서드 및 `ResourceGenerationUtility`(시드 기반 결정론적 패치 계산, 청크 경계 처리, 품목 선착순 셀 선점) 구현. `ResourceGenerationCommandSystem`(`CommandGroup`, `[UpdateAfter(typeof(ChunkLoadCommandSystem))]`)을 통해 `GeneratedChunkReadyElement` 버퍼를 읽고 중간 셀별 요청 엔티티 없이 ResourceNode 스폰 및 청크 완료 알림을 EndStateApply ECB에 기록한다. 완료 상태는 실제 반영 이후 확정하며 즉시 Playback 우회는 사용하지 않는다.
  - 후속 연결: Task 4.8에서 Authoring/프리팹 베이킹 DB를 연결하여 실제 렌더 프리팹 인스턴스화로 확장.
  - 검증: 동일 시드 100% 재현(Determinism), 청크 로드 순서 무관성(Cross-Chunk Load-Order Invariance), 타일 중복 배제(No Overlap), 설정 부재 안전성(Graceful Early Return), 파이프라인 연계 및 멱등성 검증. `Phase4ResourceGenerationTests` 7종 단위/통합 테스트 전수 통과 (186/186 EditMode 테스트 100% Pass).
  - 완료 기준: 기존 생성 규칙에 맞는 재현 가능한 결정을 수행하고 EndCommand에서 자원 엔티티와 완료 알림을 반영하며 Synchronization에서 공간 인덱스에 등록한다.

- [x] **Task 4.8: 자원 Authoring·프리팹 DB 및 스폰·공간 등록 연결**
  - 현재 진행(2026-10-12): **코드 구현 확인**. 공통 시작 관문이 활성 자원의 필수 DB를 검증하고 게임 시작을 허용한다. 설정/시작 DB 검증 실패는 Fatal이며 자동 복구하지 않는다. 생성기의 준비 전 Ready 요청 보존과 시작 실패 정책은 별개다. 실제 화면·카메라 확장은 10B.5~10B.6에 남는다.
  - 선행 조건: Task 0.6·4.6~4.7 및 기존 ResourceNode·ResourceSpatialIndex·Miner 계약.
  - 구현 범위·상태 소유자: `ResourcePrefabDatabaseAuthoring` 구현으로 인스펙터 수동 목록 및 `Resources/Prefabs/Resource` 폴더 자동 수집(`ResourcesAutoLoad` 및 ContextMenu) 지원, `ResourcePrefabDatabase` 싱글톤 및 `ResourcePrefabElement` 버퍼에 `TransformUsageFlags.Renderable` 프리팹 베이킹. `ResourceGenerationUtility` 및 `ResourceGenerationCommandSystem`에서 프리팹 DB를 조회하여 `ecb.Instantiate(prefab)`를 통해 렌더 컴포넌트 복제 및 `[GridPosition, ResourceNode, LocalTransform]` 초기화 연결. 방안 B 엄격 차단(Strict Fail)을 적용하여 프리팹 DB 부재 또는 필요한 프리팹 누락 시 전체 청크를 대기시킨다. 일부 광석만 생성하지 않으며 준비 후 같은 요청으로 재시도한다. 설정별 프리팹 조회는 업데이트당 한 번 수행한다. `SynchronizationGroup`의 `ResourceSpatialSyncSystem`을 통해 `ResourceSpatialIndex` 자동 등록 및 기존 채굴기(`Miner`) 파이프라인과 직결.
  - 후속 연결: Task 10B.5·10C.2에서 실제 월드 확장과 시각 결과를 확인.
  - 검증: 프리팹 DB 조회, 프리팹 인스턴스화 및 Transform 초기화, 프리팹 누락 시 엄격 차단, DB 부재/빈 버퍼 시 안전 차단, 채굴기 연계 및 매장량 고갈 시 엔티티 파괴 완주 검증. `Phase4ResourceAuthoringAndSpawnTests` 6종 단위/통합 테스트 전수 통과 (192/192 EditMode 테스트 100% Pass).
  - 완료 기준: 설정과 청크 요청으로 생성한 자원이 기존 채굴 파이프라인에 참여하고 실패 시 고아 요청·중복 자원·잘못된 청크 완료 상태가 없다.

- [x] **Task 4.9: 바닥 설정 로딩 및 바이옴 생성 계약 구현**
  - 현재 진행(2026-10-12): **설정·순수 선택 코드 구현 확인**. 단일 worldSeed·바이옴/변형 버퍼와 FloorBiomeSampler가 있고 색상 미리보기만 소비한다. 제품 Sprite 청크 렌더링은 이 Task의 완료 범위가 아니다.
  - 선행 조건: Task 4.5~4.6의 월드·청크 계약, 백업 FloorGenerationConfig·FloorBiomeGeneration 확인.
  - 구현 범위·상태 소유자: 바닥 설정과 기존 바이옴·변형 선택 규칙을 연결하고 렌더러가 조회할 생성 데이터·결과 계약을 마련한다. 바닥 표현이 자원·건물 점유의 원본을 소유하지 않도록 한다.
  - 후속 연결: Task 10B.6에서 청크 메쉬·머티리얼·표시 수명주기를 구현한다.
  - 검증: 동일 설정·좌표의 재현성, 청크 경계, 자원 셀과의 기존 바닥 규칙, 잘못된 설정·시각 변형 참조를 검사한다.
  - 완료 기준: 확인된 바닥 생성 규칙에 따른 결과를 렌더 계층이 사용할 수 있고 월드 시뮬레이션 상태와 책임이 분리된다.
  - 구현: 레거시 바닥 JSON을 `WorldGenerationConfig.json`의 `floor`로 통합하고 단일 `worldSeed`를 사용한다. 설정 전체를 검증한 뒤 월드 설정 엔티티에 바닥 파라미터와 바이옴·변형 버퍼를 게시한다. `FloorBiomeSampler`는 월드 셀별 바이옴·전이·변형 인덱스와 Sprite 경로 조회 계약을 제공하며 점유 상태를 참조하지 않는다. 렌더링은 Task 10B.6 범위다.

---

## ⚙️ [Phase 5] Crafter (제작기 및 레시피)

- [x] **Task 5.1: RecipeRegistry 및 Unmanaged 버퍼 기반 레시피 정의**
  - 현재 진행(2026-10-12): **코드 구현 확인**. RecipeConfigElement·RecipeIngredientElement·RecipeOutputElement 버퍼와 범위 오프셋을 사용한다. RecipeBlob/BlobAsset은 현재 소스에 없으며 Init이 시작 시 한 번 게시한다.
- [x] **Task 5.2: Crafter 재료 수집 및 제작 진행 시스템 구현**
  - [x] 입력 버퍼 재료 확인 → 소모 처리 → 제작 진행도 누적 → 결과물 출력 (선소비 모델 및 정책 B 만석 대기 완주)
  - [x] `CrafterStateDecision` 컴포넌트 분리 도입: `CrafterDecisionSystem`의 영속 상태 직접 수정 제거 및 `CrafterStateApplySystem`에서 상태 전이 일괄 반영
  - [x] 다중 부산물(Byproducts) 분배 및 All-or-Nothing 출력 공간 검증 (`Phase5CrafterExecutionTests` Test07, Test08 Pass)
  - [x] `ProductResult` 전용 버퍼를 통한 지연 스폰 및 수명주기 정리 (`ItemLifecycleApplySystem` 연결)
- [x] **Task 5.3: 레시피 변경·해제와 미소비 잔여 재료 배출 구현**
  - 현재 진행(2026-10-12): **코드 구현 확인**. 진행 상태를 초기화하고 미소비 Stored 실물을 기존 Product 슬롯 뒤에 배출한다. 품목별 입력 슬롯도 다시 계산하며 기존 입력 슬롯 구분을 보존한다. 이미 선소비한 재료의 환급이나 틱 rollback은 제공하지 않는다.
  - [x] `CrafterRecipeCommandSystem` 구현: `CommandGroup`에서 레시피 변경 및 `StorageFilter` 원자적 동기화 완료
  - [x] 레시피 변경 시 진행 중 제작 상태 초기화(선소비 재료 미환급)와 미소비 잔여 재료의 `ProductItemElement` 배출 이관
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
  - 경합 규칙: (1) 일반 벨트 이동은 기존 `BeltMovement` 파이프라인이 담당하며 이 예약의 후보에 포함하지 않는다. (2) 출고·Routing 선검사와 최종 예약은 `BeltEntryUtility.HasEntrySpace`로 월드 소유권과 활성 `BeltMovementState`를 모두 가진 목적지 점유만 계산한다. 간격은 `GameConstants.ItemSpacing`과 `AlignmentEpsilon`, 수용량은 `MaxItemsPerBeltTile = (int)(1f / ItemSpacing)`의 파생 관계를 사용하고 별도 숫자로 고정하지 않는다. (3) 공간이 있을 때만 동일 목적지의 외부 후보 중 한 틱 최대 한 개를 기존 `PlacementStamp` 비교로 승인한다. Stamp 중복·동률 계약의 남은 문제는 F-041로 구분한다.
  - 승인·거부 계약: 거절된 후보의 결정을 비활성화하고 건물 출고에는 `CanOutput = false`도 기록한다. 거절 시 원본 아이템·소유 버퍼·라우터 커서를 보존하며, 승인된 결정의 실제 위치·소유권·커서 반영과 소비는 기존 StateApply가 담당한다. 승인 이후 원본 상태가 달라지는 무효 승인 방어는 F-033의 별도 범위다.
  - 지원 범위 (2026-09-30 사용자 확정): 일반 벨트끼리의 T형 직접 합류와 출력 셀로의 추가 측면 직접 진입은 자동 경합 중재·사전 대기·교착 해소 보장 대상에서 제외한다. 합류/분배 중재는 Merger/Splitter의 역할이다. 현재 스냅샷 공간 검사와 외부 후보 중재의 보장을 모든 벨트 이동의 간격 보장으로 확대하지 않는다. 공통 정책은 [AGENTS.md](../../AGENTS.md)의 물류·생산 계약을 따른다.
  - 후속 연결: Splitter·Merger의 구체적인 후보 선택 및 cursor 갱신은 Task 6.3~6.4에서 연결.
  - 당시 검증 기록: `Phase6BeltDestinationReservationTests` 6개 단위 테스트 전수 통과 (빈 벨트 단일 출고 승인, 다중 건물 출고 PlacementStamp 경합, 건물 출고 vs 라우팅 전달 경합, 공간 부족 시 거부, 공간 충분 시 정상 승인, 무요청 시 조기 반환). 전체 EditMode 130/130 Pass. 이는 당시 기록이며 현재 테스트 구성·최신 통과 건수를 뜻하지 않는다.
  - 후속 검증 (2026-09-30): F-029 지원 범위 재정의·F-034 공통 판정 구현에서 Unity 컴파일 및 관련 EditMode 61/61 통과. 예약 46개, Splitter 4개, Merger 4개, Storage Decision 4개, 기존 EndToEnd 3개다. [검증 로그](../../Logs/QualityImprovement/F029-F034/verification-recovered.json)를 따른다. 이번 문서 정리에서는 새 컴파일·테스트를 실행하지 않았다.
  - 완료 기준: 지원하는 외부 진입의 선검사와 최종 공간 판정이 일치하고, 목적지당 한 개 승인·예약 거절 시 원본 보존·승인 후 진행도/소유권/결정 소비가 일관된다. 일반 T형 직접 합류는 지원 범위에서 제외하며 F-033의 무효 승인 방어는 별도 미완료 항목이다.
  - F-030 후속 연결 (2026-09-30): 미조회 전방 간격 문제는 이 예약의 범위를 확장하지 않고 별도 벨트 속도 상한 정책으로 완료했다. 현재 정책은 [AGENTS.md](../../AGENTS.md)의 물류·생산 계약을 따른다. F-029의 일반 T형 직접 합류 제외 정책은 유지한다.

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

- [x] **Task 7.1: 공간 점유·예약의 소유권 계약 정의**
  - 선행 조건: 현재 `BuildingFootprint`, `BuildingSpatialIndex`, `BuildingSpatialSyncSystem`과 Phase 6 연결 계약 확인.
  - 구현 범위·상태 소유자: 기존 크기·위치 데이터를 재사용하고 권위 있는 점유·예약 상태와 파생 조회 인덱스의 역할, 등록·해제 시점을 정의한다. `BuildingTypeEnum.ConstructionSite`를 추가하여 공사 현장을 `BuildingSpatialIndex`에 완공 건물과 동일하게 통합 등록($O(1)$ 동시 조회). `ConstructionComponents.cs`(`PlacementFlags`: 기본 `StrictAllOrNothing`, 선택 `AllowPartialPlacement`, `ConstructionSite`, `ConstructionSiteFlags`, `PlacementValidationResult`) 정의. `BuildingPlacementValidationUtility`를 통해 UI 프리뷰와 시뮬레이션 시스템이 공유하는 순수 함수 검증 유틸리티 구현 (다중 셀, 음수 좌표, 회전 Bounding Box Min Cell Anchor, 벨트 덮어쓰기 허용, 채굴기 자원 노드 필수 검사, 바닥 월드 아이템 회수 대기 연계, 다중 배치 선점 충돌 중재).
  - 후속 연결: 실제 예약과 현장 생성은 Task 7.2에서 구현한다. 레거시 `ChunkMapSystem`을 전제로 새로 만들지 않는다.
  - 검증: 다중 셀·회전·음수 좌표·동일 프레임 충돌의 예약 및 점유 상태 전이를 대조한다. `Phase7ConstructionContractTests` 8종 단위/통합 테스트 전수 통과 (218/218 EditMode 테스트 100% Pass).
  - 완료 기준: 원본 상태, 수정 권한, 인덱스 반영 시점이 하나의 계약으로 정리된다.

- [x] **Task 7.2: 원자적 배치 예약 및 공사 현장 생성**
  - 2026-10-11 티어·표시 이름 현행 계약: 배치 후보는 TargetType/TargetTier와 해당 Config 크기 스냅샷을 전달한다. Command가 같은 조합의 해금·크기를 검증하고 요구 비용을 현장에 복사한다. ConstructionSite가 목표 종류·티어를 보존하며 EndCommand에 실체화한다. 같은 종류·티어 벨트 방향 변경만 유지하고 다른 티어 위의 배치는 거절한다. 기존 Strict/Partial 및 접수·설치 순서 규칙은 유지한다. [명세](../Specifications/BuildingTierAndDisplayName.md).
  - 선행 조건: Task 7.1, 관련 건물 정의·공사 비용 데이터.
  - 구현 범위·상태 소유자: 여러 후보 footprint를 전부 예약한 뒤 건설 요청을 현장으로 전환한다. 권위 있는 배치 요청에서 `PlacementStamp(Tick, Order)`를 확정하고 현장에 그대로 전달한다. 공간 예약과 현장 상태는 각 소유자가 처리한다.
  - 후속 연결: 플레이어 입력·미리보기·복사는 Task 10B.2에서 연결한다.
  - 검증: 일부 셀 충돌, 잘못된 정의, 중복 요청, 생성 실패 시 모든 임시 예약의 롤백을 검사한다. `Phase7PlacementCommandTests` 8종 단위/통합 테스트 전수 통과 (226/226 EditMode 테스트 100% Pass).
  - 완료 기준: 부분 현장·고아 예약 없이 요청이 성공하거나 전체 취소된다.

- [x] **Task 7.3.1: 건물 Authoring·프리팹 DB 및 정의 연결**
  - 2026-10-11 티어·표시 이름 현행 계약: 티어·표시 이름 지원 구조와 건물 자동 탐색 제거는 구현 완료다. BuildingConfig.json은 종류/명시적 양수 tier/displayName/footprint와 각 티어의 독립 스펙을 소유하고, 프리팹 목록은 Type/Tier/Prefab을 명시한다. ResourcesAutoLoad/LoadMode/PopulateFromResources와 전용 자동 채우기 Inspector는 제거했다. Baker는 Renderable 프리팹 참조와 DB 매핑을 게시하며 런타임 BuildingType/Tier/Footprint는 생성 소유자가 붙인다. 기존 11종만 1티어/현재 종류명/기존 수치로 전환했고 실제 상위 티어 데이터·UI·승급·연구 Writer는 후속이다. 초기화/생성의 무DB fallback은 현재 제공 기능이 아니다. [명세](../Specifications/BuildingTierAndDisplayName.md).
  - 선행 조건: Task 0.6·3.5 및 기존 건물 타입·footprint 계약.
  - 초기 구현 기록(현재 계약은 위 후속 참조): 11종 전체 건물(Belt, Miner, Crafter, Storage, PowerPole, Splitter, Merger, CoalGenerator, DroneStation, ResearchBuilding, MainFacility)의 Footprint 규격을 `BuildingConfig.json`에 통합 정의(Belt/Splitter/Merger/PowerPole/Storage: 1x1, Miner/Crafter: 2x2, CoalGenerator/DroneStation/ResearchBuilding/MainFacility: 3x3)하고 `BuildingConfigLoader` 유효성 검증(Footprint 크기 >= 1, MainFacility 용량 50 등) 구현. `BuildingPrefabDatabaseAuthoring` 및 커스텀 인스펙터 에디터(`BuildingPrefabDatabaseAuthoringEditor`)를 구현하여 `PopulateFromResources()`를 통한 원클릭 11종 프리팹 자동 수집 지원. Baker에서 프리팹 원본에 `BuildingType`, `BuildingFootprint`를 동적으로 주입(방안 A)하고 `TransformUsageFlags.Renderable` 적용. `PrefabLookupUtility.TryGetBuildingPrefab` 순수 조회 유틸리티 구현 및 누락 시 Strict Fail(스폰 거부 및 Error 로깅) 정책 확립. 아이템 수명주기(`ItemLifecycleApplySystem`)에도 동일하게 프리팹 DB 활성화 환경에서의 Strict Fail 통일 및 무DB 순수 시뮬레이션 테스트 격리 Fallback 구현.
  - 후속 연결: Task 7.3(공통 건물 생성 경로 구현)에서 `BuildingPrefabDatabase`와 `BuildingConfig`를 연계하여 실제 완공 건물 엔티티 인스턴스화 및 컴포넌트 초기화에 사용.
  - 당시 검증 기록(새 실행 수가 아님): `Phase7BuildingAuthoringPrefabTests` 7개 단위/통합 테스트 전수 통과 (11종 Footprint 로딩 및 유효성, `TryGetFootprint`, Baker 동적 컴포넌트 주입, `TryGetBuildingPrefab` 등록 조회, Strict Fail 거부, Resources 11개 자동 파퓰레이트, 잘못된 Footprint 실패). `Phase1ItemAuthoringPrefabTests` 6/6 Pass, 전체 프로젝트 EditMode 회귀 테스트 234/234 Pass (100%).
  - 완료 기준: 11종 건물 정의와 프리팹 DB가 유효하게 게시되고 건물 생성 소유자가 일관되게 조회할 수 있다.

> 2026-09-30 F-001 현행 소스 대조: 아래 책임 위치를 통합 ConstructionLifecycle Job과 F-005/F-024 계약으로 갱신했다. 기존 검증 수치와 테스트명은 당시 기록이며 이번 새 실행 결과가 아니다.

- [x] **Task 7.3: 공통 건물 생성 경로 구현**
  - 2026-10-11 티어·표시 이름 현행 계약: BuildingStateApply의 BuildingLifecycleApplySystem과 공통 SpawnBuilding은 TargetType/TargetTier로 Config·프리팹을 조회하고 BuildingType.Type/Tier 및 원본 기본 크기·설치 표식을 EndBuilding에 기록한다. 직접 Spawn의 크기 생략만 Config를 사용하고 다른 티어를 대체 선택하지 않는다. 현장 완공도 같은 공통 Spawn을 호출한다. 조회 실패는 기존 Null/중단 진단이며 전체 Playback rollback을 보장하지 않는다. [명세](../Specifications/BuildingTierAndDisplayName.md).
  - 선행 조건: Task 7.1·7.3.1 및 현재 이식된 건물 타입의 데이터 계약.
  - 초기 구현 기록(현재 계약은 위 후속 참조): `SpawnBuildingRequest` 요청 컴포넌트(`IRequestComponent`) 및 Phase 5 `StateApplyGroup`의 `BuildingLifecycleApplySystem` 구현. 베이킹된 프리팹(`PrefabLookupUtility.TryGetBuildingPrefab`)과 `BuildingConfig` 설정 데이터를 연계하여 완공 건물 엔티티 인스턴스화, 공통 컴포넌트(`BuildingType`, `BuildingFootprint`, `GridPosition`, `Direction`, `PlacementStamp`, `LocalTransform`) 및 핵심 6종 건물(`Belt`, `Miner`, `Crafter`, `Storage`, `Splitter`, `Merger`)의 타입별 필수 상태/의사결정/버퍼 컴포넌트 원자적 초기화. 요청 및 현장의 `PlacementStamp`를 건물 엔티티로 승계. 등록 프리팹이 없으면 Null과 중단 진단으로 생성 거부(F-024). 무DB fallback은 제거했으며 테스트도 명시적인 프리팹 DB를 구성한다. 요청 엔티티는 단일 프레임 내 파괴(`Consume-on-Apply`).
  - 후속 연결: Task 7.4 공사 자재 수령 계약 후, Task 7.5의 Completion Job이 별도 Spawn 요청 없이 `BuildingLifecycleUtility.SpawnBuilding`을 직접 호출하여 완공 건물로 전환한다. 미이식 5종 건물(`PowerPole`, `CoalGenerator`, `DroneStation`, `ResearchBuilding`, `MainFacility`) 전용 컴포넌트는 Phase 8·9·10에서 확장.
  - 당시 검증 기록(새 실행 수가 아님): `Phase7BuildingLifecycleTests` 10개 단위/통합 테스트 전수 통과 (Belt/Miner/Crafter/Storage/Splitter/Merger 컴포넌트 및 버퍼 초기화, `BuildingConfig` 속도·용량 반영, 프리팹 인스턴스화, Strict Fail 거부, 회전 Footprint 유효 크기, 무효 타입 거부). 전체 EditMode 테스트 244/244 Pass (100%).
  - 완료 기준: 현재 이식 대상 건물이 일관된 초기 상태로 생성되고 실패 시 잔류 엔티티·점유가 없다.

- [x] **Task 7.4: 공사 자재 요구량·수령 계약 구현**
  - 현재 진행(2026-10-12): **건설 코어·실물 정산 코드 연결 확인**. 현장 요구 버퍼와 Stored 실물에 Drone Lifecycle의 실제 공급 성공분을 반영한다. 과거 공사 운송의 별도 등록/수령 처리는 없고 실제 신호 Producer·자동 운송은 Phase 9의 남은 범위다.
  - 선행 조건: Task 7.2 및 Item 소유권·저장 계약.
  - 구현 범위·상태 소유자: `ConstructionMaterialRequirementElement`에 `ReservedQuantity` 및 순수 계산 프로퍼티(`RemainingRequired`, `RemainingToReserve`, `IsSatisfied`, `IsFullyReserved`)를 추가하여 운송 예약과 실제 도착 자재를 구분하는 순수 데이터 계약 정립. `SupplyConstructionMaterialRequest` 요청 컴포넌트(`IRequestComponent`) 및 Phase 5 `StateApplyGroup`의 `ConstructionLifecycleApplySystem` 내부 `ConstructionMaterialApplyJob` 구현. 현장 유효성, 요구 품목 일치, 잔여량 존재 검증 후 자재 수령(`DeliveredQuantity` 증가, `ReservedQuantity` 차감), 현장 진행도(`Progress`) 갱신, 아이템 소유권의 현장 이전(`ItemOwnership.Stored(TargetSite)`), 렌더링 비활성화(`DisableRendering`), 현장 `StoredItemElement` 버퍼 등록을 처리한다. 수량/진행/버퍼는 Job에서 직접 갱신하고 Owner/렌더 태그와 요청 삭제는 EndStateApply에서 반영한다. 잘못된 품목·초과 전달·무효 현장 시 Strict Rejection(수령 거부, 아이템 소유권 유지, 요청만 소비) 적용. 단일 프레임 내 요청 엔티티 파괴(`Consume-on-Apply`).
  - 후속 연결: 실제 드론의 아이템/목적지 용량 예약 및 운송 도착은 Task 9.6·9.12·9.20에서 구현·연결. 완공 시점의 자재 소비 및 건물 전환은 Task 7.5에서 처리.
  - 검증: `Phase7ConstructionMaterialTests` 8개 단위/통합 테스트 전수 통과 (순수 계산 프로퍼티, 단일 유효 수령 및 소유권 이전/버퍼 등록, 기존 예약 차감, 잘못된 품목 거부, 초과 전달 거부, 무효 현장 거부, 동일 프레임 다중 전달 누적, 묶음 초과 시 한도 내 부분 수령 및 잉여분 거부). 전체 EditMode 테스트 252/252 Pass (100%).
  - 완료 기준: 자재 수량·품목·소유권이 일치하며 수령 성공 여부를 운반 측에 전달할 수 있다.

- [x] **Task 7.5: 공사 완료 및 건물 전환 구현**
  - 현재 진행(2026-10-12): **코드 구현 확인**. BuildingStateApply 마지막에서 실제 도착량·활성 월드 방해물을 검사하고 공통 Spawn 성공 후에만 EndBuilding에 현장/자재 삭제를 기록한다. 이번 드론 틱 마지막 납품/회수는 다음 건물 틱에 완공으로 반영된다.
  - 2026-10-11 티어·표시 이름 현행 계약: BuildingStateApply 마지막의 ConstructionCompletionApplyJob은 ConstructionSite.TargetBuildingType/TargetTier와 기본 크기·PlacementStamp를 공통 Spawn에 전달한다. 성공 후 자재/현장 삭제를 EndBuilding에 기록하고 다음 Synchronization이 같은 조합의 공간 정보를 등록한다. 도착량 부족·바닥 차단·Spawn 거부의 보류/보존 정책을 유지한다. 이번 상위 티어 통합 사례는 자재 도착을 직접 준비한 것으로 실제 드론 운송을 증명하지 않는다. [명세](../Specifications/BuildingTierAndDisplayName.md).
  - 선행 조건: Task 7.3~7.4.
  - 초기 구현 기록(현재 계약은 위 후속 참조): Phase 5 `StateApplyGroup`의 `ConstructionLifecycleApplySystem` 내부 `ConstructionCompletionApplyJob` 구현. 자재 요구량 충족(`requirement.IsSatisfied`) 및 바닥 아이템 청소 플래그(`AwaitingItemClearance`) 해제 확인 시, 공통 Spawn이 non-Null을 반환한 뒤에만 현장 보관 자재 삭제를 ECB에 기록한다. Null이면 같은 틱의 정상 수령을 포함해 현장과 자재를 보존한다. `BuildingLifecycleUtility.SpawnBuilding`을 통해 같은 EndStateApply ECB에 기록하여 완공 건물 엔티티를 생성하고 속성(`TargetBuildingType`, `GridPosition`, `Direction`, `BuildingFootprint`, `PlacementStamp`, `LocalTransform`) 및 타입별 필수 상태/의사결정/버퍼 컴포넌트 100% 승계 주입. 기존 `siteEntity`를 파괴하여 `siteEntity` 소멸과 완공 건물 탄생이 동일 ECB Playback 틱에 발생하도록 함으로써 점유 공백(Spatial Void) 0을 달성.
  - 후속 연결: 드론 예약 상태와의 실제 연동은 Task 9.20에서 검증. 공사 취소 및 미완성 자재 반환은 Task 7.6에서 구현.
  - 당시 검증 기록(새 실행 수가 아님): `Phase7ConstructionCompletionTests` 8개 단위/통합 테스트 전수 통과 (자재 충족 시 현장/자재 소비 및 건물 생성/컴포넌트 초기화, 자재 부족 시 전환 보류, 바닥 청소 플래그 활성 시 완공 보류 및 해제 시 완공, 자재 공급 파이프라인 연계 및 완공 상태 검증, 복수 품목 자재 전수 충족 검사, 요구량 0 즉시 완공, 공간 인덱스 점유 연속성 유지 검증, 무효 목표 타입 차단). 전체 EditMode 테스트 248/248 Pass (100%).
  - 완료 기준: 중복 소비·중복 생성·점유 공백 없이 현장에서 건물로 전환된다.

- [x] **Task 7.6: 공사 취소 및 도착 자재 반환 구현**
  - 현재 진행(2026-10-12): **코드 구현 확인**. ConstructionCancelCommandSystem이 Cancelled를 즉시 기록해 중복 반환을 막고 도착 실물의 Owner/좌표/렌더와 현장 삭제를 EndCommand에 기록한다. 다른 드론의 미도착 적재품은 새로 생성/이전하지 않으며 Drone Lifecycle 재배정에서 보존한다.
  - 선행 조건: Task 7.2·7.4~7.5.
  - 구현 범위·상태 소유자: Phase 5 `StateApplyGroup`의 `ConstructionLifecycleApplySystem` 내부 `CancelConstructionApplyJob` 구현. 같은 시스템에서 취소→수령→완공 JobHandle을 연결한다. `CancelConstructionRequest`를 소비하여 취소 승인 시 `site.Flags |= ConstructionSiteFlags.Cancelled`를 즉시 마킹하여 동일 프레임 [자재 도착/수령] 및 [완공 전환]을 원천 차단(Cancel Wins 정책). 현장에 이미 보관된 자재(`StoredItemElement`)를 `ItemOwnership.WorldItem`으로 전환하고 `DisableRendering` 제거 및 현장 그리드 좌표로 방출. `ECB.DestroyEntity(TargetSite)`를 통해 현장을 파괴하여 Phase 6 공간 동기화 시 건물 공간 점유를 안전하게 자동 해제. 중복 취소, 기파괴/완공 건물 대상 취소에 대해 요청만 안전하게 소비(Idempotent Drop / Strict Rejection).
  - 후속 연결: 운송 중 취소·화물 복구 및 드론 예약 롤백 연동은 Task 9.15~9.16·9.20에서 연결한다.
  - 검증: `Phase7ConstructionCancelTests` 7개 단위/통합 테스트 전수 통과 (빈 현장 즉시 취소 및 파괴, 보관 자재 보유 현장 취소 시 WorldItem 전환 및 DisableRendering 제거/바닥 방출, 완납 현장 취소 시 자재 반환 및 완공 차단, 동일 프레임 자재 공급+취소+완공 경쟁 시 Cancel Wins 및 자재 드론/월드 보존, 동일 프레임 중복 취소 Idempotent Drop, 무효/완공 대상 취소 Strict Rejection, 취소 시 공간 인덱스 해제). 전체 EditMode 테스트 255/255 Pass (100%).
  - 완료 기준: 확인된 취소 우선순위(Cancel Wins)를 지키고 자재·현장·공간 예약이 중복 정리되거나 남지 않는다.

- [x] **Task 7.7: 건물 철거 코어 및 내용물 반환 구현**
  - 현재 진행(2026-10-12): **코어 구현 확인**. Command의 PendingBuildingDemolition 승인·작업 중단, Spawn Admission의 생성 폐기, 기존 실물 반환·종류/티어별 비용 환급을 EndBuilding에 적용한다. 현재 Item Lifecycle/Ownership이 철거 승인 요청을 다시 읽는 과거 계약은 아니다. 드론 철거 실행·정거장 재배치는 별도다.
  - 2026-10-11 티어·표시 이름 현행 계약: 철거 환급은 완공 BuildingType.Type/Tier와 일치하는 비용 행만 조회하며 공통 ItemLifecycleUtility로 EndBuilding에 생성한다. 기존 Stored/Product 실물 반환·Indestructible/Destroy 보호는 유지했다. 상위 창고의 Copper 3개만 환급하는 통합 사례와 기존 철거 19/19가 통과했다. 환급 실패 보상/전체 rollback은 별도다. [명세](../Specifications/BuildingTierAndDisplayName.md).
  - 선행 조건: Task 7.3, Item 소유권 경계 및 Phase 6 연결 변경 처리.
  - 초기 구현 기록(현재 계약은 위 후속 참조): `BuildingDemolitionCommandSystem`이 검증·중복 제거한 요청을 `BuildingLifecycleApplySystem`이 소비한다. 기존 Stored/Product 실물을 반환하고 건축 비용을 공통 `ItemLifecycleUtility`로 새로 생성하며 건물/요청 삭제는 EndStateApply에 기록한다. `ItemLifecycleApplySystem`도 같은 승인 요청을 읽어 철거 대상 생산 결과와 Storage/Product Spawn을 거부한다. 선소비 재료·광물의 보상은 없으며 전력/드론 연결은 후속 범위다.
  - 후속 연결: 전력 등록 해제는 Task 8.7, 드론 철거 실행은 Task 9.17, 정거장 드론 재배치는 Task 9.19에서 완료한다.
  - 당시 검증 기록(새 실행 수가 아님): `Phase7BuildingDemolishTests` 13개 단위/통합 테스트 전수 통과 (빈 건물 철거, 보관/생산 내용물 월드 방출, 건축 비용 자재 100% 환급 스폰, Indestructible 파괴 거부, 무효/공사현장 대상 거부, 동일 프레임 중복 철거 멱등성, 공간 인덱스 해제, 벨트 철거 시 아이템 정적 보존, 프리팹 DB 아이템 환급 스폰, 프리팹 누락 시 Strict Fail Policy, 동일 틱 이동 아이템 타이밍 무결성 검증, 동일 프레임 물류 적용 후 철거 순서 보장 검증, EndCommandECB 즉시 커밋 검증). 전체 EditMode 테스트 268/268 Pass (100%).
  - 완료 기준: 승인 철거의 중단·반환/환급·삭제를 현재 Command/EndBuilding 경계에 맞추고 공간 인덱스를 Synchronization에서 갱신한다. 전력·드론 연결은 해당 후속 Task로 구분하며 전체 틱 rollback을 보장하지 않는다.

- [x] **Task 7.8: 건설 코어 수명주기 통합 검증**
  - 현재 진행(2026-10-12): **기존 코어 완료 기록 유지·현재 fixture 정의 확인**. 현재 Phase7EndToEndConstructionPipelineTests는 기본 수명주기·취소·벨트 방향/재배치·티어 창고 흐름을 갖는다. 아래 초기 테스트명/6페이즈/2-Sync Point 기록은 당시 범위이고 이번 새 실행 결과가 아니다. 직접 자재 준비를 실제 드론 운송으로 확대하지 않는다.
  - 2026-10-11 티어·표시 이름 현행 계약: 현재 선택 통합 흐름은 Command→건물 도메인→Synchronization과 EndCommand/EndBuilding을 사용한다. 기존 4사례에 Storage 3티어의 독립 JSON 정의/해금/원형/크기/용량/자재/직접 생성/환급 사례를 추가해 해당 클래스 5/5를 확인했다. 다른 티어 벨트 요청의 방향 변경 방지도 기존 사례에서 검사했다. 전체 111/111은 관련 8클래스의 선택 실행이며 전체 프로젝트/실제 드론 이동·Play Mode 통과 수가 아니다. [명세](../Specifications/BuildingTierAndDisplayName.md).
  - 선행 조건: Task 7.2~7.7.
  - 초기 구현 기록(현재 계약은 위 후속 참조): 6대 Phase(`Command` → `Decision` → `Reservation` → `Execution` → `StateApply` → `Synchronization`) 전체가 결합된 `GameSimulationGroup` 메가 파이프라인 상에서 건설 코어 수명주기(배치 요청 → 현장 실체화 → 자재 공급 → 완공 전환 → 생산/물류 가동 → 철거/환급 및 취소) 전 과정을 유기적으로 통합 검증. 2-Sync Point 아키텍처(`EndCommandEntityCommandBufferSystem`, `EndStateApplyEntityCommandBufferSystem`) 및 StateApply 내 실행 순서(`RoutingApplySystem`, `BuildingItemStorageApplySystem` → `BuildingLifecycleApplySystem`)가 실체화/스폰/철거 트랜잭션과 점유 공백(Spatial Void) 0을 정확히 보장함을 확인.
  - 후속 연결: 실제 드론 운송·자재 배차 및 정거장 연동은 Phase 9, 전력망 연결 및 발전기 가동은 Phase 8에서 검증.
  - 당시 검증 기록(새 실행 수가 아님): `Phase7EndToEndConstructionPipelineTests` 5개 E2E 통합 테스트 전수 통과:
    1. `Test01_HappyPath_FullConstructionLifecycle_ToOperationalLogisticsAndDemolition`: Miner 배치 → 현장 점유 → 자재 공급 → 점유 공백 없는 완공 스폰 → 30틱 연속 시뮬레이션 및 실제 채굴 생산 → 철거 및 채굴물/자재 반환/공간 해제 완전 수명주기 검증.
    2. `Test02_CancelWins_PartialSupply_ToCancellation_RefundsWorldItemsAndReleasesSpatial`: Storage 자재 부분 납입 후 동일 틱 추가 자재+취소 동시 인큐 시 Cancel Wins 우선순위 적용, 기납입 자재 WorldItem 바닥 방출 및 현장 파괴/공간 해제 검증.
    3. `Test03_BeltOverwrite_UpgradeLifecycle_SameBeltRedirectAndDifferentBeltUpgrade`: 동일 벨트 방향 재배치 시 무공사 즉시 Direction 갱신 및 이종 건물 충돌 배치 거부/벨트 보존 검증.
    4. `Test04_ImmediateRepositioning_DemolishAndReconstructOnSameTile`: Storage 철거 후 자재 환급 및 바닥 정돈 직후 동일 타일 신규 Belt 배치, `AwaitingItemClearance` 회피 및 자재 공급 후 Belt 정상 완공 전환 검증.
    5. `Test05_MultiTickContinuousSimulation_StressAndWorldInvariantZeroViolation`: 채굴기-벨트-창고 물류 라인과 공사 현장이 공존하는 복합 환경에서 100틱 연속 시뮬레이션 구동 시 `WorldInvariantValidationSystem` 위반 0건 스트레스 검증.
    - 전체 EditMode 테스트 273/273 Pass (100%).
  - 완료 기준: 현재 Command→Building→Drone→Commit→Synchronization과 각 ECB 경계에서 건설 코어 수명주기·공간/아이템 정합성을 확인하고 테스트 주입과 실제 수행부의 범위를 구분한다. 이후 구조 변경의 새 실행 증거는 별도로 확인한다.

---

## ⚡ [Phase 8] Power Grid (전력 네트워크)

- [x] **Task 8.1: 전력 설정·컴포넌트·상태 소유권 정의**
  - 완료(2026-10-12, 후속 정리 반영): BuildingConfig에 종류·Tier별 power 객체를 통합하여 기존 Init에서 검증/ECS 게시한다. 전력 컴포넌트 10개·unmanaged 능력 설정·순수 정사각형/OR 범위 유틸리티를 정의하고 [전력 명세](../Specifications/PowerGrid.md)에 소유권·실행 순서·에너지 보존·경계 기대 결과와 후속 검증 담당을 확정했다. 별도 PowerConfig.json/meta는 이관 후 제거했다. 후속 사용자 지시로 전력 전용 시계·연료 참조 버퍼·실제 발전 결과·망 집계의 독립 컴포넌트 4개를 제거했다.
  - 완료 범위·후속: 기존 설정·기본 정의·계약은 유지한다. 2026-10-13 PowerPole/PowerGrid의 StableId 필드와 발급 주석을 제거하고 아래 8.2 코드를 다시 구현했다. 현재 연료/생산 연결과 페이즈·소비 정책은 2026-10-15 Task 8.6~8.7과 전력 명세를 따른다. 연구/충전·주 시설 자동 생성은 후속이다.
  - 레거시 검토: 전력 설정은 전신주 범위·발전기 출력·석탄 에너지·소비 건물을 전체 검증한 뒤 게시했다. 파일 조회 helper와 도메인 검증/게시를 분리하고 현재 설정 실패 계약을 재사용한다. 망 집계와 셀 공급 범위의 소유자·원본/파생 데이터를 구분한다.
  - 선행 조건: 현재 생산·공간·건물 생성 계약과 기존 전력 설정 확인.
  - 구현 범위·상태 소유자: 전신주·발전기·소비자·망 상태를 정의하고 토폴로지, 공급 범위 조회, 외부 공급 데이터의 책임을 구분한다.
  - 사용자 확정 — 실행 순서·프레임 기준(2026-10-15 갱신): Command/EndCommand → Preparation → Power(Decision → Reservation → Execution → StateApply → EndPower) → Building/EndBuilding → Drone/EndSimulation → Synchronization. Preparation은 전력과 독립적인 작업 의도·수요, Power는 선택·망·가용량·예산·연료 소비·공급 게시를 소유한다. 배정량은 이번 틱 소비로 확정하며 PowerUsageResult와 소비 이후 정산은 없다. 망 합계는 임시 집계이며 망/전환 연료 실물은 EndPower에서 확정한다. 새 입고/생성은 다음 틱부터, 승인 철거는 이번 틱부터 반영한다. 모든 도메인은 같은 min(World DeltaTime, GameConstants.MaxSimulationDeltaTime)을 사용하고 별도 시계/Tick 필드를 두지 않는다. 무작업/단절/비활성도 초기화하며 그룹 순서와 Job 의존성을 함께 선언한다.
  - 사용자 확정 — 수요: 실제로 작업 가능한 소비자만 수요를 발생시킨다. 연결된 모든 소비자의 정격 소비량을 항상 합산하던 레거시를 그대로 이식하지 않는다. 정격 능력의 설정 원본, 각 도메인이 작성하는 이번 틱 수요, 전력이 게시하는 공급 결과를 구분한다. 공급률 0 자체를 수요 0의 근거로 사용하지 않는다. 전력을 제외한 작업 조건으로 수요를 준비해야 정전 후 복구할 수 있다.
  - 사용자 확정 — 설정 원본·티어: PowerConfig.json의 전신주·발전기·소비자·석탄 연료 설정을 BuildingConfig.json으로 통합했다. 종류·Tier별 역할의 power 객체를 BuildingConfigElement.Power로 게시하며 다른 정의를 상속/대체하지 않는다. 석탄 에너지·저장 슬롯과 역할별 필수 필드/값은 JSON·공개 게시·사전 등록에서 검증한다. 별도 PowerConfig.json은 제거했다. [건물 설정 명세](../Specifications/BuildingTierAndDisplayName.md), [전력 명세](../Specifications/PowerGrid.md)를 따른다.
  - 설정 실패·게시: 통합한 전력 필드는 BuildingConfig 검증에 포함하고 성공한 전체 정의를 원자적으로 게시한다. 현재 파일/파싱/검증 실패의 로그·미게시·SimulationFatalError·자동 재시도 없음 계약을 재사용한다. 별도 PowerConfig 로더나 독립 설정 원본을 추가하지 않는다. 전력 파생 게시 데이터를 따로 둘 경우의 준비 검사는 통합 원본과 일치해야 한다.
  - 사용자 확정 — 범위: 전신주의 공급 범위와 전신주 간 연결 범위는 각각 단일 스칼라 range로 정의하여 X/Y에 같은 값을 적용한 정사각형으로 조회·표시한다. supplyRangeX/Y·connectionRangeX/Y 구분은 제거할 대상이며 공급과 연결의 두 역할은 유지한다. range는 중심 ± range의 경계를 포함하는 셀 반경이다. range 5는 11×11 셀이다. 서로 다른 Tier/range의 전신주도 둘 중 한쪽 연결 범위에 상대 중심이 포함되면 연결한다(OR). 범위 표시 UI 구현은 Task 10B의 후속이다.
  - 사용자 확정 — 작업 자격: 기존 Miner의 CanMine, Crafter의 CanStartCraft/CanAdvance로 이번 틱 수요를 도출한다. Crafter가 진행 완료 후 결과 확정/출력 공간만 기다리는 상태는 수요 0이다. 미완료 Crafter의 출력 공간과 진행 허용 여부는 현재 도메인 결정 계약을 유지한다. 연구는 전력 외 신규 착수 조건 또는 진행할 로컬 주기를 기준으로 수요를 준비한다.
  - 사용자 확정 — 무전력 제작: 공급률 0이면 Crafter의 신규 착수·재료 선소비는 대기하고 이미 착수한 진행도·실물은 보존한다. 완료된 제작물의 결과 확정·출고는 허용하며 기존 출력 공간 검사는 유지한다. 연구는 기존 확정 명세의 무전력 신규 착수 금지와 진행 보존을 따른다.
  - 사용자 확정 — 배정량 확정 소비(2026-10-15 갱신): Power에서 전역 입력/예산을 모두 검증한 뒤 발전기 연료를 전환·차감하고 공급을 한 번 게시한다. 배정받았지만 실행하지 못한 전력과 조기 완료의 잔여분도 소비로 확정한다. 소비 이후 환급·재정산·같은 틱 재배분은 없으며 마지막 연료의 부분 공급과 발전기 예산/소비자 배정 한도를 검사한다. Power 실패는 같은 틱 Building/Drone을 차단하지만 Command rollback은 보장하지 않는다.
  - 사용자 확정 — 충전 수요 준비: 드론 도메인은 EndBuilding 뒤에 실행되지만 공통 공급 계산 전에 충전 후보·배터리 상태로 이번 틱 충전 예산을 준비한다. 이번 틱 새로 귀환한 드론은 다음 틱부터 충전한다. 후속 배차 등으로 실행되지 않은 충전도 Power에서 배정량을 소비로 확정하며 환급/재정산하지 않는다. 실제 충전 Writer는 Task 9.14에서 구현한다.
  - 사용자 확정 — 연료 저장: 석탄 발전기는 종류·Tier별 BuildingConfig.storageCapacity와 기존 Coal MaxStack을 사용한다. 사용자 지정 임시 1슬롯을 실제 JSON에 설정했고 현재 슬롯당 용량은 50이다. 별도 실물 10개 상한은 유지하지 않는다. 실제 공통 생성/완공·일반 입고 연결은 Task 8.6에서 구현했다.
  - 사용자 확정 — 연료 에너지: coalEnergyPerItem은 발전기 종류·Tier별로 석탄 한 개에서 얻는 에너지를 설정한다. CoalGenerator Tier 1의 임시 600을 실제 JSON/게시 데이터에 이관했으며 향후 밸런스 조정에서 변경한다.
  - 후속 구현: 그룹 내부 순서·각 컴포넌트의 Writer/Reader·프레임 초기화·에너지 필드·석탄 전환/정산/삭제 경계와 틱별 기대 결과는 전력 명세로 확정했다. 후속 Task에서 실제 Writer와 Job 의존성을 연결한다. 별도 전신주·망 번호 발급은 두지 않으며 망 생성·병합/분리 및 공급 전신주 선택 상세는 Task 8.2~8.4에서 구현한다. 이미 확정한 게임 규칙을 다시 미확정으로 취급하지 않는다.
  - 후속 연결: 망 구성·연결·배분·연료는 Task 8.2~8.6에서 구현한다. Task 8.5~8.7·9.14·10A.3은 여기서 확정한 프레임 계약을 재사용한다. 기존 규칙으로 결정할 수 없는 동작 차이는 사용자 확인 후 확정한다.
  - 검증: 통합 설정의 누락·중복·잘못된 값·종류/Tier 대응과 도메인 간 데이터 읽기·쓰기 계약을 검사한다. 연료 없음→입고, 마지막 연료 소진, 공급 중 설치/철거, 작업 가능/대기 전환, 수요 산정 후 실행 실패, 충전 후보 변경의 틱별 기대 결과와 담당 검증 Task를 기록한다. Task 8.7에서 실제 GameSimulationGroup·ECB 경계로 검증하고 드론·연구 연결은 각 후속 Task에서 확인한다.
  - 완료 기준: 생산 도메인이 전력망 내부 구조를 몰라도 공급 결과를 소비할 수 있는 계약과 실행 순서·프레임 기준·경계 사례의 기대 결과가 확정된다. 후속 구현·실행 검증이 남아 있다는 사실은 별도로 기록한다.
  - 새 검증(2026-10-12): Editor 재컴파일 completed/failed false/errors 없음·ready/비리로드·현재 runtime/editor DLL을 확인했다. 선택 EditMode는 신규 전력 계약 26, 기존 건물 Init 1·정의 24·프리팹 준비 19로 총 70/70 통과했다. 실제 GameSimulationGroup 전력 틱·연료/생산/충전·Play Mode·베이킹·Player 빌드·성능은 미실행이며 각 후속 검증에 남긴다.
  - 후속 제거 검증(2026-10-12): 소비처에 독립적인 FuelEnergyState 이름을 유지하고 전력 전용 시계·연료 참조 버퍼·발전 결과·망 집계 컴포넌트 4개와 남은 프레임 Tick 필드를 제거했다. 프레임 초기화·기존 보관 연료 재검사·일시 집계/정산 계약을 갱신하고 Editor 컴파일을 확인했다. 위 70사례는 초기 Task 8.1 검증 기록이며 이번 정의 정리 후 테스트를 재실행한 수치가 아니다.

- [x] **Task 8.2: 전신주 추가 및 망 생성·병합 구현**
  - 완료(2026-10-13): 롤백 후 공통 전신주 부착·프레임 결정·망 생성/병합·공개 연결 인덱스/Fence를 다시 구현하고 Unity 컴파일 확인과 관련 선택 EditMode를 통과했다. 전력 전용 그룹은 추가하지 않았다. 제거/분리와 공급/연료·생산/충전은 후속이다.
  - 사용자 확정 — 생성 범위: 후속 공통 직접 Spawn/현장 완공은 일반 전신주와 주 시설에 전신주 태그·초기 연결을 부착한다. 발전기/소비자 연결은 8.7, 주 시설 자동 생성은 9.22다.
  - 사용자 확정 — 연결: 종류·Tier의 단일 connection range를 셀 반경 정사각형으로 검사하고 한쪽 범위에 상대 중심이 포함되면 연결한다(OR). 공급 범위 중첩만으로 병합하지 않는다.
  - 사용자 확정 — 식별/순서: 전신주와 망은 Entity로 식별하고 망은 전신주와 별도 Entity로 관리한다. 별도 StableId/번호 발급을 두지 않으며 기존 PlacementStamp.Compare를 사용한다. 기본 정의의 남은 StableId 필드를 제거했다.
  - 사용자 확정 — 병합: 기존 유효한 망 소속 전신주 중 설치 순서가 가장 앞선 전신주의 망 Entity를 유지한다. 새 전신주의 설치 기록이 더 빨라도 기존 망 중에서 유지 대상을 선택한다. 소속 변경과 나머지 망 삭제는 같은 ECB 경계에 공개한다.
  - 사용자 확정 — 같은 틱: 연결 계산 결과는 이번 틱 공급 계획에 사용하고 실제 망 생성/소속 변경은 EndBuilding에 공개한다. N틱 EndBuilding에 생성된 참가자는 N+1부터 공급에 참여한다. ECB 예약 Entity를 살아 있는 망이나 다른 ECB의 참조로 사용하지 않는다.
  - 현재 소유자/자료(2026-10-15): PowerGridTopologyReservationSystem은 PowerReservation에서 이전 공개 망과 전신주 입력을 읽어 연결/대표/유지 망을 계획한다. PowerGridTopologyApplySystem은 PowerStateApply에서 준비된 계획을 EndPower에 기록한다. 계획 필드는 RepresentativePole·RetainedGrid이며 대조 전용 ExpectedGrid는 제거했다. 별도 준비 맵/Fence나 예약 Entity는 없고 PowerGridConnectionSyncSystem이 공개 연결 인덱스/Fence를 소유한다. 이후 단계는 중간 Writer가 없는 원본을 다시 대조하지 않는다.
  - 선행 조건: Task 8.1.
  - 후속 연결: 제거/분리는 8.3, 일반 건물 공급 연결은 8.4에서 구현한다.
  - 검증 계획: 실제 Spawn/완공·기존 그룹/ECB 경계에서 다음 틱 참여, 독립 망, 연결 다리, 기존 망 유지, 범위 경계/다른 Tier, 공급만 겹치는 경우, 중복 등록/무효 참조와 공유 자료의 접근 의존성을 확인한다.
  - 완료 기준: 연결 규칙에 맞는 망만 병합되며 중복 등록·유효하지 않은 망 참조가 없다. 폐기한 재구현의 과거 성공 결과는 다음 구현의 증거로 사용하지 않는다.
  - 롤백 검증(2026-10-13): 재구현의 추가 파일 8개를 제거하고 수정한 C# 5개가 136a44f74와 Git 정규화 해시로 일치함을 확인했다. 소스 차이는 기존 전력 그룹 삭제 7개만 남았으며 git diff --check는 통과했다. 재컴파일은 CLI의 STATUS_PIPELINE_LOAD_PENDING으로 시작되지 못했고 기존 성공 결과를 롤백 이후 실행 증거로 사용하지 않는다.
  - 새 구현 검증(2026-10-13): 현재 Unity 참조·Entities 소스 생성기로 수행한 별도 런타임/Editor C# 빌드는 각각 오류 0·경고 0이다. 새 통합 5사례는 Spawn/완공→다음 틱 참가·중복 생성 방지, 기존 망 유지/ECB 공개, 다른 Tier의 OR/공급만 겹침, 무효 참조 Fatal/이전 결정 제거, 인덱스 Reader/재할당/빈 맵 수명을 대상으로 한다. 기존 설정 검증으로 이 실제 망 흐름을 다루지 않아 추가했다. Unity CLI 컴파일은 Pipeline 인스턴스 연결 오류로 시작하지 못했으며 새 5사례와 기존 공통 생성/완공 선택 EditMode는 미실행이다. 연결 복구 후 실행 검증을 완료해야 한다.
  - 연결 복구 후 검증(2026-10-13): Unity 재컴파일 상태 up_to_date/failed false/errors 없음/compilationFailed false, Editor ready·컴파일/리로드 없음·Play stopped, runtime/Editor DLL 최신성을 확인했다. 선택 EditMode는 Phase8PowerTopologyTests 5/5, Phase8PowerContractTests 26/26, Phase7BuildingLifecycleTests 11/11, Phase7ConstructionCompletionTests 8/8로 이번 실행 총 50/50이며 실패·생략·불확정은 0이다. 위 미실행 상태와 완료 보류를 해제한다. 과거 폐기한 구현의 성공 수치와 합산하지 않으며 Play Mode·베이킹·Player 빌드·성능과 실제 공급/연료/생산/충전 검증은 미수행이다.
  - 명명/책임 분리 후 검증(2026-10-13): Reservation 시스템/파일 이름과 참조·문서를 갱신하고 망 생성/연결 변경/병합 삭제 기록을 BuildingStateApply의 Apply 시스템으로 이관했다. 기존 meta GUID와 EndBuilding 공개 계약을 유지했다. Unity 컴파일 completed/failed false/errors 없음·ready/비컴파일/비리로드·최신 runtime/Editor DLL을 확인했다. 선택 EditMode는 토폴로지 6/6·공통 생성 11/11·공사 완공 8/8로 이번 25/25, 실패·생략·불확정 0이다. 기존 병합 검증에 Reservation/Apply/Playback 경계 검사를 더하고 준비 후 연결 변경 실패의 회귀 1사례를 추가했다. 앞선 50사례와 합산하지 않으며 전력 설정 테스트·Play Mode·빌드·성능은 이번에 재실행하지 않았다.

- [x] **Task 8.3: 전신주 제거 및 망 분리 구현**
  - 완료(2026-10-13): 기존 Reservation/StateApply에 망 분리 유지권·비활성 해제·빈 망/모든 런타임 참조 정리를 연결하고 기존 철거 요청/Command/EndBuilding 경계로 검증했다. 공급 전신주 검색·발전기/소비자 제품 등록·발전/연료/생산/충전은 후속이다.
  - 사용자 확정 — 1A: 기존 망의 남은 활성 멤버 중 PlacementStamp.Compare가 가장 앞선 전신주의 연결 집합만 기존 망을 유지하고 다른 집합은 새 망을 생성한다. 이전 대표가 없어져도 남은 멤버로 판단한다.
  - 사용자 확정 — 2A: 기존 망별 분리 유지권을 먼저 확정한 뒤 집합별 기존 병합 규칙을 적용한다. 병합으로 남는 기존 망을 다른 집합에 재배정하지 않는다.
  - 사용자 확정 — 3A: Disabled 전신주는 Grid를 Null로 해제하되 자기 전신주 참조·엔티티·설치 기록을 보존한다. 재활성화 시 다시 연결하며 비활성 상태의 과거 소속을 복원하지 않는다.
  - 레거시 검토: 전신주 추가/제거 후 참가자 연결을 정리하고 남은 연결 성분으로 망을 재계산했다. 이전 전체 재구축은 참고 방식이며 제거·재연결의 공개 시점은 Task 8.1의 프레임 계약을 따른다.
  - 선행 조건: Task 8.2.
  - 구현 범위·상태 소유자: Reservation이 활성 원본으로 연결 집합/유지권을 작성하고 Apply가 최종 검사 후 대표별 망 생성·기존 런타임 참조 정리·미사용 망 삭제를 같은 EndBuilding ECB에 기록한다. 기존 망 전체를 조회하여 마지막 전신주 소실 뒤에도 회수한다. 준비 실패/누락은 빈 망으로 처리하지 않아 정상 변경을 시작하지 않는다. Prefab은 정리 대상에서 제외하고 Disabled 참가자의 참조는 정리한다.
  - 기존 철거/후속 연결: 일반 전신주의 실제 삭제는 현재 DemolishBuildingRequest → BuildingDemolitionCommandSystem/EndCommand → BuildingLifecycleApplySystem/EndBuilding 경로를 재사용한다. 살아 있는 선택 전신주의 새 망으로 참조를 맞추고 무효 선택은 해제한다. 다른 공급 전신주 검색은 8.4, 발전기/소비자 제품 부착과 실제 생산 연결은 8.7이다.
  - 검증: 끝 전신주·연결 다리·마지막 전신주 제거, 반복 제거와 재설치를 검사한다.
  - 완료 기준: 분리된 망과 남은 전신주의 소속이 일치하고 사라진 연결이 남지 않는다.
  - 새 검증(2026-10-13): Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·ready/비컴파일/비리로드/Play stopped와 runtime/Editor DLL 최신성을 확인했다. 선택 EditMode는 Phase8PowerTopologyTests 13/13·Phase7BuildingDemolishTests 19/19로 이번 32/32, 실패·생략·불확정 0이다. 기존 토폴로지 6사례에 실제 철거 기반 끝/다리/이전 대표 제거, 마지막/중복 제거·재설치, Disabled 해제/재활성화, 동시 분리·병합 유지권, 기존 일반/Disabled 참조 정리와 입력 실패 보존의 7사례를 추가했다. 일반 소비자 참조 입력은 fixture이며 실제 제품 등록/공급 선택 증거가 아니다. 과거 성공 수치와 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능과 실제 발전/연료/생산/충전은 미수행이다.

- [x] **Task 8.4: 공급 범위 및 건물의 망 연결 구현**
  - 현재 상태(2026-10-14): PowerGridConnectionDecisionSystem이 BuildingDecision에서 활성 일반 참가자의 공급 전신주를 선택한다. 기존 Reservation이 토폴로지를 준비하고 PowerGridTopologyApplySystem이 둘을 최종 검사·연결 공개를 같은 EndBuilding ECB에 기록한다. 전력 배분/발전/연료/생산/충전 Writer는 후속이다.
  - 사용자 확정 **1A·2A·3A·4A**: 회전된 점유 셀 하나라도 공급 범위에 닿으면 후보이고, 전체 점유 셀↔전신주의 최소 제곱거리와 PlacementStamp.Compare 동률 규칙으로 매 틱 재선택한다. 유효한 기존 연결도 더 가까운 후보로 변경한다. 일반 참가자의 새 선택은 활성 상태에서만 수행하며 비활성 상태는 8.3의 기존 참조 정리만 적용하고 재활성화 시 다시 선택한다.
  - 공통 생성/완공: Miner·Crafter·ResearchBuilding·CoalGenerator·DroneStation에 PowerGridConnection과 비활성 PowerGridConnectionDecision을 부착한다. 전신주/주 시설은 자기 전신주 참조를 유지한다. EndBuilding에서 생성된 참가자/전신주는 다음 틱부터 선택에 참여한다. 발전기/소비자 역할·연료·수요·충전 상태 부착은 해당 후속 Task 범위다.
  - 레거시 검토: 이전 코드 지도에는 이미 회전 후 전체 점유 셀을 검사하고 셀↔전신주 중심 거리 제곱 최소인 전신주를 선택하는 동작이 기록되어 있었다. 첫 점유 셀에서 끝내거나 anchor/시각 중심 거리로 선택하지 않았다. 앞선 단일 좌표 문제 피드백과 시점이 다른 기록이므로 현재 V2 버그나 새 실행 근거로 합치지 않는다. 과거 별도 번호를 사용한 동률 규칙은 재사용하지 않으며 후속 동률 처리는 사용자 확정한 기존 PlacementStamp.Compare를 따른다.
  - 선행 조건: Task 8.2~8.3 및 건물 footprint 계약.
  - 구현 범위·상태 소유자: BuildingConfig의 단일 supply range와 원본 GridPosition·Direction·Footprint를 조회한다. 회전 직사각형 전체에서 가장 가까운 점유 셀을 계산하므로 첫 셀/anchor/시각 중심으로 선택하지 않는다. 참가자별 선택 Job은 병렬로 실행하며 전신주 Entity 목록과 읽기 전용 Lookup을 사용한다. 별도 위치/연결 스냅샷·준비 인덱스·Fence는 만들지 않는다. 결정의 활성 상태는 준비 완료이며 활성 Null 선택은 정상 단절이다.
  - 레거시 피드백 이관: 전체 footprint 규칙을 위 1A로 V2에 확정했다. 이번 테스트에서 1×1·2×2·3×3·Left/Right 회전 비정사각형의 생성/완공, anchor가 범위 밖인 경계 연결과 모든 점유 셀이 범위 밖인 단절을 실제 선택/공개 경계로 대조했다. 과거 실행 기록과 합산하지 않는다.
  - 후속 연결: 연결된 참가자의 발전·수요 계산은 Task 8.5에서 구현한다.
  - 검증: Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·ready/비컴파일/비리로드·최신 runtime/Editor DLL을 확인했다. 선택 EditMode는 전력 토폴로지/연결 21/21·공통 생성 11/11·공사 완공 8/8·기존 철거 19/19로 이번 59/59, 실패·생략·불확정 0이다. 기존 13사례에 없던 공급 선택 통합 흐름 8사례를 추가했다. 중첩 범위의 최소 거리/설치 동률·기존 유효 연결 변경·Reservation/Apply/Playback·비활성/재활성·실제 철거 후 재연결/단절·준비 후 범위 변경 실패의 보존을 확인했다. Play Mode·베이킹·Player 빌드·성능과 실제 전력 배분/생산은 미수행이다.
  - 완료 기준 충족: 공급 범위 중첩으로 망을 합치지 않으며 참가자는 선택한 전신주의 이번 틱 망으로 갱신된다. 새 망의 예약 Entity는 동일 ECB 안에서만 사용하고, 준비 누락/최종 검사 실패로 기존 망/참조를 삭제하지 않는다.
  - Decision 이관 후 검증(2026-10-14): 이름/파일/meta GUID와 실제 그룹/참조를 정리하고 토폴로지 Lookup·선행 준비 검사를 제거했다. 선택에 필요한 전신주 구성/설정은 Decision이 독립 검증하며 망 준비 실패 시 공개 차단은 기존 Apply가 소유한다. 기존 통합 사례에서 새 전신주의 토폴로지 준비 전 선택과 후속 토폴로지 실패 보존을 확인하고, 필수 위치 누락의 Decision 입력 실패 회귀 1사례를 추가했다. Unity 컴파일·ready/비컴파일/비리로드·최신 DLL과 선택 EditMode 22/22·기존 철거 19/19로 이번 41/41을 확인했다. 이전 59사례와 합산하지 않으며 Play Mode·베이킹·빌드·성능은 미수행이다.

- [x] **Task 8.5: 발전량·수요 집계 및 공급률 게시 구현**
  - 당시 완료(2026-10-14): BuildingPowerDemandDecisionSystem/PowerSupplyDecisionSystem이 수요·가용량과 초기화를, PowerBudgetReservationSystem이 예산을, PowerSupplyExecutionSystem이 최종 검사·공급 결과 게시를 소유한다. 실제 연료·생산/사용·연구·충전은 후속이다.
  - 확정 **1A·2A·3A·4A**: 정격 기준 동일 가동률·연료 한도 고정 후 부족분 분담, 역할/프레임 상태와 실제 Miner/Crafter 수요의 공통 생성 연결, Decision → Reservation → Execution 책임 분리, 기초 발전/이미 전환된 RemainingEnergy만 공급 근거로 사용한다. 미전환 Coal 실물은 8.6까지 세지 않는다.
  - 공통 생성/완공: MainFacility/CoalGenerator에 발전 역할·계획, MainFacility/Miner/Crafter/ResearchBuilding/DroneStation에 소비 역할·수요/예약/공급/사용 상태를 부착한다. 수요/가용량/예약의 enable은 준비 완료이며 정상 0과 미준비를 구분한다. 연구/충전의 실제 수요 Producer와 주 시설 자동 생성은 후속이다.
  - 레거시 검토: 공급률은 min(1, 가용 발전량/최대 수요), 실제 소비는 기초 발전을 먼저 사용하고 부족분만 석탄 발전에 배정했다. 석탄 가능 출력은 남은 에너지와 보관 연료로 제한했다. 수요 0·연료 부족과 실제 발전/소비/공급의 일치를 확인하고 망 집계를 영속 원본으로 중복 보관하지 않는다.
  - 선행 조건: Task 8.4.
  - 구현 범위·상태 소유자: 이번 틱 선택/Topology의 살아 있는 대표로 집계하여 공개 망 Entity가 아직 없거나 이전 망이 병합/분리되기 전에도 올바른 예산을 계산한다. 집계는 Job 내부 임시 double Native 맵이며 개별 에너지 float를 아래로 반올림해 소비자 총 배정이 발전기 예산을 넘지 않게 한다. 공개 Grid·공간/연결 캐시·영속 집계·별도 시계는 쓰지 않는다. 소비자 PowerSupplyReservation은 준비된 공급률/배정량이며 Execution이 이를 공개한다. Reservation은 준비·대표·유한 수치만 확인하고 Execution은 실제 소비할 연료 조건을 확인한다. 지정 Writer 이후의 대표/수요/연결은 재대조하지 않는다.
  - 현재 갱신(2026-10-15): Power 페이즈로 이관하고 배정량을 확정 소비로 변경했다. 보관 Coal 실물도 가용량에 포함하며 8.6에서 전환/차감을, 8.7에서 생산 반영을 연결했다. 역할 enable은 양수 요청/가용량, 계획 enable은 준비 완료이며 PowerUsageResult는 제거했다.
  - 검증: Unity 컴파일 completed/failed false/errors 없음/compilationFailed false와 ready·비컴파일·비리로드·최신 runtime/Editor DLL을 확인했다. 선택 EditMode는 전력 38/38·공통 생성 11/11·완공 8/8·철거 19/19로 이번 76/76, 실패·생략·불확정 0이다. 기존 22사례에 공급 핵심/실패 통합 흐름 16사례를 추가했다. 실제 작업 결정의 수요와 다음 틱 생성 참여·dt 상한·0/부족/충분·정격/Tier/연료 제한 분담·기초 우선·같은 틱 병합/분리·미전환 석탄 제외·예산 총량·연료/대표/수요 손상·초기화 누락 보호·비활성/실제 철거를 검증했다. 연구/충전 수요는 fixture 입력이며 실제 Producer가 아니다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.
  - 당시 완료 기준 충족: 요청 수요 기반 공통 공급 비율과 배정 한도를 유지하고 망 간 예산이 섞이지 않는다. 준비 실패·누락을 정상 0으로 게시하지 않고 기존 공급은 재사용하지 않는다. 배정량은 실제 사용량/발전량이 아니며 같은 틱에 미사용분을 재배분하지 않는다.

- [x] **Task 8.6: 석탄 입고 및 연료 소비 구현**
  - 완료(2026-10-15): CoalGenerator의 설정 슬롯/Coal Whitelist, 보관 실물+잔량의 가용량, 정수 전환·배정 에너지 차감과 EndPower 삭제를 연결했다.
  - 확정 계약: 전환으로 생긴 공간은 이번 틱 입고에 쓰며 새 입고는 다음 틱 발전부터 쓴다. 사용하지 못한 배정도 소비로 확정하고 환급하지 않는다.
  - 검증: 부족·재공급·부분 공급·전환 공간 재사용·새 입고 지연·Owner 손상·철거 반환을 관련 테스트로 확인했다. Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드와 최신 runtime/Editor DLL을 확인했다. 관련 선택 EditMode 157/157, 실패·생략·불확정 0이다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

- [x] **Task 8.7: 생산 및 건설 수명주기 통합**
  - 완료(2026-10-15): Preparation에 작업 준비 컴포넌트/수요를, BuildingDecision에 공급 이후 실행/출력 결정을 두고 Miner/Crafter Execution은 공급률/에너지 한도로 진행한다. 무전력 신규 제작은 재료를 보존하며 진행 중 상태와 완료 출력은 유지한다.
  - 확정 계약: 공급은 Power에서 소비로 확정하며 조기 완료/실행 실패의 미사용분을 재정산하지 않는다. Power 실패는 공통 Fatal의 RemainingDomains로 이번 틱 Building/Drone을 차단한다. 생성/완공·승인 철거의 기존 참여 시점은 유지한다.
  - 검증: 실제 생성→선택/망/배분→연료/생산, 무전력/부분 공급/복구·완료 출력·실물 철거·최종 입력 손상과 같은 틱 Building/Drone 차단을 확인했다. Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드와 최신 runtime/Editor DLL을 확인했다. 관련 선택 EditMode 157/157, 실패·생략·불확정 0이다. Play Mode·베이킹·Player 빌드·성능은 미수행이다. 연구/충전의 실제 Producer는 10A.3/9.14 후속이다.

## 🛸 [Phase 9] Drone System (대형 도메인 이식)

중간 마일스톤은 **9A 기반(9.1~9.3 및 9.3.1) → 9B 계획·예약·배차(9.4~9.9) → 9C 정상 운반·귀환·충전(9.10~9.14) → 9D 취소·복구·통합(9.15~9.20) → 9E 게임 시작 초기화(9.21~9.22)**이다. 9.9까지 배차 확정, 9.14까지 정상 반복 루프, 9.20까지 실패 복구와 건설 연결, 9.22까지 시작 시설과 초기 지급의 실제 구성 경로를 완료한다.

### 9A: 상태 계약과 정거장 기반

현재 V2의 현장 공급/회수 관리·실물 인계·재배정은 기존 소유자가 담당한다. Task 9.1은 [상태 전이·Owner 계약](../Specifications/DroneLifecycle.md)의 확정 완료이며 나머지 체크는 각 실행 기능의 후속 마일스톤으로 읽는다. [현재 드론 계약](../CodeMemory/Components/DroneLogistics.md)의 적재품 우선·현장 설치 순서·공개 대기 예약·행동 접수 순서·건물 종료 기준 계획과 EndSimulation 공개를 유지한다. 레거시 실물 선점/영속 목적지 예약·우선순위·즉시 이동 API를 자동 복원하지 않는다.

- [x] **Task 9.1: 상태 전이표 및 Lifecycle Owner 계약 확정**
  - 완료(2026-10-15): **상태 전이표·Owner 계약 확정** — [드론 Lifecycle 명세](../Specifications/DroneLifecycle.md)에 기존 작업/배정과 후속 수행자 상태를 구분하고 정상·중단·귀환·충전·수납, 이벤트·예약/화물 처리·Writer·공개 경계와 검증 기준을 정리했다. 실제 수행부를 추가한 완료가 아니다.
  - 확정 **1A·2A·3A·4A·5A·6A**: 완충·무배정·무동작·무적재·보관 공간 충족 시 수납, 충전 경유를 포함한 실행 가능 경로의 완충 전 배차, 현재 위치 연속 배차 우선, 같은 네트워크→다른 정거장→활성 대기, 배터리 0의 비상 속도 귀환/화물 보존, 실제 작업 중단 시 이전 예약 해제와 Cancelled/Retargeting 전환을 적용한다. 경로 평가 대기·계획된 충전 경유는 중단으로 취급하지 않는다.
  - 기존 계약 유지: 상위 작업 Open/Closed, 배정 상태·Revision, 실물 원본, 적재품 우선, 공통 적재량·예약·행동 접수 순서와 EndSimulation 공개를 재사용한다. 설명용 `Pending → Reserved → Assigned…`를 하나의 상위 작업 enum으로 합치지 않는다.
  - 선행 조건 대조: 현재 작업·배정·실물 인계·예약 정산·복구와 Item·Construction·Power 계약을 소스로 확인했다. 기존 Lifecycle/Publish의 역할을 보존하고 수행자 Lifecycle·배터리·관측의 후속 단독 Writer 책임을 정의했다.
  - 후속 연결: 정거장/Identity/수용량은 9.2~9.3.1, 이동·배터리·귀환·충전은 9.10·9.13~9.14, 실제 중단/복구·추가 작업은 9.15~9.20에서 구현한다. 설정 수치·슬롯 공유·적재량 초기화 정책은 각 후속 범위다.
  - 검증: 현재 타입·상태 쓰기·예약/인계 API·기존 테스트 정의와 전이표를 정적으로 대조했다. 허용/금지 전이와 부분 성공·중복/지연 이벤트·무전력/배터리 0·수납 자격의 기대 결과를 작성했다. 코드/에셋 변경과 새 Unity 컴파일·테스트·Play Mode·성능 검증은 없으며 후속 상태표를 실행 성공으로 표시하지 않는다.
  - 완료 기준 충족: 작업 종료·예약 정산·연결 해제·화물 보존·후속 Entity 삭제와 드론 자체 전환의 책임/순서가 명확하다. 다른 Phase 9 Task의 완료 여부는 변경하지 않는다.

- [x] **Task 9.2: 정거장 등록·범위·네트워크 구현**
  - 현재 진행(2026-10-22): **구현·검수·관련 자동 검증 완료** — 공통 생성이 주 시설·정거장 역할과 결정/공개 연결을 부착한다. [정거장 명세](../Specifications/DroneStationNetwork.md)에 단일 Range·정사각형 활동 영역·망 등록/병합/분리/해제·조회 수명과 공개 경계를 정리했다.
  - 확정 1A·2A·3A·4A·5A: BuildingConfig의 종류·티어별 단일 `droneStationRangeInChunks`, 무전력 참가, 한 셀 이상 공유 시 연결, PlacementStamp 대표 Entity 식별, EndBuilding 이후 이번 틱 연결 판단/EndSimulation 공개를 적용한다. 별도 영속 망 ID와 전력망 연결 규칙은 공유하지 않는다.
  - 활동 영역: 회전 footprint의 긴 변 기준 정사각형을 만들고 단일 Range×ChunkSize로 확장한다. 홀수 여백의 남는 1셀은 양의 축에 둔다. 음수 Range는 0이며 포함 경계와 long 합산을 사용한다. 기존 footprint는 바꾸지 않는다. 이전 직사각형 footprint 그대로의 확장 규칙은 이번 확정으로 대체했다.
  - 소유자: DroneStationNetworkDecisionSystem은 같은 틱 연결안, DroneStationNetworkApplySystem은 EndSimulation 공개, DroneStationSpatialSyncSystem은 최종 연결/건물 원본 기반 범위 인덱스와 Fence 수명을 소유한다. 망의 활동 영역은 소속 영역의 합집합이며 외곽 사각형의 빈 공간은 제외한다.
  - 선행 조건 충족: Task 9.1 계약과 공통 건물 생성/공간·설정·전력 구성을 재사용했다. 전력 공급/충전/수용량은 별도 조건이며 정거장 참가에 섞지 않는다.
  - 후속 연결: 실제 작업 후보의 범위 필터는 Task 9.5, 수행자/수용량은 9.3·9.3.1, 귀환·충전은 9.13~9.14, 정거장 철거 중 드론 재배치는 9.19에서 연결한다.
  - 검증: DroneStationNetworkTests 14/14를 포함한 관련 선택 EditMode 99/99(실패·생략·불확정 0), Unity 컴파일 오류 없음·Editor ready/비컴파일/비리로드·관련 DLL 최신성을 확인했다. DroneStationNetworkTests에 EndBuilding 새 연결·공개 지연·무전력·정거장 제거/비활성/철거 승인·회전 정사각형·경계·합집합·종류/티어·음수/큰 정수·맵 용량/비우기와 설정 미게시 검증을 추가했다. 기존 건설 통합 fixture의 옛 그룹/준비·공급 누락(P2)을 보완하고 실패 3건의 재실행 5/5를 확인했다. 격리 공급을 실제 Power 통합 근거로 확대하지 않는다.
  - 완료 기준 충족: 네트워크와 범위 인덱스의 일치·등록/병합/분리/해제를 확인했고 다른 도메인 상태를 소유하지 않는다. 7역할 독립 검수와 P2 보완 경계 재검수를 마쳤다. 전체 EditMode·Play Mode·베이킹·Player 빌드·성능은 미수행이다.

- [x] **Task 9.3.1: 드론 Authoring·프리팹 DB 및 활성 엔티티 생성 연결**
  - 현재 진행(2026-10-22): **구현/관련 검증/독립 검수 완료** — DronePrefabDatabaseAuthoring과 V2 SubScene 참조, 4종 DB 준비 검증, DroneSpawnUtility의 EndSimulation 생성/기존 루트 초기 구성을 연결했다. [생성 명세](../Specifications/DroneCreation.md)를 따른다.
  - 원형/초기 상태: 원형에는 런타임 역할을 베이킹하지 않는다. 생성은 입력 위치·Revision 1/CanAcceptTask=false·빈 배정/화물과 최초 완충 배터리를 기록한다. 최대 배터리는 호출자의 필수 입력이며 기존 개체 값은 역할 초기화로 충전하지 않는다. 회전·scale·PostTransformMatrix를 보존한다.
  - 유지할 경계: API는 Ready 이후 사용하고 공개는 호출자의 EndSimulation ECB다. 같은 Entity에 미재생 초기화를 중복 기록하지 않는 것은 호출자의 중재 계약이다. 실제 출고/수납·아이템 역할/Owner/버퍼와 표시 전환·수용량·공통 적재량/설정 로딩은 Task 9.3에 남긴다.
  - 검증: Unity 컴파일 오류 없음, 관련 선택 EditMode 56/56(실패·생략·불확정 0). 실제 저장된 V2 SubScene Baker의 드론 DB1·Ready1/Fatal0, 렌더 원형과 생성 드론의 scale/위치/배터리100/배차 불가 상태를 격리 World에서 확인했다. 제품 설정 최대치 로더·실제 비행/화면 표시·Play Mode·Player 빌드·성능은 검증하지 않았다.
  - 완료 근거: 유효 원형 생성과 준비 실패/중복 초기화의 원본 보존을 확인했다. 초기화/생성 반영/컴포넌트/guard/경계 독립 검수 5개 역할에서 확인한 범위의 지적 없음.

- [x] **Task 9.3: 드론 Identity 전환 및 정거장 수용량 구현**
  - 현재 진행(2026-10-22): **구현/검증/독립 검수 완료** — 같은 Entity의 아이템/수행자 역할 전환과 요청 기반 Activate/Dock/Store, 정박 양방향 관계, 전용/일반 슬롯과 벨트 정책을 연결했다. [Identity/보관 명세](../Specifications/DroneIdentityAndStationStorage.md)를 따른다.
  - 초기화: [6A 초기화](../Specifications/DroneCapacityInitializationPlan.md)가 carryingCapacity=3과 maximumBattery=100을 한 번 게시한다. 유효한 기존 singleton은 보존하고 실패/중복/무효는 원본 보존/공통 Fatal/미게시/자동 재시도 없음이다.
  - 확정 정책 구현: BuildingConfig 종류/티어별 전용 5칸과 일반 20/50칸을 분리한다. 드론 아이템은 MaxStack 50, 활성 정박은 한 슬롯 전체를 점유하고 초과 귀환 보관은 일반 구간을 공유한다. 일반 벨트 입출고/FIFO를 유지하며 드론 벨트 입고는 전용 여유만 허용하고 드론 출고는 금지한다. 새 개체는 최초 완충, 기존 배터리는 전환으로 충전하지 않는다.
  - 경계: 병렬 Burst Decision Job은 자격, 단일 Burst Reservation Job은 작업 예약 선택 뒤 Sequence 순서/슬롯 중재, 단일 Burst Apply 데이터 Job은 작업 Publish 기록 뒤 현재 원본을 확인하고 역할/Owner/버퍼/비관리형 표시/결과를 ECB에 기록한다. 데이터 Job 완료 뒤 메인 스레드에서 관리형 RenderMeshArray만 별도 ECB에 기록하며 기존 EndSimulation에 함께 공개한다. 단순 입고는 자동 활성화하지 않는다. 수납은 완충/무배정/Idle/빈 화물/공간을 모두 요구한다.
  - 선행 조건: Task 9.1~9.2·9.3.1 및 Item 소유권·건물 생성 계약.
  - 구현 범위·상태 소유자: 기존 규칙에 필요한 드론 아이템과 활성 드론의 전환, 정거장 보관 상태·수용량 계산을 담당 경계에서 구현한다.
  - 후속 연결: 실제 귀환 선택/이동 도착 Producer는 Task 9.13, 충전은 Task 9.14다. 활성 정박 드론이 있는 정거장은 현재 철거를 거부하고 전체 재배치는 9.19에 남긴다.
  - 검증: 초기 구현의 Unity 컴파일/선택 EditMode 340/340과 독립 6개 역할/영향 재검수는 별도 기록이다. 후속 Job 분리는 Unity 컴파일 오류 없음, Burst 활성/동기 컴파일의 관련 선택 EditMode 119/119(실패/생략/불확정 0), 실제 저장 SubScene의 제품 설정/공통 생성/같은 Entity 왕복/비관리형 렌더 데이터 probe를 확인했다. 격리 Baker에 없는 RenderMeshArray 분기는 명시적 shared component를 사용하는 EditMode로 확인했다. Job 변경의 독립 5개 역할 검수는 지적 없이 완료했다. 게시 중 예외 회수는 정적 확인이며 Play Mode/실제 비행/충전/Player/성능은 미수행이다. 완료 체크와 Task 집계는 유지한다.
  - 완료 기준: 중복 Identity·고아 소유권 없이 전환되고 실제 보관 상태와 수용량이 일치한다.

### 9B: 작업 계획·예약·배차

- [ ] **Task 9.4: 작업 생성·변경·취소 요청 경로 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [Decision](../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs)이 품목별 현장 공급·실물별 현장 방해물/드론 방출 회수 의도를 만들고 [Execution](../../Assets/Scripts/Systems/Drones/Execution/DroneTaskExecutionSystem.cs)이 중복 검사·생성 순번과 EndSimulation 기록을 소유한다.
  - 남은 작업: 수동 우선순위·중단/재개·일반 반입/반출·드론 철거 명령은 미구현이다. 현장 취소/소멸의 자동 무효화와 사용자 명령을 구분한다.
  - 레거시 검토: 공사 공급·철거·건물 반입/반출·월드 회수와 우선순위 변경·수동 중단/재개를 지원했다. 자동 보류와 사용자 중단을 구분하고 자동 사유가 해소되어도 사용자 중단을 임의 해제하지 않았다. 현재 공급/회수 관리에 미이식 명령을 지원한다고 표시하지 않으며 필요한 확장만 기존 소유자 아래 검토한다.
  - 선행 조건: Task 9.1~9.3.
  - 구현 범위·상태 소유자: 작업 명령을 권위 있는 작업 데이터로 전환하고 기존 우선순위·중단·재개·취소 명령의 검증과 소비 경로를 마련한다.
  - 후속 연결: 대상별 계획은 Task 9.5, 활성 작업의 취소 부수 효과는 Task 9.15에서 연결한다.
  - 검증: 무효 대상·중복 명령·종료 작업에 대한 변경과 요청 잔류를 검사한다.
  - 완료 기준: 명령이 유효한 작업 또는 명시적 거부 결과로 소비되고 미지원 경로를 완료로 처리하지 않는다.

- [ ] **Task 9.5: 공급원·목적지 탐색 및 작업 계획 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [작업 Decision](../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs)과 [경로 계약](../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs)이 보관 공급원/목적지 선택, Direct 경로 요청·결과 대기·revision 검사와 적재품 재배정 후보를 처리한다.
  - 남은 작업: 실제 경로 평가 Producer, Task 9.2의 이번 틱 연결안/공개 망을 사용하는 작업 범위 필터, 일반 반입/반출의 계획은 미구현이다. 지금은 외부 경로 결과가 있어야 실행 가능한 후보를 고른다.
  - 레거시 검토: 반입/공사는 같은 네트워크의 저장품·생산품에서 공급원을 찾았다. 반출은 첫 예약 전에 전체 남은 수량의 수용 공간을 합산하고 가까운 보관처부터 선택했다. 재고/공간/범위 부족은 자동 보류하되 이미 예약한 운송은 유지했고 직접 작업 목적지는 계획으로 전달했다. 이는 추가 작업 유형의 확인 자료이며 현재 공급·적재품 Direct 재배정 계약을 덮어쓰지 않는다. 현재 공급원은 Storage/DroneStation/MainFacility의 보관품이며 Miner/Crafter Product 버퍼는 공급원이 아니다.
  - 선행 조건: Task 9.2·9.4와 저장·공사 수령 계약.
  - 구현 범위·상태 소유자: 계획 단계가 네트워크·품목·대상 유효성을 읽어 공급원·목적지 후보를 게시한다. 실제 아이템·용량은 선점하지 않는다.
  - 후속 연결: 선점은 Task 9.6, 후보 우선순위와 배차는 Task 9.8~9.9에서 처리한다.
  - 검증: 공급 부족, 목적지 만석, 범위 변경, 대상 소멸 시 계획 갱신을 검사한다.
  - 완료 기준: 유효한 계획만 게시되고 계획 작성만으로 소유권·예약 상태가 변경되지 않는다.

- [ ] **Task 9.6: 현재 예약 경계 검토 및 추가 운송 계약 연결**
  - 현재 진행(2026-10-12): **부분 구현** — [Reservation](../../Assets/Scripts/Systems/Drones/Reservation/ConstructionSupplyReservationSystem.cs)이 현장 잔여량을 예약하고 선택 후보·Quantity·CommittedQuantity를 공개 대기 기록에 보관한다. 공급원 실물/보관 공간의 영속 예약 없이 배정/행동 경합을 중재한다.
  - 남은 작업: 추가 작업 유형의 예약 필요성과 수행부 연결은 남아 있다. 기존 공급 예약을 다시 구현하거나 레거시 실물 선점을 복원하지 않는다.
  - 선행 조건: Task 9.5 및 목적지 수령·용량 계약.
  - 구현 범위·상태 소유자: 현재 현장 공급 예약과 미공개 CommittedQuantity 정산을 재사용한다. 공급원 재고·보관 목적지 공간의 영속 예약은 현재 제공하지 않으며 행동 계획과 실제 성공분으로 처리한다. 과거 실물 marker·영속 목적지 예약을 추가 유형에 도입할 필요가 있으면 별도 결정을 기록하고 기존 수량·소유권·공개 계약과 대조한다.
  - 후속 연결: 게시 이후의 해제·완료·무효화는 Task 9.7에서 구현한다.
  - 검증: 현재 현장 예약과 미공개 정산, 같은 행동 계획의 재고·공간 경합, 실제 성공분만 용량 소비, 필터·대상 변화와 부분 수량을 검사한다. 추가 영속 예약을 승인한 경우에만 실물·마지막 예약 용량의 획득/실패 롤백을 검증한다.
  - 완료 기준: 반쪽 예약·과다 예약·중복 선점이 없고 실패한 획득이 원상복구된다.

- [ ] **Task 9.7: 예약 완료·해제·무효화 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [예약 정산](../../Assets/Scripts/Systems/Drones/Reservation/ConstructionSupplyReservationSystem.cs)이 무효/미공개 예약을 해제하고 [Publish](../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs)가 최종 거부·수량 축소를 롤백한다. Lifecycle은 실제 수집/납품 성공량으로 예약을 정산한다.
  - 남은 작업: 추가 작업 유형·실제 수행자의 취소/실패 원인을 연결해야 한다. 현재 예약 정산과 레거시 marker 해제를 혼동하지 않는다.
  - 선행 조건: Task 9.6 및 Task 9.1의 전이 계약.
  - 구현 범위·상태 소유자: 현재 예약 소유자가 활성 현장 예약·공개 대기 기록·실제 성공량과 종료/무효화의 해제량을 함께 정산한다. 레거시 실물 marker·목적지 영속 예약을 존재한다고 전제하지 않는다. 추가 예약 정책을 승인한 경우에만 해당 획득/해제의 원자적 경계를 확장한다.
  - 후속 연결: 전달 결과는 Task 9.12, 실패·취소 결과는 Task 9.15~9.16에서 연결한다.
  - 검증: 중복 해제·완료, 소멸한 아이템·소유자·목적지, 이미 부분 완료한 작업을 검사한다.
  - 완료 기준: 한 예약의 효과가 한 번만 정리되고 음수 수량·잔류 선점이 없다.

- [ ] **Task 9.8: 작업 후보 구성 및 우선순위 선택 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [Decision 후보](../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs)와 [Reservation 선택](../../Assets/Scripts/Systems/Drones/Reservation/ConstructionSupplyReservationSystem.cs)이 적재품 우선, 공급 PlacementStamp→CreationSequence, 회수 CreationSequence 및 두 선두 비교를 수행한다.
  - 남은 작업: 추가 유형의 우선순위, 배터리 자격 Producer와 네트워크는 미구현이다. 레거시 Emergency/거리 우선은 현재 관리 기능에 적용되어 있지 않다.
  - 레거시 검토: Emergency → normal 1~10(작은 수가 우선, 입력 기본 5), 같은 우선순위는 거리 → 생성 순서였고 배터리로 불가능한 후보는 건너뛰었다. 현재 현장 공급의 PlacementStamp·CreationSequence와 적재품 우선 계약과 다르므로 현재 배정에 바로 넣지 않는다. 추가 유형에서 필요성을 확인하고 과거 network/priority/chunk/cell 검색 캐시를 자동 복제하지 않는다.
  - 선행 조건: Task 9.5~9.7.
  - 구현 범위·상태 소유자: 현재 Decision 후보·Reservation·Publish를 재사용하고 확정된 현장 설치 순서·작업 생성 순서·적재품 우선 계약으로 선택한다. 추가 작업 유형의 우선순위·거리·배터리 규칙은 레거시 검토를 거쳐 필요한 경우 별도로 결정한다. 후보 인덱스는 원본 상태를 소유하지 않는다.
  - 후속 연결: 권위 있는 claim과 할당은 Task 9.9에서 수행한다.
  - 검증: 동률, 처리 불가능한 상위 후보, 취소·소멸된 후보, 후보 갱신 순서를 검사한다.
  - 완료 기준: 소비자가 별도 준비 API를 호출하지 않아도 최신 유효 후보를 선택할 수 있다.

- [ ] **Task 9.9: 배차 확정 및 작업·드론 할당 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [배정 Publish](../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs)가 후보를 최종 재검사하고 배정·개별 예약·수행자 연결을 같은 EndSimulation ECB에 기록한다. 공개 직전 무효화/감량과 재배정 revision을 처리한다.
  - 남은 작업: 실제 수행자 생성/등록·정거장 Identity 전환·배터리/경로 자격의 제품 입력 연결이 남아 있다. 테스트의 직접 생성 수행자를 제품 구현으로 보지 않는다.
  - 레거시 검토: 완충 여부만이 아니라 수집→납품→귀환 전체 경로의 필요 에너지로 배차를 판단했다. 성공 후보의 중복 획득을 막고 틱당 배차 수를 임의 제한하지 않았다. 현재 V2는 관측의 CanAcceptTask 등 공개 자격을 읽으므로 배터리·경로 자격 Producer는 실제 수행부에서 정하고 Publish 경계를 재사용한다.
  - 선행 조건: Task 9.3·9.7~9.8.
  - 구현 범위·상태 소유자: 배차 후보를 권위 있게 claim하고 할당 상태를 전이 계약에 따라 반영한다. 후보 선택과 실제 획득 성공을 구분한다.
  - 후속 연결: 이동은 Task 9.10, 종료 후 재배차·취소 연결은 Task 9.12~9.15에서 완성한다.
  - 검증: 여러 드론의 동일 후보 경쟁, claim 직전 대상 무효화, 귀환 에너지 부족을 검사한다.
  - 완료 기준: 하나의 예약이 중복 배차되지 않고 실패한 claim이 할당·예약을 남기지 않는다.

### 9C: 정상 운반·귀환·충전

- [ ] **Task 9.10: 이동·도착 판정 및 배터리 소비 구현**
  - 현재 진행(2026-10-12): **계약 기반만 준비** — [관측 계약](../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs)과 [경로 계약](../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs)의 Position/Revision·경로 결과를 관리 계층이 소비한다.
  - 남은 작업: 9.3의 DroneBatteryConfig/DroneBatteryState와 DroneActivityState를 재사용해 실제 이동/거리 소비/위치 원본의 관측 갱신/경로 계산/도착 신호 Producer를 연결한다. 현재 배터리 최초량과 활동 초기화는 실제 이동 소비 구현이 아니다.
  - 확정 계약 연결: Task 9.1의 2A·5A·6A에 따라 충전 경유 경로·다음 구간 에너지, 배터리 0의 귀환 전용 비상 이동, 실제 중단과 평가 대기를 구분한다. 비상 귀환/대기 중 신규·적재 재배정과 에너지 고갈에 따른 자동 화물 폐기를 막는 수행자 상태 연결은 후속 구현이다. 배터리 원본은 단일 정산 소유자가 이동 소비·충전을 반영하고 관측/배정 상태를 이동 코드가 중복 소유하지 않는다.
  - 레거시 검토: 직선 이동 거리로 배터리를 소비했다. 실제 이동·관측·경로 평가·행동 신호는 현재 V2의 후속 Producer이며 과거 상태 머신 복사로 완료하지 않는다. 관측 위치/Revision과 실제 위치 원본을 연결하고 현재 Direct 경로·행동 신호의 책임 경계를 유지한다.
  - 선행 조건: Task 9.9.
  - 구현 범위·상태 소유자: 이동 단계가 확정된 경로의 실제 위치를 갱신하고 실제 거리/소비 근거를 단일 배터리 정산 소유자에 전달한다. 관측 게시 소유자가 확정 위치·배터리·Lifecycle로 관측을 갱신하며 도착·이동 실패 입력을 기존 접수 경계에 연결한다. 이동 코드가 작업 완료나 예약 정리를 직접 처리하지 않는다.
  - 후속 연결: 픽업·전달은 Task 9.11~9.12, 귀환은 Task 9.13, 복구는 Task 9.15~9.16에서 연결한다.
  - 검증: 정상 도착, 큰 DeltaTime, 중복 도착 이벤트, 목표 무효화·배터리 부족을 검사한다.
  - 완료 기준: 이동량·에너지·도착 결과가 일치하고 실패를 정상 완료로 처리하지 않는다.

- [ ] **Task 9.11: 픽업 및 화물 소유권 이전 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [행동 접수](../../Assets/Scripts/Common/DroneActionRequestUtility.cs)→Transfer Decision/Execution의 건물 종료 기준 실물 계획→[Lifecycle 실물 인계](../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs)가 저장품 수집·월드 회수, 원본/순번 재검사와 실제 성공량 정산을 처리한다.
  - 남은 작업: 실제 이동/도착에서 행동 신호를 제출할 Producer와 추가 작업 유형의 픽업 연결이 남아 있다.
  - 선행 조건: Task 9.6~9.7·9.10 및 Item 소유권 계약.
  - 구현 범위·상태 소유자: 실제 수행부의 행동 신호를 기존 입력 경계에 제출하고, 건물 종료 상태의 실물 계획과 최종 재검사로 픽업한다. 현재 Drone Lifecycle과 공통 Ownership API가 실제 성공량으로 소유권·수량·예약을 정산하며 과거 실물 선점 marker나 별도 즉시 이동 시스템을 복원하지 않는다.
  - 후속 연결: 전달은 Task 9.12, 실패 시 화물·예약 처리는 Task 9.15~9.16에서 연결한다.
  - 검증: 품목·예약 불일치, 출발지 제거, 중복 픽업, 이전 실패를 검사한다.
  - 완료 기준: 실제 이전 성공 전에는 전달 단계로 진행하지 않고 아이템이 한 소유자에게만 속한다.

- [ ] **Task 9.12: 전달 및 정상 작업 완료 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [Lifecycle 행동 반영](../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs)이 현장 공급·보관 납품·방출을 공통 Ownership API로 수행하고 DeliveredQuantity·예약·배정·결과를 실제 성공량으로 정산한다. 결과는 EndSimulation 뒤 외부에서 소비한다.
  - 남은 작업: 제품 수행부의 도착/납품 신호와 일반 운반 종류를 연결해야 한다. 마지막 납품/방해물 회수의 완공 반영은 다음 건물 틱이며 즉시 완공으로 바꾸지 않는다.
  - 선행 조건: Task 9.7·9.11 및 목적지 수령 계약.
  - 구현 범위·상태 소유자: 현재 Drone Lifecycle이 공통 Ownership API로 납품한 실제 성공량을 도착량·예약·배정·행동 결과에 정산한다. 행동 요청/계획 소비와 외부 결과 공개는 EndSimulation 계약을 유지하며 새 Transfer 요청이나 병렬 결과 소유자를 만들지 않는다. 실제 수행부와 추가 작업 유형의 연결만 후속으로 검증한다.
  - 후속 연결: 재배차·귀환은 Task 9.13, 전달 실패 복구는 Task 9.15~9.16, 공사 완주는 Task 9.20에서 연결한다.
  - 검증: 정상·부분 수량 완료, 중복 결과, 목적지 만석·소멸·수령 거부를 검사한다.
  - 완료 기준: 수령 성공한 수량만 완료되고 화물·작업량·예약량이 일치한다.

- [ ] **Task 9.13: 귀환 목적지 선택 및 정거장 보관 구현**
  - 현재 진행(2026-10-22): **귀환 선택/실제 도착 Producer 미구현** — Task 9.3의 Activate/Dock/Store와 수용량/수납 자격 API를 재사용한다. 기존 StoreCargo는 운반 아이템 보관이며 수행자 수납과 구분한다.
  - 남은 작업: Task 9.1에서 확정한 귀환·연속 배차·만석 재선택·수납 자격과 충전 접속의 전이를 실제 수행자/정거장에 연결해야 한다.
  - 확정 규칙: 3A·4A에 따라 현재 위치에서 실행 가능한 다음 작업을 우선하고, 없거나 충전이 필요하면 같은 네트워크의 수용 가능한 가까운 정거장→다른 정거장→활성 대기로 연결한다. 드론 귀환을 화물의 현장 외부 안전 방출과 혼동하지 않는다.
  - 선행 조건: Task 9.1/9.2/9.3의 계약/정거장/역할 전환 기반과 미완료 Task 9.9~9.12를 연결한다. 충전/배터리 반영은 Task 9.14와 같은 계약이다.
  - 구현 범위·상태 소유자: 후속 귀환 판단/이동 도착 Producer가 Task 9.3의 정거장 Lifecycle 요청을 제출한다. 기존 정거장 소유자가 정박/수납과 같은 Entity/Owner 관계를 EndSimulation에 반영한다. 작업/배정·예약은 기존 관리 소유자가 반영한다.
  - 수납 자격 확정: 1A에 따라 완충·활성 배정 없음·진행 중 동작 없음·적재품 없음·보관 공간 있음의 모든 조건을 요구한다. 충전 중/충전 대기 드론은 활성 상태를 유지한다. 조건 확정은 끝났지만 실제 연결/검증은 이 Task와 9.14에 남아 있다.
  - 후속 연결: 충전은 Task 9.14, 정거장 파괴 중 재배치는 Task 9.19에서 연결한다.
  - 검증: 다음 작업 존재, 정거장 만석·소멸, 대체 정거장, 수용 가능한 곳이 없는 경우를 검사한다.
  - 완료 기준: 중복 보관·용량 초과 없이 재배차·귀환·대기 상태가 기존 규칙과 일치한다.

- [ ] **Task 9.14: 전력 기반 충전 및 재배차 상태 구현**
  - 현재 진행(2026-10-22): **충전 기능 미구현 / 공통 전력 기반 준비** — Task 8.5~8.7의 수요·배분·공급·확정 소비는 구현됐다. DroneStation/MainFacility는 소비 역할과 공급 결과를 갖지만 충전 전용 수요는 현재 0이다. 배터리 최초량/최대 설정은 9.3에 있고 지속 소비/충전 Writer는 후속이다.
  - 남은 작업: 배터리 원본과 충전·배차 자격·수납 정책을 연결하고, 충전 후보/수요 근거를 Preparation에서 준비해 기존 전력 수요 Writer에 연결해야 한다. Power가 확정한 공급으로 Drone 단계에서 배터리를 갱신하는 처리가 남아 있다.
  - 레거시 검토: 충전이 필요한 드론에서 정거장 전력 수요를 계산하고 실제 공급률로 충전했다. 충전 중에도 전체 작업/귀환 에너지가 충분하면 배차하고 부족하면 충전을 계속했다. 수요·공급·충전·보관·표시의 원본을 구분하고 충전 중 수납의 상충 규칙은 Task 9.1에서 확인한다.
  - 선행 조건: 완료된 Task 9.1의 충전/보관·배차 계약과 미완료 Task 9.10의 이동 소비 및 Task 9.13의 귀환/보관 연결. 공통 전력 선행 Task 8.5~8.7은 완료돼 있으므로 재구현하지 않는다.
  - 구현 범위·상태 소유자: Preparation에서 충전 자격/수요 근거를 준비하고 기존 PowerDemand 작성 경로를 재사용한다. Power는 배분·확정 소비를 소유한다. 충전 계획은 배터리 단독 정산 소유자에 전달해 공개 공급률·AllocatedEnergy와 배터리 상한 내에서 반영하고 수행자 Lifecycle에 충전·보관·배차 자격을 연결한다. 1A·2A에 따라 충전 접속과 수납을 구분하고 완충 전 실행 가능 배차를 허용한다. 드론 처리 중 새 수요를 소급 배정하거나 미사용 배정량을 환급·재정산하지 않는다. 충전 수치·분담의 구체화는 이 Task에 남아 있다.
  - 레거시 피드백에서 이관한 확인 항목: 배터리가 최대치에 도달한 뒤 충전 대기가 계속되는 사례와 충전이 필요한 드론의 조기 수납 사례를 함께 확인한다. 완충 시 충전 대기를 종료하고 보관 자격을 만족하면 보관으로 전환한다는 요구를 Task 9.13의 조건과 대조한다. 미완충 드론의 충전·대기 흐름을 유지하고 작업·이동 중인 드론을 완충만으로 보관하지 않도록 기존 의도를 확인한다. 이 요구의 이관은 V2 정책 확정이나 구현·검증 완료를 뜻하지 않는다.
  - 후속 연결: 충전과 보관의 시각 표현은 Task 10B.4에서 연결한다.
  - 검증: 무전력·부분 공급·완충, 충전 중 배차, 공급 복구를 검사한다.
  - 완료 기준: 배터리와 배정 에너지 한도를 지키고 정상 운반→귀환→충전→재배차 루프가 완주한다.

### 9D: 취소·복구·직접 작업 및 통합

- [ ] **Task 9.15: 취소·실패 전이 및 할당·예약 정리 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [Lifecycle 무효화](../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs)가 전달된 의도와 최신 revision을 재검사해 종료/연결을 정리한다. 적재품이 있으면 Retargeting과 연결을 유지하며 결과 소비·예약 정산 전에 배정을 지우지 않는다.
  - 남은 작업: 수동 작업 취소 명령, 실제 배터리·수행 실패 원인, 정거장·추가 유형의 종료 전이를 연결해야 한다.
  - 확정 계약 연결: Task 9.1의 6A에 따라 실제 작업 중단에서 이전 예약을 해제하고 빈 수행자는 Cancelled/연결 해제, 적재품은 Retargeting/실물·연결 보존으로 정산한다. 계획된 충전 경유·경로 평가 대기는 중단으로 처리하지 않는다. 현재 무효화 재검사에 새 실패 사유를 연결하는 코드는 아직 없다.
  - 레거시 검토: 목표 소멸·배터리 부족·전달 실패는 화물 보존과 예약/할당 정리를 함께 필요로 했다. 진단 로그는 명시적 Editor/Development 옵션이며 상태를 바꾸지 않았다. 현재 Lifecycle의 재검사·실제 성공량 정산을 재사용하고 과거 진단 시스템이나 로그용 원본 상태를 새로 만들지 않는다.
  - 선행 조건: Task 9.1·9.7 및 Task 9.9~9.14의 결과 경로.
  - 구현 범위·상태 소유자: Lifecycle Owner가 취소·실패 결과를 소비하고 단계별로 할당·예약·후속 행동을 전이한다. 화물을 가진 실패는 복구 요청으로 연결한다.
  - 후속 연결: 화물의 실제 복구는 Task 9.16, 공사 취소 연결은 Task 9.20에서 완료한다.
  - 검증: 배차 전·이동 중·픽업 후·전달 직전 취소, 완료와 취소의 경합, 중복·지연 이벤트를 검사한다.
  - 완료 기준: 상태표에 따라 정리가 한 번만 적용되며 화물 복구가 필요한 상태를 종료로 누락하지 않는다.

- [ ] **Task 9.16: 운반 화물 회수·복원 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [적재품 Decision](../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs)과 [재배정 Publish](../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs)가 같은 배정/실물·최초 작업 순서를 보존해 Direct 재배정한다. 안전 방출은 활성 현장 외부와 선택 목표/요청/관측 일치를 재검사한다.
  - 남은 작업: 실제 이동·관측·목적지/외부 셀 평가 Producer가 남아 있다. 미제공 경로 결과나 외부 방출 셀 검색의 None/Unreachable에서는 적재품을 보존한다. 목적지 도달 불가 확정 뒤 현장 외부 방출 목표를 선택하는 관리는 구현되어 있지만 자동 비행은 아니다.
  - 레거시 검토: 현재 적재품은 Direct 재배정·현장 외부 안전 방출 계약을 우선한다. 경로/외부 셀 결과 대기·Unreachable에서는 실물을 보존하고 활성 현장 안에 떨어뜨리지 않는다. 과거 주변 저장소/월드 복원·자동 회수 요청은 추가 유형의 검토 자료로만 남기며 새 자동 회수 정책이나 별도 Ownership Writer를 승인하지 않는다.
  - 선행 조건: Task 9.11·9.13·9.15 및 Item 저장·월드 복원 계약.
  - 구현 범위·상태 소유자: 현재 적재품 재배정·현장 외부 방출 목표·행동 결과 정산을 기존 Drone Lifecycle과 Ownership API에 연결한다. 추가 작업 유형에서도 실물 보존·원본 소유권과 확정된 방출 자격을 유지한다.
  - 후속 연결: 실제 수행부·경로/외부 셀 평가를 연결하고, 방출 실물의 자동 회수가 필요하면 Task 9.18에서 별도 정책 확인 후 연결한다.
  - 검증: 목적지 소멸, 대체 저장소 만석, 복원 실패·재시도, 반복 복구 결과를 검사한다.
  - 완료 기준: 화물 유실·복제·고아 소유권 없이 복구되고 실패 시 재시도 가능한 상태가 명확하다.

- [ ] **Task 9.17: 드론 철거 작업 및 건설 코어 연결**
  - 현재 진행(2026-10-12): **대상 기능 미구현** — Task 7.7의 철거 승인·중단·환급 코어는 있지만 드론 철거 작업 종류·이동·철거 행동 결과 Producer는 없다.
  - 남은 작업: 드론 실제 도착/행동 결과를 현재 철거 코어에 연결해야 한다.
  - 레거시 검토: 사용자 철거는 실제 드론 도착·행동 결과 뒤 건물 제거를 요청하고 보관 실물·건설 비용 100%를 반환한 뒤 회수 작업을 만들었다. 현재 환급 계약을 재사용하며 사용자 운송/회수 연결만 후속으로 확인한다. Command의 철거 승인·동작 중단과 EndBuilding 제거를 과거 순서로 바꾸지 않는다.
  - 선행 조건: Task 7.7, Task 9.4~9.10·9.15.
  - 구현 범위·상태 소유자: 직접 철거 작업을 계획·배차·실행하고 결과를 건물 제거 소유자에 전달한다. 건물 제거 성공·보류·실패를 작업 결과와 연결한다.
  - 후속 연결: 정거장 특수 처리는 Task 9.19, 입력은 Task 10B.2에서 연결한다.
  - 검증: 유효 대상 도착, 대상 소멸, 중복 철거, 취소, 건물 제거 보류를 검사한다.
  - 완료 기준: 유효한 드론 실행 결과로만 사용자 철거가 진행되고 작업과 건물 결과가 일치한다.

- [ ] **Task 9.18: 월드 아이템 회수 작업 구현**
  - 현재 진행(2026-10-12): **부분 구현** — [회수 작업 생성](../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs)이 DroneRecoveryPending이 있는 방출품과 비취소 현장 내부의 월드 방해물만 대상으로 작업을 만든다. 배정·회수·보관 납품과 중복 방지도 관리 코드에 있다.
  - 남은 작업: 일반 바닥 전체 회수·사용자 범위 회수 명령·정거장 범위·실제 수행부는 미구현이다. 보통 월드/벨트 실물을 자동으로 전부 회수하지 않는다.
  - 레거시 검토: 범위 밖 회수는 자동 보류하고 범위가 생기면 재평가했다. 저장 소유권 전환·대상 소멸 때 오래된 회수·표시를 정리했다. 취소/철거 반환과 실패 화물의 자동 회수 범위는 따로 확인하며 현재 비활성 벨트 실물의 자동 반환/회수나 Disabled 기반 소유권을 새로 추가하지 않는다.
  - 선행 조건: Task 9.5~9.16 및 월드 아이템 공간 조회 계약.
  - 구현 범위·상태 소유자: 월드 아이템의 회수 계획·선점·픽업·전달을 기존 예약·운반 경로와 연결한다. 공간 인덱스는 조회용으로 사용한다.
  - 후속 연결: 사용자 회수 요청과 표시 연결은 Task 10B.2·10B.4에서 완료한다.
  - 검증: 동일 아이템 경쟁, 아이템 이동·저장 전환·소멸, 범위 밖 대상, 목적지 용량 부족을 검사한다.
  - 완료 기준: 유효한 월드 아이템만 한 번 회수되고 소유권 변화 후 오래된 작업·예약이 남지 않는다.

- [ ] **Task 9.19: 정거장 철거 및 보관 드론 재배치 구현**
  - 현재 진행(2026-10-22): **철거 전 재배치 미구현** — Task 9.3의 정박 관계/수용량과 기존 건물 철거를 재사용한다. 현재 활성 정박 드론이 있으면 Command가 철거를 거부한다. 목적지 선택/전체 수용량 선검사와 실제 재배치는 후속이다.
  - 남은 작업: 전체 보관 드론의 수용 공간 선검사·귀환·철거 보류를 정거장 도메인에 연결해야 한다.
  - 선행 조건: 완료된 Task 7.7/9.2/9.3과 미완료 Task 9.13~9.17을 연결한다.
  - 구현 범위·상태 소유자: 건물 제거·정거장 수용량·드론 lifecycle 경계를 통해 기존 보관 드론 재배치와 철거 보류 규칙을 구현한다.
  - 유지할 기존 규칙: 철거 전에 모든 보관 드론의 목적지와 누적 수용량을 확인한다. 같은 네트워크의 가까운 정거장을 우선하고 공간이 없으면 다른 네트워크의 정거장도 탐색한다. 모두 수용할 수 있을 때만 드론 Identity를 유지해 대체 정거장으로 귀환시키며 월드 아이템 변환이나 자동 회수 작업을 만들지 않는다. 일부 드론만 옮길 수 있으면 기존 보관 상태를 유지하고 전체 철거를 보류한다. 기존 건물·정거장·드론 소유자의 계약으로 연결하며 이전 구조의 해결 기록을 V2 완료로 표시하지 않는다.
  - 후속 연결: 표시·사용자 피드백은 Task 10B.3~10B.4에서 연결한다.
  - 검증: 충분한 대체 수용량, 일부만 수용 가능, 대체 정거장 소멸, 충전 중 철거, 반복 요청을 검사한다.
  - 완료 기준: 재배치할 수 없는 드론이 있는 상태에서 정거장이 부당하게 제거되지 않고 드론 Identity·수용량이 보존된다.

- [ ] **Task 9.20: 건설 운반 포함 전체 흐름·경합·부하 검증**
  - 현재 진행(2026-10-12): **부분 구현** — [드론 수명주기 통합 테스트](../../Assets/Editor/Tests/DroneLifecycleIntegrationTests.cs)에 현재 [Test] 6개가 있다. 같은 틱 건물 입고/출고 공간, 취소·완공과 적재품 재배정, 철거 전/중 운반 정리의 정의를 확인했다.
  - 남은 작업: 수행자와 경로 결과를 테스트가 직접 제공한다. 실제 이동·전력·충전·정거장·추가 작업 종류를 포함한 전체 운반 및 부하 검증은 남아 있고 이번 대조에서 테스트를 새로 실행하지 않았다.
  - 선행 조건: Task 9.1~9.19, Phase 7~8 코어 검증.
  - 구현 범위·상태 소유자: 공사 자재 공급, 건물 입출고, 철거, 회수, 충전과 취소·복구를 실제 실행 그룹에서 연결한다. 각 소유자의 상태를 검증한다.
  - 후속 연결: 플레이·UI 검증과 전체 Profiler 비교는 Phase 10에서 수행한다.
  - 검증: 다중 드론·다중 작업·마지막 용량 경쟁, 공사 취소·완료 경합, 운송 중 대상 소멸과 반복 부하를 검사한다.
  - 완료 기준: 아이템·예약·할당·네트워크 불변식과 통합 테스트가 통과하고 부하 조건·측정 결과·미검증 범위가 기록된다.

### 9E: 게임 시작 초기화

- [ ] **Task 9.21: 시작 아이템 설정 로딩 및 초기 지급 계약 구현**
  - 현재 진행(2026-10-12): **자료만 준비** — StartingItemConfig.json은 있으나 이를 게시하는 현재 제품 로더나 초기 지급 Writer는 없다.
  - 남은 작업: 초기 지급 정의의 검증/게시와 부트스트랩 지급을 구현해야 한다.
  - 레거시 검토: 검증한 품목·수량 설정을 주 시설 소유의 실제 아이템과 드론 아이템으로 지급했다. 현재 MaxStack·필수 설정·프리팹 준비와 Item 생성 경계를 재사용하고 전용 Spawn 병렬 구현이나 수량만 기록하는 별도 재고를 만들지 않는다.
  - 선행 조건: 기존 ItemType·스택·소유권 계약과 백업 StartingItemConfig 스키마 확인.
  - 구현 범위·상태 소유자: 기존 품목·수량 목록을 검증하여 초기 지급 정의로 게시한다. 정의 로더는 아이템을 직접 생성하지 않고 실제 지급은 부트스트랩과 기존 Item 생성 경계에서 처리한다.
  - 후속 연결: Task 9.22에서 주 시설 생성 후 초기 아이템·드론 아이템을 지급한다.
  - 검증: 잘못된 품목, 음수·중복 항목, 빈 목록, 설정 누락 및 재로딩의 기존 처리 규칙을 검사한다.
  - 완료 기준: 초기 지급 정의가 유효한 상태로 한 번 게시되고 설정 로딩만으로 중복 지급이 발생하지 않는다.

- [ ] **Task 9.22: 주 시설·초기 전력·정거장·시작 아이템 부트스트랩 구현**
  - 현재 진행(2026-10-15): **공통 생성·전력 기반 준비 / 자동 시작 미구현** — [공통 건물 생성](../../Assets/Scripts/Common/BuildingLifecycleUtility.cs)은 MainFacility에 파괴 불가·Storage·가상 전신주·발전/소비 역할을 부착하고 Power가 기초 발전을 처리한다. Task 9.3의 정거장 보관/전환 기반은 있지만 주 시설 자동 생성과 충전/초기 지급 부트스트랩은 없다.
  - 남은 작업: 준비된 MainFacility 구성과 Power 처리를 재사용해 주 시설을 자동 1회 생성하고 정거장·충전·시작 아이템을 연결해야 한다. 테스트나 직접 Spawn을 새 게임 부트스트랩으로 표시하지 않는다.
  - 레거시 검토: 준비 완료 후 (0,0)에 파괴 불가 주 시설을 한 번 만들고 저장·정거장·소비·기초 발전과 자기 셀만 공급하는 가상 전신주를 연결했다. 실제 전신주 연결 전에는 시설 내부 전력/충전만 가능했다. 현재 V2 Test Scene·종류/티어 Config·Footprint·필수 DB·생성/공개 경계를 재사용하며 SampleScene·프리팹 크기·ChunkMap 예약을 되살리지 않는다.
  - 선행 조건: 완료된 Task 1.7/4.5~4.8/7.3/8.1~8.7/9.2/9.3.1/9.3의 생성/월드/전력/정거장/드론 초기화 및 전환 계약을 재사용한다. 미완료 선행은 Task 9.14/9.21의 충전/지급 연결이다.
  - 구현 범위·상태 소유자: 필수 정의·프리팹·설정 준비 후 기존 시작 위치·footprint·파괴 불가 규칙과 공통 생성 경로로 주 시설을 한 번 구성한다. 건물·공간·전력·정거장·아이템 소유자에 각 상태 반영을 맡기고 초기 지급과 드론 전환을 연결한다. 기존 공개 경계와 새 참가자의 다음 틱 공급 참여를 유지하며 기초 발전·가상 전신주나 전력망 소유자를 중복 구현하지 않는다.
  - 후속 연결: Task 10C.2에서 실제 시작 장면과 사용자 흐름을 검증한다. 연구 초기 해금은 Task 10A.1의 설정 준비와 연결하되 연구 상태를 부트스트랩이 중복 소유하지 않는다.
  - 검증: 빈 월드 시작, 설정·프리팹 준비 지연 뒤 재진입, 반복 업데이트에서 시설·지급·망 등록의 중복과 고아 상태를 검사한다. 치명적 초기화 실패는 공통 Fatal로 중단하며 같은 실패 월드의 자동 재시도/재개를 전제로 하지 않는다.
  - 완료 기준: 테스트용 직접 엔티티 구성 없이 런타임 초기화 경로로 주 시설·초기 전력·정거장·지급품이 일관되게 준비되고 반복 실행으로 복제되지 않는다.

---

## 🔬 [Phase 10] Research / Input·UI·Presentation / Integration·Performance

10A·10B·10C를 각각 독립된 완료 단위로 추적한다. 최소한의 상태 관찰 화면은 앞 단계에서 연결할 수 있다. 설정·Authoring·월드 생성·부트스트랩은 담당표의 Task에서 구현하고, 10B에서는 입력·표시를 연결한다. UI가 추가 상태 소유자가 되지 않도록 한다.

### 10A: Research

확정 게임 규칙은 [연구건물 명세](../Specifications/ResearchBuilding.md)를 재사용한다. 하나의 전역 활성 연구, 병렬 로컬 주기·2주기 재료 보관·원자적 선소비, 변경/완료의 로컬 초기화와 소비 재료 비반환, 전역 진척도 보존·보상 1회·UI 외 권위 해금 검증을 별도 레거시 명세로 중복하지 않는다. 이전 연구의 기본 속도·건축 비용·전력은 현재 종류/티어 Config와 해당 도메인에 연결하고, UI 배치와 음수 보너스 등 차이는 명세의 후속 확인 항목에서 결정한다.

- [ ] **Task 10A.1: 연구 설정·선행 관계·해금·보상 데이터 정의**
  - 현재 진행(2026-10-12): **자료·참조 필드만 준비** — ResearchConfig.json, ResearchBuilding 종류, 건물 RequiredResearch/배치 거부 코드 등은 있으나 연구 설정 ECS 로더·전역/로컬 연구 데이터 소유자는 없다.
  - 남은 작업: 연구 정의·선행/해금·보상 검증/게시와 원본 상태를 구현해야 한다.
  - 선행 조건: 기존 연구 설정과 현재 건물·레시피 식별자 확인.
  - 구현 범위·상태 소유자: 연구 설정을 검증·게시하고 전역 연구 상태, 건물 로컬 진행, 해금·보상의 원본을 구분한다.
  - 후속 연결: 선택·진행·완료는 Task 10A.2~10A.4에서 구현한다.
  - 검증: ID 중복, 선행 관계 순환, 누락된 건물·레시피, 잘못된 재료·보상을 검사한다.
  - 완료 기준: 유효한 설정만 공개되고 실패한 로딩이 부분 상태를 게시하지 않는다.

- [ ] **Task 10A.2: 연구 선택·변경 및 전역 상태 구현**
  - 현재 진행(2026-10-12): **대상 기능 미구현** — 현재 연구 선택 요청 Consumer, 전역 활성 연구/진척도와 로컬 주기 초기화 Writer는 없다.
  - 남은 작업: 확정 명세의 선택/변경·권위 조건·진척도 보존을 구현해야 한다.
  - 선행 조건: Task 10A.1.
  - 구현 범위·상태 소유자: 전역 연구 소유자가 선택·변경 요청과 선행 조건을 검증하고 기존 진척도 보존·로컬 주기 초기화 규칙을 적용한다.
  - 후속 연결: 실제 주기 진행은 Task 10A.3, UI 입력은 Task 10B.4에서 연결한다.
  - 검증: 잠긴 연구, 완료 연구, 반복 선택, 진행 중 변경 및 잘못된 요청을 검사한다.
  - 완료 기준: 연구 원본이 중복되지 않고 확인된 선택·변경 규칙과 요청 수명주기를 만족한다.

- [ ] **Task 10A.3: 연구 재료 소비·로컬 진행·전역 진척도 반영**
  - 현재 진행(2026-10-15): **연구 진행 미구현 / 공통 전력 기반 준비** — ResearchBuilding은 소비 역할·전력 연결·수요/예약/공급 결과 구성을 갖지만 연구 수요는 현재 0이다. Crafter의 재료 선소비·전력 반영은 제작 기능이며 연구 입력·2주기 보관·주기 진행/전역 기여 Writer는 아니다.
  - 남은 작업: 연구 재료/자격을 Preparation에서 준비해 기존 전력 수요 작성에 연결하고 확정 공급에 따른 연구 주기 진행·전역 기여를 구현해야 한다.
  - 선행 조건: Task 10A.2와 Item 입고·소비 경계 및 관련 보완 검증. Task 8.5~8.7의 전력 기반은 완료됐으며 연구 전용 수요/진행 연결이 남아 있다.
  - 구현 범위·상태 소유자: 연구건물의 재료 수령과 전력 외 작업 자격을 준비하고 기존 PowerDemand Writer에 수요 근거를 전달한다. Power가 배정량을 이번 틱 소비로 확정한 뒤 연구 실행 소유자가 공개 공급률·AllocatedEnergy 한도로 주기를 진행하고 결과를 전역 연구 소유자에게 전달한다. 재료·연구 진척과 전력 배분의 소유자를 분리하며 미사용 배정은 환급하지 않는다.
  - 후속 연결: 최종 완료·보상은 Task 10A.4에서 구현한다.
  - 검증: 재료 부족, 무전력, 복수 연구건물, 연구 변경 중 주기 완료와 재료 소비 시점을 검사한다.
  - 완료 기준: 재료·전력·주기 결과가 일치하고 전역 진척도가 중복 반영되지 않는다.

- [ ] **Task 10A.4: 연구 완료·해금·보상 및 생산·건설 연동**
  - 현재 진행(2026-10-12): **대상 기능 미구현** — 건물/레시피의 정적 정의와 배치 검증은 있지만 연구 완료·보상·영구 속도·동적 해금 Writer는 없다.
  - 남은 작업: 연구 완료 1회 처리와 생산·건설 소비자 연결이 남아 있다.
  - 선행 조건: Task 10A.3 및 건설·제작의 권위 있는 요청 검증 경로.
  - 구현 범위·상태 소유자: 전역 연구 소유자가 완료·보상을 한 번 적용하고 건설·레시피·속도 소비자가 공개 데이터를 읽도록 연결한다.
  - 후속 연결: 해금 상태 표시와 입력 제어는 Task 10B.2~10B.4에서 연결한다.
  - 검증: 동시 완료, 완료 후 잔여 로컬 주기, 보상 중복, UI를 우회한 잠긴 건물·레시피 요청을 검사한다.
  - 완료 기준: 해금이 실제 요청 처리에서 강제되고 완료·보상·진행 정리가 일관된다.

### 10B: Input / UI / Presentation

각 Task 착수 시 기존 화면·사용자 흐름을 대조하고 독립적으로 검증할 수 있는 하위 작업으로 나눈다. 입력·렌더 검증은 관련 컴파일/EditMode 결과와 별도로 기록한다.

- [ ] **Task 10B.1: 카메라·월드 입력·UI 입력 차단 연결**
  - 현재 진행(2026-10-12): **대상 기능 미구현** — 현재 제품 C#에는 카메라·게임 입력·UI Toolkit 입력 차단 Controller가 없다. ECS 요청 API만으로 UI 입력 연결을 완료한 것은 아니다.
  - 남은 작업: 카메라/포인터와 월드 요청·UI 차단을 실제 장면에 연결해야 한다.
  - 레거시 검토: 기본/건설 모드 전환은 건물 선택과 배치 입력을 함께 전환하고 UI 위 입력은 배치·철거·선택으로 통과시키지 않았다. 카메라는 이동·줌·가속과 자신이 활성화한 입력만 해제했다. OnEnable/OnDisable 콜백, blocking-ui, World·카메라 유효성 검사를 재사용한다.
  - 선행 조건: 현재 장면·입력 계층과 V2 월드 좌표·상태 조회 계약.
  - 구현 범위·상태 소유자: 카메라와 포인터 좌표 변환, 월드 입력 전달, UI 위 입력 차단을 연결한다. 시뮬레이션 상태는 ECS가 소유한다.
  - 후속 연결: 건설·설정·연구 명령은 Task 10B.2~10B.4, 카메라 기반 청크 요청은 Task 10B.5에서 연결한다.
  - 검증: 좌표 변환과 요청 생성은 가능한 범위에서 자동 검증하고 실제 카메라·입력·UI 차단은 사용자 수동 검증으로 구분한다.
  - 완료 기준: 합의된 입력 흐름이 올바른 명령으로 전달되고 UI 조작이 월드 명령과 중복되지 않는다.

- [ ] **Task 10B.2: 건설·복사·취소·철거 입력 및 미리보기 연결**
  - 현재 진행(2026-10-12): **요청·Command 기반만 준비** — 현재 배치 요청 Utility·Command, 레시피/취소/철거 요청은 있으나 건설 입력 Controller·복사/설정 붙여넣기·미리보기/단축키 UI는 없다.
  - 남은 작업: 기존 ECS 공개 요청을 사용자 입력/미리보기에 연결해야 한다.
  - 레거시 검토: 좌클릭/드래그 배치, 우클릭 철거·취소, Delete 뒤 inclusive 범위 철거·회수·현장 중복 없는 취소, R 묶음 회전, Ctrl+C 범위 복사, Shift+우/좌클릭 설정 복사/붙여넣기였다. 같은 버튼 재선택은 해제, 우선순위는 normal 1~10(기본 5)이었다. 복사는 선택 사각형 좌하단 로컬 pivot과 현재 world anchor를 구분하고 설정은 같은 종류의 요청으로 붙여넣었다. 현재 종류/티어·Strict/Partial 계약을 우선하며 과거 전역 예약/즉시 공간 갱신을 복제하지 않는다.
  - 선행 조건: Task 10B.1, Phase 7, Task 9.17~9.20 및 연구 해금 계약.
  - 구현 범위·상태 소유자: ① 배치·회전·미리보기 ② 복사·묶음 배치 ③ 공사 취소 ④ 범위 철거·회수의 하위 흐름을 나눠 기존 요청 경로에 연결한다. 프리뷰는 점유 원본을 소유하지 않는다.
  - 유지할 기존 규칙: 잠겨서 숨긴 건물 버튼을 제외하고 현재 화면 표시 순서에 숫자 단축키를 연속 배정한다. 1~9는 첫 번째~아홉 번째 버튼, 0은 열 번째 버튼이며 표시 목록이 바뀌는 같은 프레임에 매핑도 갱신한다. 마우스 클릭과 숫자 입력은 같은 건물 선택 동작을 사용한다. 예를 들어 벨트·채굴기·제작기만 보이면 1·2·3으로 선택한다. 기존 표시·해금 조회와 선택 경로를 재사용하며 이 규칙의 보존을 V2 UI 구현 완료로 해석하지 않는다.
  - 후속 연결: 작업 진행 표시는 Task 10B.4에서 완료한다.
  - 검증: 각 하위 흐름의 요청 값·해금·중복 입력과 표시 순서·단축키 대응, 해금 변경 후 즉시 갱신, 클릭·키보드의 같은 선택 결과를 검사한다. 실제 입력·미리보기는 사용자 수동 검증한다.
  - 완료 기준: 모든 하위 흐름의 완료 증거가 있고 표시와 실제 예약·공사·철거 결과가 일치한다.

- [ ] **Task 10B.3: 건물 정보·설정·아이템 작업 UI 연결**
  - 현재 진행(2026-10-15): **ECS 조회·요청 기반 준비 / UI 미구현** — 저장/생산/레시피·공간 상태와 요청 경로에 더해 전력 수요·공급률·배정 에너지·발전 계획·연료 잔량이 준비됐다. 건물 정보·설정·아이템 작업 UI는 없다.
  - 남은 작업: 선택·패널 표시·설정 요청과 UI 콜백 수명을 구현해야 한다. 전력 표시는 현재 공개 상태를 읽어 연결하며 UI에 망 집계나 전력 정산 상태를 따로 만들지 않는다.
  - 레거시 검토: 활성 비보관 드론을 반경으로 우선 선택한 뒤 현장→건물을 조회하고 완공 시 같은 셀의 건물로 선택을 넘겼다. 벨트 속도/셀 아이템·입출력·진행·전력·드론 할당은 ECS에서 표시했다. 잠긴 건물/레시피는 숨기고 권위 요청 검증도 유지했다. 필수 UI 요소 누락은 부분 바인딩으로 진행하지 않았고 행·슬롯·쿼리는 반복 갱신에서 재사용했다. 드론 전용 슬롯/선택 우선순위는 현재 정책과 대조하며 설정 변경·아이템 이동은 공개 요청만 발행한다.
  - 선행 조건: Storage·Crafter 및 완료된 Task 8.1~8.7의 Power 조회 계약, 표시 대상 Drone 기능의 조회·명령 계약, Task 10B.1 및 보완 검증 F2. 드론 충전/상태 표시의 미구현 범위는 Task 9.14·10B.4와 구분한다.
  - 구현 범위·상태 소유자: ① 정보·버퍼/전력 상태 표시 ② 레시피·설정 변경 ③ 아이템 반입·반출 명령을 구분하여 연결한다. UI는 원본 상태를 읽고 요청을 발행한다. 전력 표시는 PowerDemand·PowerSupplyResult·PowerGeneratorSupplyPlan·FuelEnergyState를 재사용하며 역할 활성과 준비 완료 0·미준비를 구분한다.
  - 후속 연결: 도메인 전체 플레이 흐름은 Task 10C.2에서 확인한다.
  - 검증: 선택 대상 제거, 패널 재활성화, 반복 명령, 콜백 등록·해제와 실제 표시·조작을 구분해 검사한다.
  - 완료 기준: 대상·표시·명령이 일치하고 UI에 별도 게임 상태나 중복 콜백이 남지 않는다.

- [ ] **Task 10B.4: 연구 UI 및 작업·드론 상태 표시 연결**
  - 현재 진행(2026-10-12): **작업·관측 데이터 기반만 준비** — 현재 드론 작업/행동 결과는 있어도 연구 UI, 공사 고스트·철거/회수 표시·실제 드론 상태 UI는 없다.
  - 남은 작업: 원본 상태를 소비하는 표시·연구 UI와 정책 차이 확인이 남아 있다.
  - 레거시 검토: 전역 연구 조회 ID·그래프 루트·실제 활성 연구는 구분하고 개별 연구건물 UI는 상태만 표시했다. [연구 명세의 후속 확인](../Specifications/ResearchBuilding.md)에서 자동 배치/설정 좌표 차이와 재료 표시를 확인한다. 공사 고스트·작업 표시는 원본 작업/대상만 관찰하고 모드 종료에도 현장 표시 수명을 유지하며 공간 점유·입력 대상·소유권을 만들지 않는다. 철거/회수 표시는 완료·취소·대상 소멸·아이템 수납 때 제거하고 Footprint·회전·원본 위치를 사용한다. 과거 공용 X 메시/머티리얼은 구현 참고다. 과거 현장 고스트의 청색 반투명, 철거 범위 붉은 경계와 작업 X 표시는 표현 참고로 보존하며 현재 구현 완료나 확정 외형을 뜻하지 않는다.
  - 선행 조건: Phase 9, Phase 10A 및 Task 10B.1.
  - 구현 범위·상태 소유자: ① 연구 선택·진척·해금 표시 ② 공사·철거·회수 작업 표시 ③ 드론 이동·충전·보관 표시를 독립 하위 흐름으로 연결한다. 표시 객체는 원본 상태의 관찰자다.
  - 레거시 피드백에서 이관한 확인 항목: 정상 수납된 드론은 숨기고, 출고되어 작업 또는 충전을 시작한 드론은 다시 표시한다는 요구를 Task 9.13~9.14에서 확정한 원본 상태와 대조한다. 완충 후 충전 대기 표시가 남거나 수납 드론이 화면에 남는 사례를 별도로 확인한다. UI가 수납·충전 상태를 직접 바꾸지 않으며 이관만으로 표시 정책 확정이나 시각 검증 완료를 뜻하지 않는다.
  - 후속 연결: 장면 전체의 실제 표시 결과는 Task 10C.2에서 확인한다.
  - 검증: 완료·취소·대상 소멸 시 표시 제거, 충전/보관 구분, 화면 재진입을 검사하고 시각 검증 결과를 별도 기록한다.
  - 완료 기준: 각 하위 흐름의 UI·표시가 원본 수명주기를 따르며 고아 표시·오래된 정보가 남지 않는다.

- [ ] **Task 10B.5: 카메라 주변 청크 로딩 연결**
  - 현재 진행(2026-10-12): **청크 생성 기반만 준비** — [초기 청크 요청](../../Assets/Scripts/Systems/Initialization/InitialChunkLoadBootstrapSystem.cs)과 [요청/완료 관리](../../Assets/Scripts/Systems/Command/ChunkLoadCommandSystem.cs)는 구현되어 있지만 카메라 주변 요청 Producer는 없다.
  - 남은 작업: 실제 카메라 이동에 따른 요청·재방문·표시 연결을 구현해야 한다.
  - 레거시 검토: 카메라 중심 청크 변경 시 주변 미요청 청크를 요청하고 생성 완료 청크는 건너뛰었다. 현재 Queue·GeneratedChunkTracker·완료 공개 데이터를 재사용하고 완료 상태를 카메라에 영속 복제하지 않는다. 음수 좌표·완료 전후 재방문·반복 요청을 구분한다.
  - 선행 조건: Task 4.6~4.8 및 Task 10B.1.
  - 구현 범위·상태 소유자: 카메라 주변의 기존 로드 범위를 계산해 청크 요청을 발행한다. 카메라는 관찰 범위와 입력을 담당하고 실제 로딩·생성 완료 상태는 Task 4.6의 소유자가 관리한다.
  - 후속 연결: Task 10B.6에서 생성된 청크의 바닥을 표시하고 Task 10C.2에서 실제 월드 확장을 확인한다.
  - 검증: 카메라 청크 경계 통과·되돌아오기·빠른 이동·음수 좌표, 초기 로딩과의 중복 요청을 검사한다. 실제 카메라 입력 결과는 수동 검증으로 구분한다.
  - 완료 기준: 카메라 이동에 따라 필요한 청크·자원이 준비되고 재방문 시 중복 생성이나 불필요한 요청 잔류가 없다.

- [ ] **Task 10B.6: 청크 바닥·바이옴 렌더링 연결**
  - 현재 진행(2026-10-12): **진단 기반만 준비** — [바닥 선택](../../Assets/Scripts/Chunks/FloorBiomeSampler.cs)과 [색상 미리보기](../../Assets/Scripts/Debug/V2FloorBiomePreview.cs)가 ECS 바닥 설정의 바이옴/전이 결과를 표시한다.
  - 남은 작업: 실제 Sprite·청크 메시/머티리얼·렌더 자원 생성/해제는 미구현이다. 진단 색상 텍스처를 제품 바닥 렌더링 완료로 표시하지 않는다.
  - 레거시 검토: 청크별 변형 submesh/material, 재사용 vertex/UV/triangle scratch, 변형 초기화의 Sprite UV/머티리얼은 렌더 구현 참고다. 현재 [바닥 선택 계약](../CodeMemory/Components/WorldAndResources.md#바닥-선택과-진단-표시-계약)과 생성 완료 데이터를 소비하고 점유 상태로 바이옴을 바꾸거나 매 프레임 전체 청크를 폴링하는 병렬 원본을 만들지 않는다. 표시 해제 때 렌더 자원도 해제하되 시뮬레이션 청크 제거를 새로 지원한다고 표시하지 않는다.
  - 선행 조건: Task 4.6·4.9 및 Task 10B.1·10B.5.
  - 구현 범위·상태 소유자: 생성된 청크와 바이옴 결과로 기존 바닥 메쉬·머티리얼·정렬·가시 범위를 연결하고 렌더 자원의 생성·해제를 관리한다. 표현용 가시성 처리와 시뮬레이션 청크 제거를 구분한다.
  - 후속 연결: Task 10C.2 실제 시각 검증, Task 10C.3 렌더링 부하 측정.
  - 검증: 청크 생성 후 표시, 경계 이음, 카메라 이동·재진입, 설정·소재 누락과 렌더 자원 누수를 검사한다. 시각 결과는 사용자 수동 검증으로 별도 기록한다.
  - 완료 기준: 기존 바닥·바이옴 표현이 생성된 월드와 일치하고 중복 렌더 객체·누수 없이 표시 수명주기가 관리된다.

### 10C: Integration / Performance

- [ ] **Task 10C.1: 이식 대응 목록 및 기능별 완료 증거 대조**
  - 현재 진행(2026-10-15): **문서 대조 일부 진행** — 기존 대조에 Task 8.1~8.7 완료, Preparation/Power 실행 경계와 Writer 기반 검사 범위, 직전 Unity 컴파일·관련 선택 EditMode 174/174 근거를 추가했다. Task 9.1의 상태 전이·Owner 문서 계약도 완료했으며 실행 기능의 완료와 구분한다. 기존 수치는 해당 코드 변경의 자동 검증이며 전체 이식·플레이 또는 이번 상태표의 실행 검증은 아니다.
  - 남은 작업: Phase 9~10B의 구현 누락·정책 결정·실제 실행 증거를 최종 대조해야 한다. 앞선 전체 품질 평가 종료와 별개이며 이번 문서 갱신만으로 완료하지 않는다.
  - 선행 조건: 각 도메인의 범위 대응, Phase 0·1·3·4에 추가된 런타임 연결 Task와 Phase 6~10B 결과. 완료된 전력 8.1~8.7과 직전 검증 기록은 재사용하고 남은 드론·연구·입력/UI·시작 초기화의 증거를 구분한다.
  - 구현 범위·상태 소유자: 기존 기능과 담당 Task, 현재 코드, 검증 증거를 대조한다. 누락은 해당 도메인의 하위 Task로 배정하며 이 항목에 구현을 몰아넣지 않는다.
  - 후속 연결: 누락 작업과 증거 공백을 정리한 뒤 Task 10C.2의 실제 흐름을 확정한다.
  - 검증: 부트스트랩·Authoring·설정·월드/자원·입력/UI 등 실제 유지 대상이 명시적인 대응을 갖는지 확인한다.
  - 완료 기준: 미구현·검증 대기·완료가 구분되고 담당 없는 유지 대상이 없다. 의도적인 범위 제외는 사용자 확인을 기록한다.

- [ ] **Task 10C.2: 실제 실행 환경의 전체 흐름 검증**
  - 현재 진행(2026-10-15): **전체 실행 검증 미완료** — 전력·석탄·Miner/Crafter·건설 경계의 관련 선택 EditMode 174/174 근거는 확보됐으나 연구·드론 수행부·충전·UI·시작 초기화가 연결된 제품 전체 흐름은 없다.
  - 남은 작업: 자동 회귀 근거를 유지하며 전체 시작→건설→물류→전력→드론→연구의 사용자 검증과 증거를 확보해야 한다. 이번 문서 갱신에서는 실행하지 않았다.
  - 선행 조건: Task 10C.1에서 확인한 제품 흐름의 구현 누락 해소, Task 9.22의 시작 초기화, 드론 수행/충전·연구·입력/UI의 담당 Task와 Task 10B.5~10B.6의 월드 표시 연결. 완료된 전력 코어와 관련 자동 검증은 선행 기반이며 전체 실행 검증을 대신하지 않는다.
  - 구현 범위·상태 소유자: 시작 월드→건설→채굴·생산·물류→전력→드론→연구의 사용자 흐름과 필요한 장면·에셋 연결을 확인한다.
  - 후속 연결: 재현되는 성능 문제와 대표 부하는 Task 10C.3에서 측정한다.
  - 검증: 사용자 수동 플레이·입력·시각 결과와 자동 테스트 증거를 나누어 기록한다. 별도 실행 요청 없는 Play Mode 자동 검증은 수행하지 않는다.
  - 완료 기준: 실제 흐름의 검증 결과가 있으며 미수행 항목을 통과로 처리하지 않는다.

- [ ] **Task 10C.3: 대표 부하 시나리오 및 Profiler 기준 측정**
  - 현재 진행(2026-10-15): **대표 부하 측정 미완료** — Preparation/Power 분리·반복 검사 제거와 관련 EditMode 통과는 코드·자동 회귀 근거다. 과거 유휴 마커 표본이나 Lookup 수 감소는 대표 규모·반복 조건·ON/OFF 비교의 기준 측정이 아니다.
  - 남은 작업: 규모를 명시한 실제 측정과 병목 판정이 필요하다. 이번 문서 작업에서 Profiler를 실행하지 않았다.
  - 선행 조건: Task 10C.2의 실행 가능한 제품 흐름과 실제 측정 실행 범위 확인. 완료된 전력 코어·자동 회귀를 대표 부하 측정 완료로 취급하지 않는다.
  - 구현 범위·상태 소유자: 아이템·건물·드론 규모와 환경을 명시한 시나리오에서 프레임 시간, Preparation/Power Job 대기, EndPower를 포함한 네 ECB 경계·공간 동기화, 할당·메모리를 측정한다.
  - 이관한 성능 검토: Belt/Building/Resource 공간 인덱스의 전체 재구축 비용, WorldInvariantValidationSystem의 Fence 완료 대기·검사 비용, Reservation/Apply의 직렬 Job 비용을 각각 측정한다. Item 공간 인덱스는 빈번한 이동을 고려해 정적 대상과 구분한다. 불변식 검사를 켠 경우와 끈 경우, 검사 간격, Development 검증 비용과 Release 측정 조건을 구분해 기록한다. 검사 옵션·간격은 현재 제공 여부를 먼저 확인하며 이 문서 정리로 옵션을 새로 구현하거나 검사를 제거하지 않는다.
  - 후속 연결: 확인된 병목만 Task 10C.4의 개별 최적화 대상으로 선정한다.
  - 검증: 측정 환경·규모·반복 조건을 기록하고 추정 비용과 실제 관찰 결과를 구분한다.
  - 완료 기준: 재현 가능한 기준 결과와 우선순위가 있으며 V2 구조만으로 성능 향상을 가정하지 않는다.

- [ ] **Task 10C.4: 측정된 병목별 최적화 및 전후 비교**
  - 현재 진행(2026-10-15): **측정 기반 최적화 미착수** — 전력 반복 검사 제거는 Writer/페이즈 계약 정리다. Task 10C.3의 대표 측정과 동일 조건의 전후 비교가 없으므로 Lookup 감소·자동 테스트 통과를 이 Task 완료나 성능 향상 증거로 보지 않는다.
  - 남은 작업: 확인된 병목만 선정하고 전후 효과·관련 회귀를 확인해야 한다.
  - 선행 조건: Task 10C.3의 현재 실행 구조에 대한 측정 근거. 전력 검증의 중복 제거 자체를 측정된 병목 판정으로 대체하지 않는다.
  - 구현 범위·상태 소유자: 병목마다 별도 하위 Task로 범위를 정해 수정하고 기존 소유권·실행 순서 계약을 유지한다.
  - 이관한 최적화 후보와 조건: 정적 공간 인덱스의 Dirty/Event 갱신, 불변식 검사 간격·활성 제어, 독립된 Item Apply의 병렬화는 측정으로 병목이 확인된 경우에만 검토한다. 공간 인덱스 원본·소유자·Fence와 갱신 공개 시점을 유지하고, 공유 저장 상태에 경합이 있으면 필요한 직렬 처리를 유지한다. 불변식 검사 자체를 제거하거나 모든 시스템을 병렬화하지 않으며 기존 구현을 확장할 수 있는지 먼저 확인한다. 이관한 후보는 구현 승인이나 측정 결과가 아니다.
  - 후속 연결: 변경된 도메인의 검증·문서에 결과를 반영한다. 추가 최적화는 새로운 근거가 있을 때만 확장한다.
  - 검증: 동일 조건의 전후 측정과 관련 회귀 테스트를 수행한다.
  - 완료 기준: 각 변경의 효과·비용·잔여 한계가 확인된다. 최적화 대상이 없으면 측정 근거와 함께 변경 불필요로 종료할 수 있다.

### 2026-10-15 준비/실행 결정 분리·공통 Fatal·Job 의존성 정리

Preparation 이름과 기존 실행 경계를 유지한다. MinerWorkPreparation/CrafterWorkPreparation은 전력 외 자격을, BuildingDecision의 기존 결정은 공급 이후 실행/완료 출력·상태 후보를 소유한다. PowerFrameState를 제거하고 공통 Fatal의 NextTick/RemainingDomains로 중단 범위를 표현했다. 전력 입력 수집/검증·배분·소비·게시는 Job 체인으로 연결하며 EndPower에서 완료한다. 이미 확정한 소비/연료/생산 정책과 Task 93개·완료 56개 집계는 유지한다.

이번 준비/결정 분리·공통 Fatal·Job 의존성 변경은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 170/170(실패·생략·불확정 0)으로 확인했다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

### 2026-10-15 검증 자료 소유자/Job 입력 정리

PowerSimulationGroup을 단계 구성으로 제한하고 검증 자료의 수명/접근 추적을 PowerValidationInitializationSystem으로 분리했다. EndPower가 완료된 첫 오류를 인계받아 공통 Fatal을 게시한다. PowerReadContext는 제거하고 소비자/발전기 입력 Job 및 기타 검증 Job에 필요한 Lookup만 직접 전달한다. 기존 중단 범위·확정 소비·공개 경계와 Task 완료 집계는 유지한다.

이번 소유자/Lookup 분리 변경은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 170/170(실패·생략·불확정 0)으로 확인했다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

### 2026-10-15 Writer/페이즈 경계 기반 반복 검사 제거

설정의 전체 유효성 검사는 게시 전, 필수 구성은 공통 생성이 보장한다. Preparation/Decision의 역할·선택 전체 검증과 Reservation/Execution/Apply의 반복 구성·위치/범위·수요/망 대조를 제거했다. 예산의 준비·대표·유한 값, 실제 소비할 연료·잔량/수량/실물, 단계별 Ready 차단을 유지한다. 대조 전용 계획 필드 4개와 사용처 없는 검증 Job 파일을 제거했다. Task 집계는 바꾸지 않는다.

Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 174/174(실패·생략·불확정 0)을 확인했다. 대상은 전력 51+26, Miner 5, EndToEnd 3, Crafter Execution 6·Input 19, Building Lifecycle 11·Completion 8·Demolish 19·ItemCreationDemolition 16, ResourceAuthoringAndSpawn 10이다. 이전 170/170·112/112과 별도 실행이며 Play Mode·베이킹·Player 빌드·성능은 미수행이다. 상세 Writer 표와 테스트 정리는 [전력 명세](../Specifications/PowerGrid.md#writer와-검사-경계-2026-10-15)를 따른다.

### 2026-10-15 Task 9.1 상태 전이표·Lifecycle Owner 계약 확정

사용자 확정 1A·2A·3A·4A·5A·6A를 [드론 Lifecycle 명세](../Specifications/DroneLifecycle.md)에 반영했다. 현재 작업/배정과 후속 수행자 상태를 나누고 정상·실패·귀환·충전·수납·추가 작업의 전이, 이벤트·버전·예약/화물 정산·Writer·공개 경계·검증 기준을 정의했다. Task 9.1만 계약 완료로 변경했으며 전체 Task 93개, 완료 57개, 미완료 36개다. 앞선 완료 56개 기록은 당시 전력 변경 시점의 집계다.

현재 코드·기존 테스트 정의와의 정적 대조 및 문서 링크/집계 검사를 수행했다. 코드·에셋·설정은 변경하지 않았고 Unity 컴파일·테스트·Play Mode·베이킹·Player 빌드·성능 검증은 실행하지 않았다. 정거장·수행자 생성·이동·배터리·충전과 새 실패 이벤트의 실제 Writer/Reader 연결은 해당 후속 Task의 미완료 범위다. 슬롯 수·충전 수치/분담·적재량 초기화 정책은 이번 계약 확정에 포함하지 않는다.

### 2026-10-22 Task 9.2 정거장 등록·정사각형 범위·드론 네트워크

사용자 확정 1A·2A·3A·4A·5A와 단일 Range·정사각형 A안을 구현했다. 자세한 설정/Writer/공개/Fence 계약과 검수·실행 근거는 [정거장 명세](../Specifications/DroneStationNetwork.md)를 따른다. 이번 변경의 최종 Unity 컴파일은 오류 없고 관련 선택 EditMode는 99/99다. 첫 기존 건설 통합 실행의 3개 실패는 옛 fixture 그룹/준비·공급 구성으로 확인해 보완했으며 최종 5/5와 경계 재검수로 해소했다. 실제 Power 통합·드론 이동/충전·수용량/재배치·전체 실행/성능은 후속이다.

Task 9.2만 새로 완료 처리했다. 최상위 Task는 93개, 완료 58개, 미완료 35개이며 부분 구현 12개·계약/진단 기반 2개·문서 대조 일부 1개·나머지 20개다. 이전 57개/36개 등의 기록은 해당 시점 집계다.

### 2026-10-22 Task 9.3.1 드론 DB/생성 초기화

사용자 지시에 따라 Task 9.3의 Identity 전환/수용량 구현은 진행하지 않고 Task 9.3.1만 완료했다. Authoring/V2 SubScene 참조/4종 DB 준비 검증과 EndSimulation 생성 초기화 API, 배터리 최초 상태를 연결했다. 관련 명세/컴포넌트 계약/주석과 후속 선행 조건을 갱신했다. 실제 설정 최대치 로더/출고/수납/이동/충전은 후속이다.

Unity 컴파일 오류 없음, 관련 선택 EditMode 56/56(실패/생략/불확정 0), 실제 저장 SubScene의 격리 Baker probe에서 드론 DB1/Ready1/Fatal0과 생성 원형/초기 상태를 확인했다. 독립 검수 5개 역할에서 지적은 없었다. 검증 범위/한계는 [생성 명세](../Specifications/DroneCreation.md)를 따른다. 최상위 Task는 93개, 완료 59개, 미완료 34개이며 부분 구현 12개/계약/진단 기반 2개/문서 대조 일부 1개/나머지 19개다. 앞선 집계는 해당 시점 기록으로 유지한다.

### 2026-10-22 Task 9.3 Identity/정거장 보관

확정 정책에 따라 같은 Entity의 Activate/Dock/Store, 전용 5슬롯과 일반 공간 공유, 벨트 드론 입고/출고 제한 및 일반 FIFO, 공통 적재량/최대 배터리 초기화를 구현했다. EndSimulation 공개와 기존 작업/예약 소유자를 유지한다. 독립 6개 역할 검수와 초기화/guard/경계 집중 재검수 지적을 해소했다. 자세한 Writer/가시성/검증 근거는 [Identity/보관 명세](../Specifications/DroneIdentityAndStationStorage.md)를 따른다.

최종 컴파일 오류 없음, 관련 선택 EditMode 340/340, 실제 저장 SubScene의 격리 Baker/제품 생성/동일 Entity 전환을 확인했다. 실제 귀환 목적지 선택/이동/충전/Player/성능은 후속이다. Task 9.3만 새로 완료 처리하여 총93개/완료60개/미완료33개다. 미완료 분류는 부분 구현12개/계약 및 진단 기반2개/문서 대조 일부1개/나머지18개이며 앞선 집계는 해당 시점 기록으로 보존한다.
