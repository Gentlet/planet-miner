# PlanetMiner Architecture V2 - Transient Command & Event Standard

> Architecture V2의 일회성 요청·결과 이벤트에 대한 수명주기 계약이다. 실제 상태 소유자, 소비 시점, ECB 반영 경계를 명시한다. 준비 대기를 없애거나 모든 도메인에 공통 재시도 프레임워크를 추가하는 규칙은 아니다.

---

## 1. 핵심 설계 원칙

### 1.1 Consume-on-Apply (소비자 수명주기 책임)

- 요청의 소비 책임은 해당 Consumer에 있다. 성공·최종 거부 또는 담당 소유자의 대기 상태로 인계가 확정되면 요청을 소비한다. 단순히 요청을 읽거나 검토했다는 이유로 삭제하지 않는다.
- 필수 설정·프리팹 등 준비 조건이 충족되지 않았다면, 정의된 정책에 따라 요청을 유지하거나 소유자의 Pending 상태로 인계한다. 인계한 원래 요청과 Pending 상태가 같은 작업을 각각 실행하지 않도록 한다.
- 처리 완료 후 같은 요청을 다시 실행할 수 있게 방치하지 않는다. 값 변경·Enableable 비활성화·버퍼 제거는 해당 소비 지점에서 수행하고, 엔티티 파괴는 지정된 ECB에 기록해 재생 시 반영한다.
- 요청 소비, ECB 명령 기록, 실제 상태 반영, 결과 이벤트 소비는 서로 다른 시점일 수 있다. ECB에 기록했다는 사실만으로 외부에 실제 완료를 게시하지 않는다.

### 1.2 처리 상태와 프레임 경계

아래 구분은 계약을 설명하기 위한 것이다. 모든 요청에 같은 enum이나 컴포넌트를 추가하라는 뜻은 아니다.

| 구분 | 의미 | 종료·후속 처리 책임 |
| --- | --- | --- |
| 준비 대기 | 실행에 필요한 설정·프리팹 등이 아직 준비되지 않음 | 지정된 Consumer 또는 Pending 소유자가 조건을 다시 확인 |
| 처리 확정 | 성공·최종 거부·대기 상태 인계가 확정됨 | Consumer가 원래 요청을 소비하고 필요한 결과를 남김 |
| ECB 반영 대기 | 생성·파괴 등의 명령은 기록됐지만 아직 월드에 반영되지 않음 | 중복 제출을 막는 담당 상태를 유지하고 지정된 재생 경계를 기다림 |
| 결과 소비 대기 | 실제 반영 이후 결과를 다음 Consumer에 전달 중 | 결과 Consumer가 적용 후 이벤트를 소비; 다음 프레임 소비도 계약에 따라 허용 |

### 1.3 SynchronizationGroup의 역할

- 모든 요청·이벤트가 프레임 끝에 비어 있어야 하는 것은 아니다. 각 타입의 약속된 소비 시점과 허용 대기 조건을 검사한다.
- 같은 StateApply에서 끝나야 하는 요청의 잔류와, 다음 Command까지 유지해야 하는 완료 알림을 구별한다.
- 현재 `WorldInvariantValidationSystem.ValidateRequestLifecycleInvariants`는 활성 `TransferOwnershipRequest`와 `DestroyItemRequest`의 프레임 말 잔류를 검사한다. 모든 요청의 타임아웃을 자동 탐지·정리하는 범용 시스템은 아니다.
- 고아·만료 상태의 진단과 실제 정리는 구분한다. 예약·화물 등 부수 상태가 있는 작업은 담당 소유자의 취소·복구 경로로 정리한다. 검증기가 정상 Pending을 일괄 삭제하지 않는다.

## 2. 요청·이벤트의 저장 방식

기존 컴포넌트와 도메인 소유권을 우선 재사용하며 다음 중 필요한 방식을 선택한다.

| 방식 | 적용 기준 | 소비 방식 |
| --- | --- | --- |
| 독립 Request 엔티티 | 대상이 아직 없거나 독립적인 저빈도 명령. 예: `SpawnItemRequest` | Consumer가 파괴를 ECB에 기록; 실제 삭제는 지정된 재생 시점 |
| 대상 부착 Enableable | 기존 대상의 단일 요청 상태. 예: `TransferOwnershipRequest` | Consumer가 데이터 반영 후 비활성화 |
| 소유자 버퍼의 요청·결과 | 기존 큐·대기 상태와 연결되는 일괄 처리. 예: 청크 요청·완료 알림 | Consumer가 처리한 항목만 제거하거나 전부 처리한 버퍼를 비움 |

- 컴포넌트 요청에는 기존 `IRequestComponent` / `IEnableableRequest` 마커를 사용한다. `IBufferElementData`인 큐·결과 요소에는 같은 수명주기 설명을 적용하되 컴포넌트 마커를 억지로 구현하지 않는다.
- Enableable 한 칸은 여러 독립 요청을 보관하는 큐가 아니다. 여러 Producer가 같은 대상에 쓰는 경우 병합·거부·우선순위 중 적용할 정책과 작성 순서를 정의한다.
- Enableable 토글 자체는 구조 변경이 없지만, 요청 처리의 렌더링 컴포넌트 추가·제거 등은 별도 구조 변경을 일으킬 수 있다. 방식만으로 성능을 보장하지 않는다.
- 벨트 이동처럼 매 프레임 갱신하는 상태는 현재의 영속 상태·결정 컴포넌트를 우선 사용한다. 표준을 맞추려고 새로운 이동 Request 엔티티를 만들지 않는다.

## 3. 8대 필수 메타데이터 주석 규약

새 요청·결과 타입을 만들거나 기존 계약을 변경할 때 아래 항목을 기록한다. 정책이 필요 없거나 아직 구현되지 않았다면 그 사실을 명시한다. 문서 형식만 맞추기 위해 기존 C# 전체를 일괄 수정하지 않는다.

```csharp
/// <summary>
/// [1. 역할]         : 명령/결과 구분과 목적; 접수인지 실제 완료인지
/// [2. Producer]     : 생성 시스템 및 중복·경합 처리 책임
/// [3. Consumer]     : 소비 시스템과 상태 소유자; 결과 수신자가 별도면 함께 기록
/// [4. Create Phase]: 실제 생성 Group 및 즉시 작성/ECB 재생 중 노출 시점
/// [5. Consume Phase]: 소비 Group, 같은/다음 프레임 여부 및 필요한 그룹 내부 순서
/// [6. 수명주기]     : 대기·인계 조건, 소비 방식, ECB 시스템 및 실제 반영 시점
/// [7. 결과 정책]    : 성공/최종 거부/재시도, 중복 결과의 처리와 부수 상태 복구 책임
/// [8. 안전망]       : 정상 대기 조건, 만료 여부·기준, 진단·정리 담당 및 실제 검증 범위
/// </summary>
```

- 준비 대기에 임의의 공통 시간 제한을 추가하지 않는다. 만료가 없으면 없다고 기록하고, 만료가 필요하면 기존 규칙 또는 확인된 요구사항에 따라 정한다.
- 결과 이벤트는 해당 작업을 식별할 방법과 중복·지연 결과 처리 방침을 명시한다. 이미 제공된 키·엔티티·상태를 우선 사용하고 모든 요청에 새 ID를 강제하지 않는다.

## 4. 현재 구현에 적용된 흐름

### 4.1 같은 프레임 소유권 요청

실제 정의는 [ItemRequests.cs](../../Assets/Scripts/Components/Item/ItemRequests.cs), Producer는 [BuildingItemStorageApplySystem.cs](../../Assets/Scripts/Systems/5_StateApply/BuildingItemStorageApplySystem.cs), Consumer는 [ItemOwnershipApplySystem.cs](../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs)를 따른다.

```text
StateApply: BuildingItemStorageApplySystem
  버퍼·입출고 상태 처리 → 대상 Item의 TransferOwnershipRequest 작성·활성화
StateApply: ItemOwnershipApplySystem
  TargetOwner == Entity.Null → 월드 소유권 반영
  유효한 TargetOwner → 저장 소유권 반영
  무효한 TargetOwner → 요청 Drop
  요청 비활성화; 필요한 렌더링 구조 변경은 EndStateApply ECB에 기록
EndStateApply ECB 재생 → Synchronization에서 요청 잔류 검사
```

- 요청 대상은 컴포넌트가 붙은 Item이므로 실제 정의에는 `TargetOwner`만 있다. 별도 `TargetItem`을 추가하지 않는다.
- Producer는 DecisionGroup에서 소유권 요청을 바로 발행하는 예제가 아니라, 예약을 거친 StateApply 흐름을 따른다. Producer에는 Consumer보다 먼저 실행하는 순서가 지정되어 있다.
- 이것은 현재 소유권 필드 요청의 수명주기 설명이다. 버퍼 이전 전체의 성공 결과를 제공하는 범용 트랜잭션 API를 뜻하지 않는다. 무효 소유자 요청을 Drop하는 것을 다른 도메인의 예약·화물 정리 완료로 간주하지 않는다.

### 4.2 준비 대기와 다음 프레임 완료 알림

실제 정의는 [WorldGenerationComponents.cs](../../Assets/Scripts/Components/World/WorldGenerationComponents.cs), 처리 경로는 `ChunkLoadCommandSystem` → `ResourceGenerationCommandSystem`이다.

```text
Command N: ChunkLoadCommandSystem
  외부 청크 요청 소비 → Map/Pending으로 중복 제거 → Pending 및 Ready에 접수
Command N: ResourceGenerationCommandSystem
  필수 설정·프리팹 준비 전이면 Ready/Pending 유지
  준비되면 스폰 명령 뒤에 Completed 알림을 같은 EndStateApply ECB에 기록
  Ready 소비, Pending 유지
EndStateApply N: 스폰 및 Completed 알림 반영
Synchronization N: 생성된 자원의 공간 인덱스 반영
Command N+1: ChunkLoadCommandSystem
  Completed 소비 → Pending에서 Map으로 이전
```

- 준비가 여러 프레임 걸리면 준비된 프레임에 스폰을 제출한다. `Ready`가 남아 있다는 이유만으로 누수로 판단하지 않는다.
- 현재 자원 경로는 준비 실패 시 청크 전체를 대기하며 자동 만료하지 않는다. 새로운 타임아웃·Fallback 정책을 이 문서에서 추가하지 않는다.
- `Completed`는 ECB 재생 후 다음 Command까지 남는 정상 결과다. 요청과 결과를 같은 프레임에 모두 지우면 실제 반영 확인을 잃는다.

## 5. Structural Change & ECB 경계

1. 단순 값 변경·Enableable 토글은 일반 컴포넌트 쓰기를 사용한다. 구조 변경이 없어도, 실제 스폰 뒤의 완료 알림처럼 명령 순서가 필요한 경우에는 같은 ECB에 값·버퍼 변경을 기록할 수 있다.
2. 현재 GameSimulation의 생성·파괴 등은 `EndStateApplyEntityCommandBufferSystem`에서 재생해 Synchronization 전에 반영한다. `EndSimulationEntityCommandBufferSystem`으로 임의 교체하면 검사·공간 동기화보다 늦어질 수 있다.
3. `ecb.DestroyEntity` 호출은 즉시 삭제가 아니다. 재생 전 요청이 존재하는 동안 중복 처리되지 않도록 소비자 실행 순서·소비 표시·Pending 인계 등 해당 경로의 보장을 명시한다.
4. 같은 ECB에서 스폰 뒤에 완료 알림을 기록하는 것은 반영 순서를 보장하기 위한 방식이며, 자동 롤백 트랜잭션을 뜻하지 않는다. 필수 참조는 기록 전에 검증하고 ECB 실패를 완료로 보고하지 않는다.
5. Producer/Consumer가 같은 Group에 있으면 필요한 순서를 명시한다. Group 이름, 폴더 번호, 테스트의 수동 호출 순서만으로 런타임 순서를 보장하지 않는다.

## 6. 변경 시 검증

- 같은 프레임 처리, 최종 거부, 준비 지연 후 처리, 같은 작업의 중복 접수, 실제 반영 후 결과 소비 중 해당 경로에 적용되는 사례를 검증한다.
- 프레임 경계 테스트는 실제 `GameSimulationGroup`과 `EndStateApplyEntityCommandBufferSystem`을 사용한다. 임의의 추가 Playback으로 정상 실행보다 일찍 요청을 노출하거나 제거하지 않는다.
- 프레임 말 검사에는 해당 프레임에 소비가 끝나야 하는 타입만 포함한다. 다음 프레임 결과·준비 대기에는 각자의 수명주기 검증을 적용한다.
- 문서 정리만으로 런타임·테스트 변경을 요구하지 않는다. 실제 동작이 확인된 계약과 어긋날 때 관련 범위를 수정하고 프로젝트의 Unity CLI 검증 절차를 따른다.
