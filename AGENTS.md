# Planet Miner 코드 지도

## 프로젝트 개요

`planet miner`는 격자 기반 2D 채굴·공장 시뮬레이션 Unity 프로젝트다. 현재 소스는 Architecture V2로 전환 중이며, Unity Entities의 unmanaged 컴포넌트와 `ISystem`/Burst Job을 중심으로 구성한다. 설정 로드 일부와 시스템 그룹, 개발용 검증에는 managed 시스템을 사용한다.

이 지도는 2026-09-28에 작성하고 2026-10-06 건물·드론 도메인 분리까지 현재 소스 계약을 갱신했다. 현재 구현 범위는 아이템 수명주기, 벨트·저장·채굴·제작·분배/합류, 청크 자원 생성과 바닥 선택, 프리팹 DB 베이킹, 건물 배치 검증·공사 현장 생성·자재 요구 관리·현장 취소·완공 건물 직접 스폰과 드론 작업 생성·배정·현장 공급 예약이다. 기존 공사 자재 운송/수령/예약 처리는 제거했으며 새 드론의 행동 신호 기반 실물 인계·도착량·예약 정산을 연결했다. 실제 이동·행동 신호 생성은 아직 구현하지 않았다.

`Assets/Scenes/V2 Test Scene.unity`를 빌드와 평소 Editor 편집의 공통 진입 장면으로 사용한다. 전역 `ProjectSettings/EditorBuildSettings.asset`와 활성 Windows 빌드 프로필의 장면 목록은 모두 이 메인 장면 하나를 활성화한다. 메인 장면은 `Assets/Scenes/V2 Test Scene/sub.unity`를 자동 로드하며, 이 SubScene에 건물·아이템·자원 프리팹 DB Authoring이 있다. `V2FloorBiomePreview`의 진단용 색상 바닥은 유지한다. Editor에서는 이 메인 장면을 열어 작업하고 Play Mode Start Scene도 이 장면으로 지정한다. 이 시작 장면 지정은 Editor별 로컬 설정이므로 다른 Editor에서도 같은 기준을 적용한다. SampleScene 계열은 레거시 참고로 남아 있으며 현재 빌드 진입이 아니다.

2026-09-29 F-037 검증에서 공사 완료의 완공 건물 생성과 제작기 입력·생산 연결을 확인했다. 주 시설 자동 부트스트랩, 전력망, 드론 운송, 연구 진행, 게임 입력·카메라 제어·UI Toolkit 컨트롤러, 실제 바닥 청크 렌더링 시스템은 이 지도의 기존 미구현 범위다. enum·요청 필드·프리팹·JSON·계획이 존재하는 것과 해당 기능이 구현된 것은 구분한다.

현재 사용자 지시와 검증한 소스를 기억·문서보다 우선한다. `.agents/` 및 `Docs/architecture v2 plan/`의 계획은 설계 배경으로 참고하고, 현재 동작은 컴포넌트, 쿼리, 업데이트 순서, 실제 호출 관계로 확인한다. 코드 확인 결과와 실제 실행으로 검증한 결과를 구분해 보고한다.

## 문서 기준과 탐색·출력 규칙

- 이 `AGENTS.md`를 프로젝트 공통 설계·안전 규칙, 현재 코드 지도, 검색·출력 및 Unity CLI 검증 절차의 기준 문서(canonical source)로 둔다. 외부 `planet miner.md`는 시작 경로·Git 환경과 여기서 다루지 않는 고유 제약을 보관한다. 아래 상세 문서의 적용 범위를 확인하고, 같은 규칙의 전문을 다른 파일에 복제하지 않는다.
- 이미 컨텍스트에 제공되거나 이번 작업에서 읽은 동일한 지침은 변경되지 않았다면 다시 전문 출력하지 않는다. 지침 변경이나 컨텍스트 소실이 있으면 필요한 부분을 다시 확인한다.
- 대상 파일이나 심볼을 알고 있으면 저장소 전체 검색부터 시작하지 않는다. 기본 순서는 **파일명/경로 확인 → 정확한 심볼 검색 → 해당 메서드·초기화·직접 의존 구간 확인 → 필요한 경우에만 범위 확대**다. `rg --files`로 후보 경로를 좁히고, 리터럴 심볼에는 `rg -n -F`를 사용한다.
- 일반 코드 탐색은 대상이 속한 `Assets/Scripts/` 하위 경로와 관련 `Assets/Editor/` 테스트로 제한한다. 현재 `Assets/UI/`는 없다. 범위를 넓힐 때는 누락된 호출자, 변경된 데이터 계약, 실패한 테스트 등 이유를 정한다. 파일 목록·크기는 후보 선정에 사용하며 목록 전체의 내용을 읽을 이유로 삼지 않는다.
- `.meta`, `.unity`, `.prefab`, `.asset`, `Packages/`, `UIElementsSchema/`는 해당 형식·참조·직렬화·패키지 문제가 작업과 직접 관련 있을 때만 탐색한다. `Library/`, `Temp/`, `Logs/`, `.git/`도 일반 소스 검색에서 제외하고 진단 근거가 있을 때 필요한 경로만 연다. `Assets/Resources/Config/`는 관련 설정 의미를 확인할 때 추가한다. 파일 삭제나 Git 추적 제외를 뜻하지 않는다.
- 검색 결과가 많거나 출력이 잘리면 출력 한도를 늘리지 말고 경로·심볼·검색어를 좁힌다. `Get-Content -Raw ... | Select-String`은 사용하지 않는다. 줄 단위 `rg -n` 또는 `Select-String -Path`로 검색한 뒤 필요한 구간만 읽는다.
- 이미 읽은 대형 파일이 변경되지 않았다면 다시 전문 출력하지 않는다. 심볼·변경 구간과 필요한 직접 의존 부분만 확인한다. 다른 작업자의 변경이나 컨텍스트 소실이 의심되면 해당 파일의 변경 여부를 확인한다. 긴 작업의 인계 요약에는 읽은 경로·심볼, 확인한 계약과 남은 의문을 남겨 재탐색을 줄인다.
- CodeMemory 전체를 읽지 않는다. 현재 작업 영역에 직접 대응하는 문서만 읽고, 다른 영역은 변경된 계약이나 실패 근거가 생겼을 때 관련 절만 추가한다. 구버전 문서의 심볼은 현재 소스에 존재하는지 먼저 확인한다. 문서의 "함께 확인할 영역"은 해당 변경 종류에 적용하며, 모든 의존 문서를 연쇄적으로 읽으라는 뜻이 아니다. 테스트도 관련 사례의 초기화·헬퍼·assertion부터 확인한다.
- 대량 원본 로그는 컨텍스트에 재출력하지 않고 필요한 경우 파일로 보존한다. 성공 결과는 최종 상태·통계만, 실패는 실패명·메시지·관련 스택만 출력한다. 요약 과정에서 실패·경고·생략·미완료 상태를 숨기지 않는다.
- 코드 구조나 계약이 바뀌면 그 변경에 해당하는 AGENTS/CodeMemory 절의 갱신 필요성을 확인한다. 국소 변경을 이유로 모든 문서와 과거 기록을 재감사하지 않는다.

## 에이전트 검수 실행

- 게임 코드 또는 동작에 영향을 주는 에셋을 변경하면 [V2 코드 검수 실행 규칙](Docs/Development/CodeReviewWorkflow.md)에 따라 관련 페이즈와 데이터 계약의 독립 검수를 수행한다. 모든 역할을 매번 실행하지 않는다.
- 프로젝트 전용 프로필은 `.codex/agents/*.toml`이다. 일반 검수는 GPT-6 Luna / xhigh, 경계 통합 검수는 GPT-6.1 Sol / xhigh를 사용하며 프로필의 모델/effort를 따른다. 메인 에이전트의 모델은 변경하지 않는다.
- 메인을 제외한 동시 검수 상한은 6개다. 평소에는 변경에 필요한 역할만 3~4개 선택하며, 작은 변경은 필요한 역할이 더 적으면 그 수만 실행한다. 여러 페이즈/데이터 경계에 걸친 변경은 필요한 경우 5~6개까지 동시에 실행한다. 검수자는 읽기 전용 분석만 수행하고, 메인이 승인된 작업 범위의 수정과 필요한 Unity 검증을 관리한다. 사용자 지시가 이 실행 규칙보다 우선한다.
- 그룹/공개 시점/Writer/소유권/예약/Fatal/공유 자료 의존성이 바뀌거나 중요한 검수 이견이 있으면 `boundary_review`를 포함한다. 결과는 같은 검수 버전의 근거로 판정하며 미확인/실패를 통과로 간주하지 않는다.
- 문서/에이전트 설정만 변경한 경우에는 게임 페이즈 검수나 Unity 컴파일 대신 설정 문법/로드/경로와 변경 범위를 확인한다.

## 실행 단계와 시스템 지도

2026-10-06 건물·드론 도메인 실행 그룹을 분리했다. 사용자가 확정한 1A·2A·3A·4B·5A·6A 규칙은 아래 현재 실행 계약에 반영한다. 완료된 품질 평가·검증 문서는 2026-10-12 사용자 요청으로 제거했으며, 본문에 남은 과거 실행 수치를 새 검증이나 현재 전체 동작 보장으로 확대하지 않는다.

Initialization에서 설정과 초기 청크 요청을 준비한다. SimulationSystemGroup 아래 GameSimulationGroup은 다음 순서로 각 그룹을 한 번씩 실행한다.

```text
Command → EndCommand → Preparation
        → PowerSimulation: Decision → Reservation → Execution → StateApply → EndPower
        → BuildingSimulation: Decision → Reservation → Execution → StateApply(마지막 완공)
        → EndBuilding
        → DroneSimulation: Decision → Reservation → Execution → StateApply
        → SimulationCommit: EndSimulation
        → Synchronization
```

- 건물 입출고·생산 아이템 생성·철거 반환/환급·건물 생성/삭제는 EndBuilding에서 확정되어 같은 틱 드론 입력으로 보인다. 기존 공급원 종류와 품목 제한은 유지한다.
- 각 도메인은 한 번만 실행한다. 건물의 이번 틱 입고 자재를 재차 생산 판단하지 않는다. 드론은 드론 실행 전 계획을 모두 준비하며, 드론 처리 도중 새 수집품이나 새 공간을 기존 계획에 추가하지 않는다.
- 작업·경로·배정과 행동 결과는 EndSimulation에 공개한다. 새 작업·배정·경로는 다음 틱부터 사용한다.
- 완공은 BuildingStateApply 마지막에서 판단한다. 이번 틱 드론의 마지막 납품·방해물 회수는 다음 틱 완공 판정에 반영된다. 이번 틱 완공된 건물은 드론에 보이지만 자체 생산·출고는 다음 틱부터 한다.
- GameSimulationGroup은 틱 시작에 PrefabDatabaseReady, 건물·아이템·레시피·자원/바닥 설정 엔티티와 필수 버퍼, SimulationFatalError를 검사한다. 중간 EndBuilding에서 오류가 확정되어도 현재 틱은 끝까지 수행하고 다음 틱부터 차단한다. 현재 틱 전체 rollback은 보장하지 않는다. Power 검증 실패는 EndPower에서 공통 SimulationFatalError의 StopScope=RemainingDomains로 게시하여 이번 틱 Building/Drone도 건너뛴다.

| 그룹 / 소스 위치 | 시스템과 책임 |
| --- | --- |
| Initialization / Systems/Initialization | 설정 게시·초기 청크 요청과 프리팹 DB 준비를 처리한다. |
| Command / Systems/Command | 청크·자원 요청, 레시피 변경, 배치·철거 승인·공사 취소를 처리한다. EndCommandEntityCommandBufferSystem이 마지막에 현장·자원 생성과 반환·요청 삭제를 확정한다. |
| Preparation / Systems/Buildings/Preparation | 공급/검증 scratch 초기화 뒤 MinerWorkPreparation/CrafterWorkPreparation에 전력 외 작업 자격을 작성하고 전력 요청을 준비한다. 활성 소비 역할은 양수 요청이며 전력 부족으로 의도를 지우지 않는다. |
| PowerSimulation / Systems/Power | Decision은 선택/가용량, Reservation은 망/예산/연료 계획, Execution은 준비·실제 연료 소비 조건 확인 후 확정 소비/공급 게시, StateApply/EndPower는 망/연료 실물 공개를 소유한다. |
| BuildingDecision / Systems/Buildings/Decision, Items/Decision | 기존 이동·입출고·라우팅·아이템 생성 자격을 판단한다. MinerDecisionSystem/CrafterDecisionSystem은 준비 자격과 공급을 읽어 실행/완료 출력·상태 후보를 작성한다. |
| BuildingReservation / Systems/Buildings/Reservation | 기존 저장 슬롯/출고·라우팅 경합을 중재한다. EndPower에서 비워진 연료 공간을 이번 틱 입고에 사용한다. |
| BuildingExecution / Systems/Buildings/Execution | 벨트와 생산 진행/재료 선소비를 처리한다. Miner/Crafter는 확정 공급률·에너지 한도 내에서 진행하고 무전력 신규 착수는 재료를 보존한다. 완료 결과 확정은 전력 때문에 막지 않는다. |
| BuildingStateApply / Systems/Buildings/StateApply, Items/StateApply, Construction/StateApply | 기존 Storage/Routing/Ownership·일반 아이템/건물 수명주기를 적용한다. ConstructionLifecycleApplySystem은 마지막 완공을 담당하며 EndBuilding은 건물 도메인의 마지막 직접 자식이다. 전력망/연료 실물은 앞선 EndPower에서 확정한다. |
| DroneDecision / Systems/Drones/Decision | DroneStationNetworkDecisionSystem은 앞단에서 이번 틱 정거장 망을 판단한다. DroneTaskDecisionSystem은 생성·무효화·경로 의도와 배정 후보를, DroneItemTransferDecisionSystem은 행동 자격을 작성한다. 작업 후보의 실제 망 필터 연결은 Task 9.5 후속이다. |
| DroneReservation / Systems/Drones/Reservation | ConstructionSupplyReservationSystem은 미공개·무효 예약을 정산한 뒤 후보를 선택하고 현장 공급량을 예약한다. |
| DroneExecution / Systems/Drones/Execution | DroneItemTransferExecutionSystem은 OrderFirst로 건물 종료 상태 기준 실물·수량·공간 계획을 준비한다. DroneTaskExecutionSystem은 생성·경로 명령과 순번 발급·최종 ECB 기록을 소유한다. |
| DroneStateApply / Systems/Drones/StateApply | DroneTaskLifecycleApplySystem은 의도 최종 재검사·종료·연결·삭제 보류와 신호 인계·실제 성공량 정산을 담당한다. DroneTaskAssignmentPublishSystem은 OrderLast로 배정을 최종 검사하고 롤백하거나 공개를 기록한다. |
| SimulationCommit / Systems/Commit | EndSimulationEntityCommandBufferSystem이 드론 작업·경로·배정·결과 및 종료 삭제를 확정한다. |
| Synchronization / Systems/Synchronization | 최상위 OrderLast로 최종 Commit 뒤에 실행한다. Belt/Building/Item/ResourceSpatialSyncSystem, PowerGridConnectionSyncSystem, DroneStationSpatialSyncSystem이 원본 ECS로 맵을 재구축하며 WorldInvariantValidationSystem은 내부 OrderLast다. |

### 페이즈 배치와 시스템 명명

- 새 시스템은 처리 대상과 데이터 변경 책임을 먼저 정하고 기존 실행 그룹에 배치한다. Initialization은 Unity의 별도 상위 그룹이며, Command 이후의 사용자 정의 체인은 GameSimulationGroup 안에 있다. 기능별 그룹 추가나 재배치는 기존 실행·공개 경계에 영향을 주는 구조 변경으로 다룬다.
- 시스템 이름은 `대상·책임 + 처리 역할 + System`으로 작성한다. 그룹은 `*Group`, Job은 `*Job`으로 구분한다. 아래 표는 이름 선택 기준과 현재 예외이며, 접미사와 단계 소속을 일대일 대응으로 해석하지 않는다. 실제 처리 책임은 위 시스템 지도와 현재 소스를 따른다. 기존 이름의 차이를 일괄 rename 지시로 해석하지 않는다.

| 소속 / 역할 | 이름 기준과 현재 예외 |
| --- | --- |
| Initialization | 현재 `*InitializationSystem`, `*InitSystem`, `*LoadSystem`, `*BootstrapSystem`을 사용한다. 로드·게시 책임과 BuildingConfigLoadSystem.cs의 예외는 아래 초기화 절을 따른다. |
| Command | `*CommandSystem`. 외부 입력과 시스템 내부에서 전달한 요청을 처리한다. |
| Preparation | `*PreparationSystem`. 전력 외 작업 자격과 요청 근거를 준비 컴포넌트에 작성한다. |
| Decision | `*DecisionSystem` |
| Reservation | `*ReservationSystem` |
| Execution | `*ExecutionSystem`. 진행도·재료 선소비 등 원본 값도 소유 계약에 따라 변경한다. |
| StateApply | 주된 책임에 맞춰 `*ApplySystem`, `*StateApplySystem`, `*LifecycleApplySystem`을 사용한다. 이 역할들은 상호 배타적이지 않다. |
| 공개 기록 | `*PublishSystem`. 현재 DroneTaskAssignmentPublishSystem은 DroneStateApply 마지막이며 별도 Publish 페이즈는 없다. ECB 기록과 실제 Playback 공개를 구분한다. |
| ECB 종료 경계 | `End*EntityCommandBufferSystem` |
| Synchronization / 인덱스 재구축 | `*SpatialSyncSystem` |
| Synchronization / 정합성 검사 | `*ValidationSystem`. 현재 WorldInvariantValidationSystem이 그룹 마지막에 있다. |

- 실제 소속은 `UpdateInGroup`, 같은 그룹 내부 순서는 `UpdateBefore`/`UpdateAfter`/`OrderFirst`/`OrderLast`와 Job 의존성으로 확인한다. 그룹을 가로지르는 순서는 같은 부모의 도메인 그룹 사이에 선언한다. 폴더와 이름만으로 순서를 단정하거나, 그룹 순서만으로 Job 완료와 NativeContainer 안전성을 가정하지 않는다.

### 도메인별 실행 계약

DroneTaskCandidateDecisionElement는 Decision이 매 틱 Clear하고 다시 만드는 순수 후보다. DroneTaskPendingPublicationElement는 Reservation이 선택한 후보 스냅샷·Quantity·CommittedQuantity와 Publish의 PublicationQueued를 소유한다. PublicationQueued는 ECB 기록 완료이며 실제 공개 완료와 다르다. 미공개 CommittedQuantity는 다음 Reservation의 해제까지 보존하고 공개 기록은 다음 Reservation에서 제거한다. 현장 ReservedQuantity는 활성 개별 예약과 아직 공개되지 않은 CommittedQuantity를 포함한다. 후보 Clear가 예약 해제 근거를 제거하지 않는다.

드론 작업 관리에서 DroneTaskDecisionSystem은 생성·무효화·경로 의도와 배정 후보를 작성한다. DroneTaskExecutionSystem은 전달된 의도의 대상 생존·타입·중복·경로 스냅샷을 검사하며 작업 필요 여부를 새로 탐색하지 않는다. DroneTaskLifecycleApplySystem의 무효화·종료 정리는 전달된 의도와 이미 Closed인 작업 참조를 반영한다. 행동 신호 인계와 성공량 정산은 아래 행동 계약을 따른다. 적재품 재배정은 신규보다 먼저 최초 OriginalTaskCreationSequence 순서로 선택하며, 신규 공급 대표 PlacementStamp→CreationSequence·회수 대표 CreationSequence와 두 대표 비교 규칙을 유지한다. 공통 적재량은 DroneCapacityState가 소유하고 초기화·연구 Writer는 후속이다.

드론 행동은 DroneActionRequestUtility.Submit의 World 접수 순서로 적용한다. Lifecycle은 준비된 실물만 현재 생존·Owner·Destroy·대상/버전으로 검사하고 ItemOwnershipApplySystem.TryTransferItem으로 옮긴 실제 성공분을 배정·적재 출처·DeliveredQuantity·예약·행동 번호·결과에 반영한다. 새 TransferOwnershipRequest는 발행하지 않는다. 요청/계획 제거와 결과 추가는 EndSimulation이며 외부 TryConsumeResult가 결과를 소비한다. 공급원 재고·보관 공간의 영속 예약은 없고 목적지 슬롯 예산은 성공분만 소비한다.

안전 방출은 현재 활성·비취소 현장의 회전 footprint 외부만 허용한다. 현재 셀이 내부이면 외부 평가자의 Direct/IsDropPositionSearch 결과를 기다리며 Reachable/HasDropPosition/DropPosition을 검사한다. None/Unreachable은 적재 대기다. 최종 Drop은 선택 목표==요청 위치==관측 격자 셀과 현장 외부를 검사한다. 새 현장이 막으면 Retargeting으로 돌려 실물을 보존한다. 예정 위치 버퍼·기록 helper는 없으며 월드 아이템 생성도 Decision/최종 Apply에서 현장 내부를 거부한다.

건물 철거는 Command에서 승인·PendingBuildingDemolition 게시·이전 Transfer 비활성화·ProductResult Clear 후 후보 생성을 차단한다. ItemSpawnAdmissionDecisionSystem은 철거 대상 Storage/Product 요청을 비활성화하고 EndBuilding에 삭제한다. BuildingLifecycleApplySystem은 승인 상태로 기존 Disabled 포함 대상을 조회하여 반환·환급·철거를 EndBuilding에 기록하며 BuildingType 소실만 방어한다. 기존 실물 반환·비용 환급·활성 Destroy 제외와 같은 틱 재배치의 이전 인덱스 점유 규칙은 유지한다.

현재 드론 관리에는 Task 9.3.1의 생성 유틸리티와 Task 9.3의 명시적 출고/정박/수납 요청 처리가 있다. 실제 이동/귀환 선택/경로 평가/지속 관측/행동 신호와 연구 Writer는 후속이다. [드론 계약](Docs/CodeMemory/Components/DroneLogistics.md), [건설 명세](Docs/Specifications/ConstructionAndDroneSupply.md)를 따른다. 기존 통합 4사례와 기존 142사례는 이전 여섯 phase의 별도 실행 기록이며 새 구조의 검증으로 합산하거나 소급 해석하지 않는다.

2026-10-15 Task 9.1은 [드론 상태 전이·Lifecycle Owner 계약](Docs/Specifications/DroneLifecycle.md)을 문서로 확정했다. 작업·배정·예약·화물은 기존 소유자를 유지하고, 수행자 자체 상태·배터리·관측의 후속 Writer와 공개 경계를 구분한다. 1A~6A의 엄격한 수납 자격, 충전 경유의 완충 전/연속 배차, 정상/비상 귀환과 실제 중단 시 예약 해제·화물 보존을 적용한다. 이 계약 완료는 실제 정거장·이동·충전 구현이나 새 실행 검증의 완료가 아니다. 슬롯 수·충전 수치·적재량 초기화의 남은 결정은 해당 후속 Task에서 다룬다.

[공통 적재량 초기화 계약](Docs/Specifications/DroneCapacityInitializationPlan.md)은 2026-10-22 사용자 확정 6A(설정값 3/공통 Fatal/유효한 기존 singleton 보존)를 따른다. Task 9.3의 DroneConfigInitializationSystem이 carryingCapacity와 최대 배터리를 한 번 게시한다. 개발 마일스톤 Phase7 등의 숫자는 런타임 그룹 개수를 뜻하지 않는다.
2026-10-22 Task 9.2는 [정거장 네트워크 명세](Docs/Specifications/DroneStationNetwork.md)를 따른다. 공통 건물 생성은 주 시설·정거장에 역할/결정/공개 연결을 부착한다. BuildingConfig의 종류·티어별 단일 DroneStationRangeInChunks와 회전 footprint 긴 변으로 정사각형 활동 영역을 계산한다. 음수 Range는 0, 홀수 여백은 양의 축에 1셀을 더 두며 long 합산을 사용한다. 무전력 참가·공유 셀 연결·PlacementStamp 대표 Entity를 적용한다. DroneDecision의 이번 틱 결과를 DroneStationNetworkApplySystem이 EndSimulation에 공개한다. DroneStationSpatialSyncSystem은 공개 연결/건물 원본으로 맵을 재구축하고 같은 singleton의 Fence를 완료한 뒤 재할당/해제한다. 작업 망 필터·수용량·귀환·충전·철거 시 수행자 재배치는 후속 Task다.

## 핵심 데이터와 변경 규칙

2026-10-15 Preparation 이름을 유지하고 준비/실행 결정 컴포넌트를 분리했다. Preparation만 MinerWorkPreparation/CrafterWorkPreparation을 작성하며 enable은 준비 완료이고 작업 불가도 정상 준비 결과다. BuildingPowerDemandPreparationSystem은 이 자격으로 전력 요청을 작성한다. BuildingDecision만 MinerDecision/CrafterDecision/CrafterStateDecision을 작성하고 공급과 완료 출력/상태를 판단한다. Power는 연결/가용량·망/예산·연료 전환/배정 소비·공급을 소유한다. PowerFrameState는 제거하고 공통 SimulationFatalError.StopScope를 사용한다. Power는 RemainingDomains로 같은 틱 Building/Drone을, 기존 오류의 NextTick은 다음 게임 틱을 차단한다. 기초 우선·정격 분담/연료 한도·미사용 배정 소비와 환급/재배분 금지·새 입고/참가자의 다음 틱 참여를 유지한다. 실제 소비할 연료 조건을 모두 확인한 뒤 소비하며 EndPower에 연료 실물/망을 확정한다. 이번 준비/결정 분리·공통 Fatal·Job 의존성 변경은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 170/170(실패·생략·불확정 0)으로 확인했다. Play Mode·베이킹·Player 빌드·성능은 미수행이다. 이전 157/157은 이 분리 전의 실행 기록이다. [전력 명세](Docs/Specifications/PowerGrid.md)를 따른다.

### 공간과 작업 의존성

- ECS 컴포넌트가 원본 상태다. `GridPosition.Value`가 격자 좌표이며 건물·공사 현장은 좌하단 기준점으로 사용한다. `LocalTransform`만 바꾸어 공간 이동을 전달하는 기존 방식은 현재 구조에 없다.
- `BeltSpatialIndex`는 셀→`BeltInfo`, `BuildingSpatialIndex`는 점유 셀→`BuildingInfo`, `ResourceSpatialIndex`는 셀→자원 엔티티, `ItemSpatialIndex`는 셀→복수 월드 아이템을 저장한다. 원본 ECS에서 파생한 검색용 데이터이며 생성·갱신·해제 위치는 다음 수명 관리 절을 따른다. `ChunkMapSystem`은 현재 소스에 없다.
- 인덱스는 Synchronization 이전의 구조 변경을 즉시 반영하지 않는다. 동일 프레임 배치·입출고·스폰 경합은 해당 요청/예약 경계에서 처리하며, 인덱스만 조회하고 이미 반영됐다고 간주하지 않는다.
- 청크 변환은 `Chunks/ChunkUtility`의 floor division, 방향 오프셋은 `DirectionExtensions`, 라우팅 회전·포트 계산은 `RoutingDirectionUtility`를 사용한다. Footprint 정규화·회전 계산은 `Common/BuildingFootprintUtility.GetEffectiveSize`를 사용한다. `BuildingFootprint.Size`는 현장과 완공 건물 모두 방향 적용 전 기본 크기다. 크기 원본은 `BuildingConfigElement.Footprint`이며 건물 종류·티어 조합별로 고정한다. 배치 후보는 Config 크기의 스냅샷이고 다른 크기를 거절한다. 직접 생성의 `(0,0)`은 생략이며 Config를 사용하고 그 외 입력은 Config와 일치해야 한다. DB/타입 기본 크기 fallback은 없다. 동명 확장 메서드도 같은 계산을 호출하며 각 Reader가 한 번 회전한다. 크기 변경은 요청→생성→인덱스의 회전 적용 횟수까지 추적한다.
- 구조 변경은 네 경계에서 확정한다. Command는 EndCommand, 전력망/전환 연료는 EndPower, 건물·일반 아이템·완공은 EndBuilding, 드론은 EndSimulation에 기록한다. Playback과 마지막 Synchronization의 인덱스 갱신을 구분한다. 구조 변경 전후 DynamicBuffer를 보관하지 않고 필요한 경우 복사 후 재조회한다.

### 공유 인덱스와 Fence의 수명·등록 관리

현재 네 공간 인덱스의 수명 소유자는 `Assets/Scripts/Systems/Synchronization/`에 있고 SynchronizationGroup에 속한다. 타입·접근 API의 파일 배치는 아래 소스 배치 절을 따른다.

| 공유 인덱스 / Fence | 생성·갱신·해제 소유자 |
| --- | --- |
| BeltSpatialIndex / BeltSpatialIndexFence | BeltSpatialSyncSystem |
| BuildingSpatialIndex / BuildingSpatialIndexFence | BuildingSpatialSyncSystem |
| ItemSpatialIndex / ItemSpatialIndexFence | ItemSpatialSyncSystem |
| ResourceSpatialIndex / ResourceSpatialIndexFence | ResourceSpatialSyncSystem |

네 Fence 구조체는 `IComponentData`를 상속한 빈 마커 인터페이스 `IFenceComponent`로 분류한다. 인터페이스는 메서드나 공통 상태를 제공하지 않으며 각 구조체가 핸들과 의존성 관리 구현을 유지한다.

- **생성·공개:** 소유 시스템의 `OnCreate`가 `Allocator.Persistent` 맵을 생성하고, 새 singleton 엔티티 하나에 인덱스와 초기 Fence를 함께 게시한다. 소비 시스템은 공유 맵을 빌려 사용하며 별도 맵·Fence를 중복 생성하거나 Dispose하지 않는다.
- **내용 갱신:** 소유 시스템의 `OnUpdate`가 Synchronization에서 Clear → Populate를 예약하고 최종 Writer를 등록한다. 원본 엔티티가 0개여도 Clear한다. 맵의 수명과 매 틱 내용 갱신은 서로 다른 책임이다.
- **Reader Job 등록:** 공유 맵을 읽는 Job을 예약하는 시스템이 `state.Dependency`와 `GetReaderDependency()`를 결합하고, 반환 핸들을 해당 Fence의 `AddReader(handle)`와 자신의 `state.Dependency`에 등록한다. 예를 들어 Resource Reader Job은 MinerWorkPreparationSystem이, Belt Reader Job은 벨트·입출고·라우팅·예약 시스템이 예약한다.
- **Writer Job 등록:** 공유 맵을 쓰는 Job을 예약하는 시스템이 `GetWriterDependency()`로 이전 Writer와 누적 Reader를 의존성에 포함하고, 최종 쓰기 핸들을 `SetWriter(handle)`와 자신의 `state.Dependency`에 등록한다.
- **메인 스레드 동기 접근:** 직접 조회 전에 마지막 Writer 완료를 확보한다. BuildingPlacementCommandSystem은 필요한 세 Writer 핸들을 결합하여 완료하고, WorldInvariantValidationSystem은 해당 Fence를 Complete한 뒤 검사한다. 동기 조회에는 Reader Job 등록을 적용하지 않는다. Clear·Capacity 변경·Dispose 같은 직접 쓰기 전에는 `Fence.Complete()`로 Writer와 모든 Reader를 기다린다. ECS 컴포넌트 의존성만으로 공유 맵 접근이 동기화된다고 가정하지 않는다.
- **핸들 추적 정리:** `SetWriter`는 새 Writer를 저장하고 Reader 추적 핸들을 비운다. 선행 Reader가 새 Writer의 의존성에 포함되어야 하며, `SetWriter` 자체가 완료를 기다리지는 않는다. `Complete`는 Writer와 Reader를 기다린 뒤 양쪽 핸들을 초기화한다. Reader별 `RemoveReader`나 소비자 `OnDestroy`의 등록 해제 API는 없다.
- **최종 해제:** 소유 시스템의 `OnDestroy`가 조회 가능한 Fence를 먼저 완료하고, 조회 가능한 인덱스의 `IsCreated`인 맵을 Dispose한다. Fence 자체는 JobHandle 값만 보관하므로 별도 Dispose 대상이 아니다. 현재 네 시스템은 singleton 엔티티·컴포넌트의 명시적 Destroy/Remove를 수행하지 않으며, World의 ECS 정리와 NativeContainer 메모리 해제를 구분한다.
- **ECB Producer 등록:** 현재 managed ECB 시스템의 `CreateCommandBuffer` 경로에서 Job이 ECB에 기록하면 해당 종료 시스템의 `AddJobHandleForProducer(handle)`에도 기록 핸들을 등록한다. 이는 공유 맵의 Reader/Writer 등록과 별도이며 하나로 다른 쪽을 대신하지 않는다. 메인 스레드에서만 ECB를 기록하고 Job을 예약하지 않는 경로에는 Producer JobHandle이 없다.
- **다른 공유 자료의 소유자:** GeneratedChunkTracker는 ChunkLoadCommandSystem의 `OnCreate`에서 singleton이 없을 때 생성하고 기존 singleton은 재사용한다. Map/Pending은 같은 시스템의 Command `OnUpdate`가 메인 스레드에서 갱신하며, `OnDestroy`는 직접 생성했는지와 무관하게 조회한 singleton의 생성된 집합을 Dispose한다. 공간 Fence는 사용하지 않는다. 새 공유 자료도 수명 소유자·게시 엔티티·접근 방식·갱신 페이즈를 먼저 정하고, Job 접근이 있으면 그에 맞는 의존성 계약을 명시한다.
- **전력 공개 연결 자료:** PowerGridConnectionIndex/PowerGridConnectionIndexFence는 PowerGridConnectionSyncSystem이 같은 singleton에 생성하고 Synchronization에서 Query의 공개 연결을 Clear/재등록하며 종료 시 Fence 완료 후 맵을 Dispose한다. 위 Reader/Writer·재할당·해제 계약을 따른다. 위치/점유를 복제하지 않는 Entity→연결 맵이며 실제 제품 Reader는 후속이다. 같은 틱 준비는 이 맵 대신 PowerGridTopologyDecision/PowerGridConnectionDecision을 사용한다.

### 전력 검증 scratch와 Job 경계

이번 소유자/Lookup 분리 변경은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 170/170(실패·생략·불확정 0)으로 확인했다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

- PowerSimulationGroup은 단계 구성만 담당한다. PowerValidationInitializationSystem은 OnCreate에서 비공개 NativeReference<PowerValidationResult>를 Persistent로 생성한다. 값은 틱 내 검증 결과이며 ECS 상태/설정/연결을 복제하지 않는다. Preparation의 PowerSupplyResetPreparationSystem이 Reset Job을 예약한다.
- 모든 전력 소비자는 GetDependency(state.Dependency)로 선행 scratch 접근을 결합하고 최종 작업/해제 핸들을 소유자의 Register에 등록한다. 이 전력 내부 체인은 첫 오류 보존과 소비 전 연료 조건 확인을 위해 순서를 유지하며 개별 선택/수요/가용량 Job은 병렬 실행한다.
- 입력 Entity 목록은 ToEntityListAsync와 deferred array로 전달한다. 검증 Job은 필요한 RO Lookup만 직접 받고 소비는 별도 RW Job이 소유한다. 공통 PowerReadContext와 반복 소비자/발전기·선택 검증 Job은 제거했다. EndPower는 소유자의 체인 완료/오류 인계 후 공통 Fatal과 ECB를 공개한다. 소유자는 OnDestroy에서 마지막 접근 완료 후 scratch를 Dispose한다. ECS 의존성·ECB Producer 등록은 별도로 유지한다.
- 예산 Reservation은 호출별 로컬 Ready로 준비·대표·배분 수치 검사를 전달한다. 미준비 계획·수요나 무효 대표로 검증이 조기 반환하면 후속 배분·예약 게시를 생략하고 Ready 해제 핸들도 소유자에 등록한다. 공급 결과 초기화는 Preparation의 단독 책임이며 Power Decision에서 반복하지 않는다.

2026-10-15 검수 보완은 선택 전신주 삭제/토폴로지 제거·미준비 계획/수요의 회귀 6사례를 추가하고 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 112/112(실패·생략·불확정 0)을 확인했다. 이전 170/170과 별도 실행이며 대상·한계는 [전력 명세](Docs/Specifications/PowerGrid.md#검수-지적-보완-2026-10-15)를 따른다.

- 전력 설정은 게시 전에 검증하고 필수 구성은 공통 생성이 부착한다. 위치·방향·footprint·선택·수요·배정은 Power 내부의 지정 Writer 이후 바뀌지 않는다. 후속 단계는 그 계약을 신뢰하며 같은 틱 원본/계획을 재대조하지 않는다. 이전 공개 망의 유지권 검사는 Topology Reservation에 한 번, 실제 소비할 석탄·잔량/수량/한도 검사는 Execution에 한 번 남긴다. 소비/망 적용의 준비 누락 차단, 일반 소유 버퍼의 개발용 불변식 검사, Fatal 중단 범위와 해제/ECB 핸들 등록은 유지한다. 자세한 Writer 표는 [전력 명세](Docs/Specifications/PowerGrid.md#writer와-검사-경계-2026-10-15)를 따른다.

2026-10-15 Writer 경계 정리는 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 174/174(실패·생략·불확정 0)을 확인했다. 이전 170/170·112/112과 별도 실행이며 대상과 미수행 범위는 전력 명세를 따른다.

### 아이템·결정·요청

- 아이템 하나는 엔티티 하나다. `ItemIdentity.Type`이 종류, `ItemOwnership.Owner == Entity.Null`이면 월드 아이템이고 그 외에는 해당 소유자에 수납된 아이템이다. ECS `Disabled`를 소유권 표현으로 사용하지 않는다.
- `StoredItemElement`는 저장품/제작 재료/공사 도착 자재, `ProductItemElement`는 생산품 출력 대기 버퍼다. 버퍼의 엔티티 참조와 `ItemOwnership`을 함께 유지한다. `Storage.SlotCount`와 `ItemRegistry`의 품목별 `MaxStack`은 서로 다른 제한이다.
- `BuildingInputSlotElement`는 슬롯별 허용 품목을 정하고 버퍼 길이는 `Storage.SlotCount`와 같다. Crafter는 생성 시 0슬롯/빈 Whitelist로 시작하며, 레시피 변경 시 `BuildingInputSlotUtility`로 품목별 요구량을 합산하여 `ceil(요구량/MaxStack)`개의 전용 슬롯을 구성한다. 입고 예약은 해당 품목 슬롯만 사용한다. 버퍼가 없는 일반 창고에는 이 전용 슬롯 규칙을 적용하지 않는다.
- 일반 입출고는 기존 `BuildingItemStorageApplySystem`의 버퍼·위치·벨트 갱신과 Transfer 요청 → `ItemOwnershipApplySystem`의 Owner·렌더 적용을 유지한다. 드론 실물 이동은 일반 요청 적용 이후 같은 Ownership 소유자의 공통 API로 버퍼·Owner·위치·벨트·렌더를 함께 반영하고 새 Transfer 요청을 만들지 않는다. 같은 Lifecycle 소유자의 행동 반영 메서드가 수령량·예약·배정·결과를 정산한다. 공사 취소 반환과 완공 자재 소비는 공사 시스템이 소유한다.
- 2026-10-04 철거 승인 상태·동작 정지 정책: 기존 F-004의 같은 틱 입고 후 건물 위치 반환·정상 출고 진행을 앞단 입고/출고 차단으로 대체했다. Command가 보관 실물의 이전 Transfer를 비활성화하고 이후 후보 생성도 막는다. Ownership Apply와 Building Lifecycle의 상대 순서는 추가하지 않는다. 유효한 Destroy는 기존 소유 버퍼 선행 제거 및 반환 제외 계약을 유지한다.
- 2026-10-04 기존 공사 운송 제거: 운송/결과 컴포넌트, 공급/운송 취소 요청, 등록·수령·실물 선점·예약 정산·공개 결과 처리와 그 전용 테스트/헬퍼를 제거했다. 유일한 공사 운송 Reader를 잃은 `TransferOwnershipRequest.ProcessedInStateApply` 및 설정/초기화도 제거했다. 일반 Transfer와 Destroy/철거 보호는 유지한다. 후속 드론 2단계에서 현장 예약량 Writer를 새로 연결했으며 3단계 DroneTaskLifecycleApplySystem이 실제 도착량과 인계에 따른 개별 예약·합계를 갱신한다. 기존 F-003 구현은 제거된 과거 경로이며 현재 제공 기능이 아니다. 새 규칙은 [건설 명세](Docs/Specifications/ConstructionAndDroneSupply.md)를 따른다.
- 아이템 생성/삭제는 `ItemLifecycleApplySystem`이 담당한다. 저장된 아이템을 소비할 때는 소유 버퍼에서 먼저 제거한 뒤 `DestroyItemRequest`를 활성화한다. `TransferOwnershipRequest`만으로 소유 버퍼의 추가/제거까지 이루어지지는 않는다.
- 일반 Spawn·생산물 생성과 건물 철거 비용 환급의 프리팹 초기화는 `Common/ItemLifecycleUtility.SpawnPrefabItem`을 공유한다. 호출자가 DB 조회·실패 정책·버퍼 등록·ECB 시점을 소유하며, 유틸리티는 런타임 구성만 같은 ECB에 기록한다. 취소/철거의 기존 실물 반환은 신규 생성과 구분한다.
- `ProductResult`는 Execution이 기록하고 Item Lifecycle이 소비하는 임시 생산 결과 버퍼다. 생산량 `Count`는 실제 아이템 엔티티 수로 변환된다. 생산물의 슬롯 0은 주생산품이고 후속 슬롯은 부산물에 사용한다.
- 2026-10-12 F-006 A 정책: 치명적 오류로 중단된 월드는 계속 사용하지 않고 종료/새 월드 시작을 전제로 하며, 예상 밖 Item 프리팹 누락/Null로 만들지 못한 결과도 마지막 `ProductResult.Clear`에서 소비한다. 유효한 결과의 생성·소유권·Product 버퍼 등록은 EndBuilding ECB 기록이고 정상 Playback 뒤에 실물로 확정된다. 부분 생성의 성공분을 취소하지 않고 미생성 기록은 남기지 않으며 선소비 재료/광물 보상·현재 틱 rollback·자동 복구/재시도·중단 월드 재개는 제공하지 않는다. Fatal은 다음 틱부터 게임 그룹을 차단하며 앱 종료/World 폐기 기능을 추가한 것은 아니다. 승인 철거의 의도적 폐기와 공사 완료 실패의 현장/보관 자재 보존은 기존 별도 계약을 유지한다. 제품 코드 변경과 새 컴파일·테스트·Play Mode·실패 주입 없이 정책 문서화로 완료했으며 조건부 실패의 발생/재현과 일반 Playback 예외 처리를 검증했다고 확대하지 않는다.
- `BeltMovementState`는 enableable 실제 이동 상태, `BeltMovementDecision`은 일반 컴포넌트인 프레임 이동 계획이다. 그 밖의 입출고·채굴·제작·라우팅 결정에는 enable 상태가 처리 대상 여부를 나타낸다. 컴포넌트 존재와 활성 상태를 구분하고, `IgnoreComponentEnabledState` 쿼리는 의도한 범위를 확인한다.
- 일회성 요청은 처리 후 엔티티 삭제, enableable 요청은 비활성화, 임시 결과 버퍼는 Clear로 소비한다. 준비 대기 중인 청크 요청처럼 재시도가 필요한 데이터는 소비 조건이 충족될 때까지 유지한다. 프레임 결정과 `IRequestComponent`를 혼동하지 않는다.
- `GameConstants`가 아이템 간격, 간격에서 파생한 타일 수용량과 벨트 속도 상한, 저장 슬롯 상한, 시뮬레이션 delta time 상한과 월드 광맥 반경/초기 청크 크기의 지원 상한을 제공한다. 벨트 수용량과 속도 상한을 별도 고정 숫자로 정하지 않으며 각 시스템에 숫자 규칙을 복제하지 않는다.

### 베이킹·프리팹

- Baker는 프리팹 참조와 정적 식별 데이터를 제공하고, 생성 시스템이 위치·소유권·진행도·요청의 런타임 상태를 초기화한다. `ItemAuthoring`은 `ItemIdentity`를 베이킹한다. 건물·아이템·자원 DB는 각각 별도 엔티티와 버퍼를 사용한다.
- 프리팹 DB 부재 fallback은 건물·아이템 생성과 철거 자재 환급에서 제거했다. 테스트도 명시적인 ECS 프리팹 DB를 구성한다. 설정값 기본값 fallback과는 별개다.
- `PrefabDatabaseInitializationSystem`은 Initialization 마지막에 요청된 SubScene 로딩을 기다린 뒤 건물·아이템·자원 DB의 유일성, 등록 타입·중복·필수 항목, 엔티티 생존·Prefab·LocalTransform, 아이템 ItemIdentity 일치를 검증한다. 건물은 None/Count/ConstructionSite를 제외한 모든 종류의 1티어와 Config에 작성한 모든 종류·티어 조합이며 설정 없는 등록도 거절한다. 아이템은 None을 제외한 종류, 자원은 활성 생성 설정의 품목이 필수다. 실패하면 오류를 기록하고 시작을 차단하며 자동 재시도하지 않는다.
- `GameSimulationGroup`은 프리팹 DB와 필수 설정의 게시 데이터가 준비되고 `SimulationFatalError`가 없을 때만 실행한다. `PrefabDatabaseReady` 자체는 설정 준비를 뜻하지 않는다. DB는 검증 후 월드 수명 동안 불변이다. Spawn 중 DB/항목 누락은 ECB로 중단 오류를 게시하여 다음 틱을 차단한다. 현재 틱 전체 rollback은 보장하지 않는다. 공사 완료는 공통 Spawn의 non-Null 결과 이후에만 자재·현장 삭제를 기록한다. Null이면 현장과 보관 자재를 보존한다. 직접 생성 요청 소비·생산 결과 Clear·환급 실패 정책의 별도 보상은 이번 변경 범위가 아니다.

2026-10-22 Task 9.3.1은 [드론 생성 명세](Docs/Specifications/DroneCreation.md)를 따른다. DronePrefabDatabaseAuthoring이 V2 SubScene에 단일 Dynamic 원형 참조를 게시하며 공통 준비 검증은 건물·아이템·자원·드론 4종 DB를 요구한다. 원형에는 런타임 수행자/배터리/아이템 상태를 베이킹하지 않는다. DroneSpawnUtility는 Ready 이후 EndSimulation ECB에 새 Entity 또는 기존 루트의 초기 구성을 기록하고 표시 원형의 회전·scale·PostTransformMatrix를 보존한다. 초기 관측은 Revision 1/CanAcceptTask=false, 배정/화물은 비어 있으며 배터리는 호출자가 명시한 최대치로 최초 초기화하고 기존 값은 유지한다. 이 생성 API는 설정/Identity/보관 관계를 직접 소유하지 않으며 아래 Task 9.3의 별도 소유자가 연결한다. 자동 생성/배차·실제 이동/충전은 추가하지 않았다.

### 드론 Identity/정거장 보관

Task 9.3의 [Identity/정거장 보관 계약](Docs/Specifications/DroneIdentityAndStationStorage.md)은 같은 Entity의 Item/Worker 전환과 정박 관계를 EndSimulation에 공개한다. DroneStationLifecycleDecisionSystem은 병렬 Burst IJobEntity로 자격을, ReservationSystem은 단일 Burst IJob으로 기존 작업 예약 선택 뒤 Sequence 순서/슬롯 경합을 처리한다. ApplySystem의 단일 Burst 데이터 Job은 DroneTaskAssignmentPublishSystem의 공개 기록 뒤 최신 실물/활동/공간을 재검사하고 역할/버퍼/결과/비관리형 표시를 ECB에 기록한다. 승인된 Actor/Prefab 목록 완료를 기다린 메인 스레드는 관리형 RenderMeshArray만 별도 ECB에 기록하며 두 ECB 모두 기존 EndSimulation에 공개한다. 입력 deferred array/임시 Native 자료의 마지막 접근과 state.Dependency/ECB Producer를 등록하며 새 공유 Fence는 없다. 새 수행자는 최초 CanAcceptTask=false다. 입고만으로 활성화하지 않으며 수납에는 완충/무배정/Idle/빈 화물/공간을 모두 요구한다. DroneDocked와 DockedDroneElement는 같은 Lifecycle 소유자가 정박 양방향 관계를 기록하고 활성 정박은 슬롯 전체를 차지한다. 철거 시 이 관계의 재배치는 Task 9.19 후속이므로 현재 활성 정박 드론이 있는 정거장 철거는 Command에서 거부한다.

BuildingConfig의 DroneStationStorageCapacity는 주 시설/정거장의 종류/티어별 전용 5슬롯이다. 일반 StorageCapacity 50/20과 분리하며 생성된 Storage.SlotCount는 두 구간의 합계다. 전용 구간은 총 슬롯의 뒤쪽이고 일반 아이템은 들어가지 않는다. 드론 아이템은 ItemConfig MaxStack 50으로 보관하고 귀환 보관은 전용부터 일반 공간을 공유한다. 벨트 드론 입고는 전용 여유만 허용하고 드론 출고는 금지하며 일반 아이템 FIFO는 유지한다. 실제 이동/충전과 DroneActivityState의 지속 Writer는 후속이다.

### 코드 작성 스타일

- 아래는 새로 작성하거나 수정하는 구간의 작성 기준이다. 현재 코드에는 축약명·한 줄 또는 복합 guard 같은 예외가 남아 있다. 스타일 통일을 이유로 요청 범위 밖의 코드를 일괄 변경하지 않는다.
- Allman 중괄호와 4칸 들여쓰기를 사용한다. 타입·메서드·프로퍼티·public ECS 필드와 상수는 `PascalCase`, private 인스턴스 필드는 `_camelCase`, 지역 변수·매개변수는 `camelCase`로 작성한다. static readonly 필드는 해당 타입의 기존 관례를 따른다. JSON DTO와 기존 직렬화 필드의 이름·형식 계약은 유지한다.
- 이름은 대상·행동·역할이 드러나게 작성한다. 상태·판단·요청·결과·설정을 구분하고, 단위나 수명·실행 단계가 의미를 바꾸면 이름에 드러낸다. 루프 인덱스처럼 짧은 범위의 관례적인 이름을 제외하고 의미를 숨기는 축약을 피한다. `Try`·`Can`·`Is` 접두사는 해당 메서드의 작업 성공 여부·실행 가능 여부·상태 판정과 맞춘다. 반환값과 부수 효과·실패 계약을 호출자가 오해하지 않게 작성한다.
- `DirectionEnum`의 `Up, Right, Down, Left, Count` 순서는 회전과 직렬화 의미를 가진다. 기존 enum의 값·순서·`None`·정의된 terminal `Count`를 보존하고, 모든 enum에 `Count`가 있다고 가정하지 않는다. 새 도메인 enum은 기존 `Enum` 접미사 관례를 우선한다.
- 메서드는 한 행동이나 판단에 집중하고, 상위 메서드가 처리 순서를 보여주게 구성한다. 검증·준비·실행·반영 경계가 읽히도록 의미 있는 작업 단위로 나누며, 줄 수를 줄이기 위한 분해로 흐름이나 소유권을 숨기지 않는다.
- 처리 불가능한 입력·상태는 조기 `return` 또는 `continue`로 제거한다. null guard는 각각의 조기 반환으로 분리하고, null·생존·유효성 검사와 행동 조건을 한 조건문에 섞지 않는다.
- 핵심 시뮬레이션의 자료 접근과 변경은 직접적인 `for`·`switch`·지역 변수를 우선한다. 표현 선택은 가독성과 실행 환경의 제약에 맞추며, 기존 QueryBuilder 같은 API 구성 관례는 유지한다.
- 각 변경에 필요한 유효성·불변식 검사를 먼저 수행한다. 최신 상태 재검사·소유 버퍼 변경·예약 정산·성공분 반영·롤백·요청 소비와 ECB 기록/Playback 순서는 기존 도메인 계약을 유지하며 명시한다.
- 공통 계산·판정의 중복은 기존 Utility·확장 메서드로 모은다. 공통화할 때 호출자가 소유한 실행 단계·실패 정책·반영 책임을 유지한다.
- 주석은 설계 이유와 읽는 데 필요한 소유자·생산자/소비자·수명·반영 시점을 기본적으로 한국어로 설명한다. API 이름·기술 용어와 기존 표기는 유지하고, 계약이 바뀌면 관련 주석을 함께 갱신한다. 상세 계약 문서의 전문은 반복하지 않는다.
- MonoBehaviour는 Authoring·표시·입력/요청 생성에 집중하고, 게임플레이 상태와 작업 진행은 ECS가 소유한다. 같은 역할의 새 공간 캐시나 병렬 구현을 중복 생성하지 않는다.
- 사용자의 명시적 요청이 없으면 하나의 시스템을 여러 partial 소스 파일로 분리하지 않는다. Unity Entities 소스 생성에 필요한 partial 선언은 유지한다.
- UI Toolkit을 추가할 때 콜백은 `OnEnable`에서 연결하고 `OnDisable`에서 해제하며, 월드 입력 차단 요소는 `blocking-ui` USS 클래스를 사용한다. 이는 현재 UI 구현이 존재한다는 뜻은 아니다.

## 주요 실행 흐름과 구현 경계

### 초기화·월드 생성

| 설정 입력 | 게시 시스템 / 데이터 |
| --- | --- |
| `Assets/Resources/Config/ItemConfig.json` | `ItemConfigInitSystem`이 `Resources.Load<TextAsset>("Config/ItemConfig")`로 읽어 `ItemRegistry` 태그 / `ItemConfigElement` 버퍼에 게시한다. None을 제외한 모든 품목의 명시적 양수 MaxStack이 필수이며 로드/파싱/검증 실패는 중단 오류다. |
| `Assets/Resources/Config/CrafterRecipeConfig.json` | `RecipeInitSystem` / `RecipeConfigLoader` → `RecipeRegistry`와 레시피·재료·출력 버퍼. 전체 초기화는 ID 1~5를 요구하며 파일 실패 뒤 기본 레시피로 대체하지 않는다. |
| `Assets/Resources/Config/BuildingConfig.json` | `BuildingConfigInitSystem` / `BuildingConfigLoader` → 전력 능력 Power를 포함한 `BuildingConfigElement`, `BuildingConstructionMaterialElement`, 호환용 `BuildingRuntimeConfigElement`. 역할별 전력 검증을 포함하며 실패 시 게시하지 않는다. |
| `Assets/Resources/Config/WorldGenerationConfig.json` | `WorldGenerationConfigLoadSystem` / `WorldGenerationConfigLoader` → 자원 설정과 `FloorGenerationSettings`, `FloorBiomeElement`, `FloorVariantElement`를 같은 엔티티에 게시한다. 자원·바닥·Sprite 참조 검증 실패 시 부분 게시하지 않는다. |
| `Assets/Resources/Config/DroneConfig.json` | `DroneConfigInitializationSystem` → 공통 `DroneCapacityState`와 읽기 전용 `DroneBatteryConfig`. 명시적 양수 적재량/유한 양수 최대 배터리를 검증하고 유효한 기존 상태는 보존한다. 오류/중복은 공통 Fatal이며 이동/충전 설정 필드는 아직 읽지 않는다. |

`BuildingConfigLoadSystem.cs`에는 실제 로더인 managed `BuildingConfigInitSystem`과, `BuildingConfig`를 요구하고 한 번 실행 후 비활성화되는 `BuildingConfigLoadSystem`이 함께 있다. 이름만 보고 로드 책임을 잘못 배정하지 않는다. 전력 능력은 BuildingConfig로 통합했고 별도 PowerConfig.json은 제거했다. 기존 드론·연구·시작 아이템 JSON의 존재는 현재 로더가 사용한다는 증거가 아니다.

- 2026-10-08 F-011: 현재 네 설정 로드 경로와 프리팹 준비는 파일 누락·읽기/파싱 실패·필수 데이터 누락을 로그·해당 설정 미게시·즉시 `SimulationFatalError`로 처리한다. 앱 종료·자동 재시도는 추가하지 않는다. 건물은 None/Count/ConstructionSite를 제외한 11종, 레시피는 ID 1~5가 필수다. 자원과 바닥은 같은 엔티티의 전체 구성을 요구한다. 정상 설정의 빈 건축 자재는 유지한다. 당시의 기본 footprint는 후속 F-022에서 명시적 양수 설정으로 변경했다. 당시 아이템의 공통값/품목별 기본 규칙은 후속 F-007에서 전체 명시 설정으로 변경했다. 격리 테스트는 Init 없이 필요한 최소 데이터를 명시적으로 게시한다. 배치와 공통 Spawn은 설정 부재를 허용하지 않으며 Spawn의 대상 종류 누락도 거부한다.
- 2026-10-11 F-022: 건물 크기는 `Assets/Resources/Config/BuildingConfig.json`의 footprint만 편집한다. 모든 건물에 양수 두 축을 명시하며 누락·빈 배열·형식 오류·0·음수는 로드 실패다. 공개 게시와 사전 등록 Init도 양수 크기를 검사한다. Init 실패는 기존 로그·미게시·Fatal 정책을 재사용하고 무효 사전 등록 원본은 보존한다. 프리팹 Authoring/DB의 크기 필드와 자동 기본 규격 표를 제거했다. 티어 구분·건물명 별도 지정, 그림 자동 배율/회전 복원, R-01 산술 상한과 F-025 진입 장면은 후속이며 이번 범위가 아니다.
- 2026-10-11 F-022 후속 티어·표시 이름: 기존 enum 번호는 유지하고 종류·명시적 양수 Tier 조합으로 설정·프리팹·배치·공사 목표·완공 건물·철거 비용을 조회한다. displayName은 Config의 필수 표시 이름이며 중복을 허용하고 125 UTF-8 bytes를 넘으면 오류다. 전체 설정은 모든 기존 종류의 1티어가 필수이며 공개 부분 게시와 사전 등록도 키·이름·크기를 검증한다. 프리팹은 Type/Tier/Prefab을 수동 등록하고 건물 ResourcesAutoLoad/PopulateFromResources는 제거했다. 기존 11종은 1티어·현재 종류명과 기존 수치를 명시했으며 실제 상위 티어 데이터는 추가하지 않았다. 현장 분류 BuildingType.Tier는 0, 목표 티어는 ConstructionSite.TargetTier다. 완공/직접 Spawn·Sync·환급은 선택한 티어를 유지하고 다른 티어 벨트 위의 배치는 승급으로 처리하지 않는다. UI·그림 보정·기존 건물 승급·연구 Writer는 후속이다. [명세](Docs/Specifications/BuildingTierAndDisplayName.md)를 따른다.
- 2026-10-11 F-025: 사용자가 V2를 빌드·편집 공통 기준으로 확정하여 전역/Windows 프로필의 SampleScene 진입을 기존 V2 메인 장면으로 변경했다. 장면·SubScene·프리팹·Config·제품 코드는 변경하지 않았다. 저장된 V2 SubScene의 실제 Baker에서 건물 11개(모두 Tier 1)·아이템 9개·자원 4개와 24개 정확한 원형 참조, 9개 ItemIdentity를 확인했다. 독립 World에서 현재 Config Init과 PrefabDatabaseInitializationSystem이 Ready 1/Fatal 0으로 받아들였다. 컴파일 상태와 실행 DLL 최신성을 확인했으며 Play Mode·자동 시작 실행·Player 빌드는 미실행이다. 주 시설·드론 생성/이동·UI/입력/카메라·실제 바닥 렌더링의 후속 범위를 확장하지 않는다.
- 2026-10-08 F-007: Item 설정은 None을 제외한 실제 9품목의 MaxStack을 JSON에 모두 작성하며 0·음수·누락 값·빈/무효 이름·숫자 이름·None 항목·중복을 오류로 거부한다. 이름 대소문자는 무시한다. `GetDefaultMaxStackFor`와 `DefaultMaxStack`은 제거했으며 설정 부재를 50으로 보충하지 않는다. `ItemRegistry.GetMaxStack`은 원본 버퍼를 읽는 정적 조회이고 None·범위 밖·인덱스 불일치는 용량 0이다. enum None과 숫자 순서를 유지하기 위해 버퍼 0번에만 내부 None/0을 둔다. 자동 Init의 사전 등록 검사도 전체 인덱스·실제 품목의 양수 값·내부 None/0을 요구한다. 일반 창고는 빈 슬롯 선택 전에 0 이하 한도를 거부하며 기존 F-031 예약 품목/수량·F-037 전용 슬롯·F-008 1회 게시 계약은 유지한다.

- 2026-10-11 F-017: 광맥 반경은 0≤min≤max≤2칸, 초기 청크 한 변은 1~3이며 기존 0→3 해석을 유지한다. 상한/기본값은 GameConstants 한 곳에서 관리하고 JSON·공개 게시·사전 등록 Init이 공통 수치 검사를 사용한다. 관련 JSON 정수 필드의 소수·문자열·int 범위 밖 값도 거절한다. 무효 사전 등록 원본은 보존하고 기존 로그·Fatal을 재사용하며 무효 초기 크기는 Bootstrap의 큐 생성 전에 차단한다. 바닥은 양수 int 영역과 0~영역의 전이 폭을 유지하고 float 곱셈·노이즈/영역의 int 좌표 변환 안전 조건을 적용한다. 이는 전체 지도 크기·표시 정밀도·성능 보장이 아니다. 광맥 중심/셀/거리 제곱은 long 중간 계산 후 대상 청크 내부에서만 int 셀로 변환하며 표현 불가능한 완전 청크는 ChunkUtility가 거절한다. 현재 JSON·시드·난수 순서·청크 공개/완료 수명은 유지했다. R-01/R-02·대규모 로드 분할·새 월드 기능은 포함하지 않는다.

`BuildingConfigLoader`는 `requiredResearch` 변환 전에 UTF-8 길이가 기존 `FixedString32Bytes.Capacity`(29)를 넘는지 검사한다. 초과하면 파싱을 실패시키고 두 out 목록을 null로 유지하여 게시하지 않는다.

- 2026-10-02 F-008: Item/Recipe 설정은 시작 시 한 번 게시하고 World 종료까지 읽기 전용으로 유지한다. 자동 초기화는 필요한 버퍼를 가진 사전 등록 Registry를 사용하고 비활성화한다. 공개 초기화/Recipe 게시 API는 기존 Registry가 있으면 입력 처리·새 엔티티 생성 전에 명확히 거부한다. 게임 중 재로드, 게시된 설정의 직접 수정·삭제·재등록은 지원하지 않는다.
- 2026-10-07 F-014: 같은 레시피의 중복 재료 품목은 합산하지 않고 설정 오류로 거부한다. JSON 재료 수량은 `long`으로 읽어 런타임 `int` 범위를 확인하며, 파싱과 공개 게시 경계가 중복을 거부한다. `RecipeInitSystem`의 입력 검증 실패는 설정 미게시·오류 로그·즉시 `SimulationFatalError`로 게임 시뮬레이션 전체를 차단한다. 자동 Init은 중단 오류 뒤 재시도하지 않는다. 유효한 설정의 입력 슬롯 계산은 기존 Utility를 재사용한다.
- 2026-10-08 F-013: 레시피 JSON의 재료·주생산품·작성한 부산물은 정식 품목명만 허용하며 대소문자 무시·앞뒤 공백 허용을 유지한다. 오타·빈 이름·숫자 문자열·None·미정의 품목은 거부한다. 실제 주생산품은 출력 슬롯 0에 필수이고 없는 부산물은 생략/빈 목록으로 표현한다. 공개 PublishConfig와 자동 Init의 사전 등록도 같은 품목·출력 범위·역할·양수 출력량을 확인한다. 초기화 오류는 기존 로그·미게시·즉시 Fatal을 재사용하고 잘못된 사전 등록 원본은 보존한 채 차단한다. JSON의 기존 비양수 수량/시간 보정과 F-014·F-037·F-008 계약은 유지한다.
- 2026-10-09 F-015: 레시피 정의 ID는 명시적 양수이며 전체 입력 안에서 유일해야 한다. 누락 시 0인 JSON·0·음수·내용이 같더라도 중복 ID는 ParseJson/PublishConfig/제품 Init의 사전 등록에서 같은 검사로 거부한다. 번호 자동 부여·앞/뒤 정의 우선 규칙은 없다. 서로 다른 ID의 동일 생산품은 허용하고 입력 순서는 보존하되 ID로 선택한 제작법은 행 순서와 무관하다. 제품 Init은 기존 로그·미게시·즉시 Fatal을 재사용하고 잘못된 사전 등록 원본은 보존한 채 차단한다. 필수 ID 1~5·명시적 최소 게시·0 이하 변경 요청 해제·1회 게시 계약은 유지한다.
- 설정 버퍼와 엔티티는 ECS가 소유한다. Init 시스템과 외부 호출자는 설정 메모리를 따로 보관하거나 Dispose하지 않으며, Init 시스템만 제거해도 설정은 남는다. World 종료의 tracked Job 완료 경계를 재사용하고, 읽는 Job은 읽기 전용 BufferLookup과 state.Dependency를 등록한다. 버퍼를 프레임마다 복사하거나 Init 내부 필드를 조회하지 않는다.
- ItemConfigElement는 품목 번호와 같은 인덱스에 저장하며 ItemRegistry 태그가 같은 엔티티의 원본 버퍼를 식별한다. 품목별 기본값이나 별도 원본 캐시는 없다. RecipeConfigElement는 같은 엔티티의 RecipeIngredientElement/RecipeOutputElement 버퍼 내 시작 위치·개수를 가진다. RecipeConfigLookupUtility는 기존 ID/주생산품 첫 일치 조회를 제공한다. 레시피 입력 순서, 주생산품 슬롯 0, 기존 기본값·파싱 정책은 유지하며, 게시 실패 시 그 호출이 만든 미완성 엔티티만 회수한다.

1. `InitialChunkLoadBootstrapSystem`이 월드 설정의 `InitialChunkSize`로 원점 주변 N×N 청크를 `ChunkLoadRequestQueue`에 한 번 넣는다.
2. `ChunkLoadCommandSystem`이 `GeneratedChunkTracker.Map`(완료)과 `Pending`(접수/반영 대기)으로 중복을 제거하고 `GeneratedChunkReadyElement`로 넘긴다. 이 Tracker는 생성 수명주기이며 공간 점유 인덱스가 아니다.
3. `ResourceGenerationCommandSystem`은 활성 자원 설정의 프리팹 전체가 유효할 때 `ResourceGenerationUtility`로 자원을 생성한다. 월드 시드·후보 청크·품목을 사용하며, 이웃 청크에서 시작된 광맥 중 대상 청크의 셀만 생성한다.
4. 자원 스폰과 `GeneratedChunkCompletedElement`를 같은 EndCommand ECB에 순서대로 기록하고 Command 끝에 재생한다. 엔티티는 같은 프레임 Decision 전에 존재하지만 공간 인덱스 등록은 Synchronization에서 이루어진다. 다음 Command가 완료 알림을 받아야 Pending에서 Map으로 이동한다. 정상적인 빈 청크도 이 완료 절차를 거친다.
5. `ResourceSpatialSyncSystem`이 생성된 자원을 등록한다. 다음 프레임의 채굴 판단이 이 인덱스를 읽는다.

`FloorBiomeSampler`는 같은 `WorldSeed`와 월드 셀 좌표로 바이옴·전이·변형 Sprite 경로를 선택하는 순수 계산이다. `V2FloorBiomePreview`는 결과를 진단용 텍스처로 보여 주며, 실제 Sprite 청크 메시·표시 수명주기는 구현하지 않는다.

### 배치·공사·완공 건물 스폰

- 플레이어만 배치 요청을 생성하며 사전 가능 여부 확인 뒤 `BuildingPlacementRequestUtility.Submit`으로 접수한다. Submit은 World의 `BuildingPlacementReceiptSequence`에서 양수 `ReceiptSequence`를 발급하고 요청·후보를 함께 만든다. 현재 런타임 UI는 미구현이고 테스트가 같은 API를 사용한다. Command는 실제 접수번호 순서로 최종 검증하여 현장 생성/기존 벨트 방향 변경을 EndCommand에 기록한다. RequestTick·Query/Entity 순서를 접수 순서로 사용하지 않는다.
- 배치 Command는 Building/Resource/Item 공간 Fence를 요구하고 직접 Map 조회 전에 세 마지막 Writer를 완료한다. 다른 Reader 전체나 World 전체를 완료하지 않으며 Validator의 동기화 부수효과에 의존하지 않는다.
- 한 배치 묶음은 기본 `StrictAllOrNothing` 또는 `AllowPartialPlacement` 정책을 사용한다. Validation은 기존 건물/현장·자원·해금·바닥 아이템과 묶음 내부 임시 `claimedCells`, Command 전체의 `approvedCells`를 검사한다. Command는 묶음 정책 확정 뒤 최종 승인 후보만 공유하므로 Strict 실패는 선점을 남기거나 앞 요청의 승인을 해제하지 않는다. 두 목록은 한 Command 처리의 임시 데이터이며 공간 인덱스나 영속 예약을 대체하지 않는다. 접수번호 부재/중복은 오류 기록 후 요청 전체를 EndCommand에 소비한다.
- 승인된 현장은 `BuildingTypeEnum.ConstructionSite`, 위치·크기·방향·`PlacementStamp`, 자재 요구 버퍼와 보관 버퍼를 가지며 건물 공간 인덱스에 포함된다. 바닥 아이템은 배치를 막지 않고 `AwaitingItemClearance`를 표시한다. 기존 같은 타입 벨트 덮어쓰기는 새 현장 대신 방향 변경을 ECB에 기록하며 요청 간 중복도 앞 요청의 최종 승인 우선이다. F-041에서 ReceiptSequence를 Stamp로 승계했다. 설치 비교는 표식 유무→Tick→양수 접수번호 유무/접수번호→요청 내부 Order→좌표 x/y 순서다. 같은 Tick에서는 앞 접수 묶음 전체가 우선하며 완공은 전체 표식을 보존한다. 기본 Tick과 번호는 기존 BuildingPlacementReceiptSequence가 소유하고 Command만 Tick을 전진한다. 직접 생성은 SubmitSpawn 접수 시 생략 표식을 발급한다. API를 거치지 않은 생략 요청은 Apply 준비 묶음의 같은 표식/좌표로 비교하며 실제 접수 순서로 해석하지 않는다. 명시 표식과 기존 벨트 방향 변경의 표식은 보존한다.
- `ConstructionCancelCommandSystem`은 Command에서 취소를 처리하고 자재 반환과 현장/요청 삭제를 EndCommand에 확정한다. `ConstructionLifecycleApplySystem`은 BuildingStateApply 마지막에서 완공만 처리하며 이번 틱 드론 결과는 다음 틱에 검사한다. 기존 운송 등록·수령·예약·결과 처리와 철거 요청 조회는 없다. 완공은 요구 행의 `IsSatisfied`와 현장 플래그를 검사하며 공통 Spawn 성공 후에만 자재/현장을 삭제한다. `ConstructionSite.Progress`는 제거했고 자재 도착 비율을 보관하지 않는다. 신호 기반 드론 공급/회수 인계는 연결됐으며 적재품 재배정·바닥 차단 갱신은 4단계에서 연결했으며 실제 이동은 후속이다.
- 직접 생성은 `SpawnBuildingRequest` → `BuildingLifecycleApplySystem`, 공사 완료는 `ConstructionLifecycleApplySystem`에서 별도 Spawn 요청 없이 공통 `BuildingLifecycleUtility.SpawnBuilding`을 호출한다. 두 경로 모두 타입별 런타임 구성을 주입한다. F-037은 자재 요구가 충족된 현장에서 완공한 Crafter의 생성·제작 연결을 검증했으며 전체 배치·자재 배송이나 취소·철거를 검증한 것은 아니다.
- 공사 취소는 Command의 Cancel Job이 `Cancelled`를 즉시 설정하여 중복 반환을 막고, 보관 실물을 현장 위치의 월드 아이템으로 반환하며 현장/요청 삭제를 EndCommand에 기록한다. 활성 Destroy 실물은 반환하지 않고 기존 Item Lifecycle이 삭제한다. Command 취소 처리 이후 생성된 요청은 다음 Command에서 대상을 재검증하며 이미 완공된 건물은 보호한다. 같은 틱 취소/철거와 재배치는 기존 공간 인덱스의 점유를 기준으로 재배치를 거부한다. 조기 공간 동기화나 Command 중간 ECB 재생을 추가하지 않으며 Synchronization 이후 새 요청부터 변경된 점유로 검증한다. 관련 테스트는 도착 실물을 직접 준비하고 운송을 검증하지 않는다. 건물 철거의 Command 검증·기존 실물 반환·비용 환급 및 일반 입고·철거(F-004)는 앞의 유지 계약을 따른다.
- `BuildingConfigElement.IsUnlocked`와 해금 변경 유틸리티는 있지만 연구 진행 시스템은 없다. 전력·드론·연구 건물 종류와 프리팹 등록도 해당 시뮬레이션 구현을 의미하지 않는다.

### 물류·생산

- 2026-10-10 F-032: 건물 입고와 Splitter/Merger의 원본은 월드 소유이며 `BeltMovementState`가 활성인 종단 아이템만 선택한다. 정체로 이번 틱 이동량이 0인 활성 아이템은 제외하지 않는다. 입고는 매 판단 시작에 대상 Null·CanDeposit=false·슬롯 -1·결정 비활성으로 초기화하고 활성 후보만 게시한다. 비활성 프레임 결정의 재판단을 위한 `IgnoreComponentEnabledState`는 유지하며 실제 이동 활성은 별도로 검사한다. 바닥 실물의 잔여 Progress는 자격이 아니다. 기존 현장 회수 후 완공과 정상 출고의 벨트 복귀를 유지하며 재설치/직접 Spawn에 자동 복귀를 추가하지 않는다.
- 입고: `BuildingItemInputDecisionSystem`이 벨트 종단의 다음 셀, `Storage`, 필터, 제작기의 잔여물 대기를 확인한다. `BuildingStorageInputReservationSystem`이 프레임 임시 맵에 예약 품목·수량을 함께 기록하여 일반 창고의 새 슬롯도 같은 품목의 MaxStack까지 재사용하고 제작기 전용 슬롯 제한을 유지한 채 슬롯을 확정한다. 이후 `BuildingItemStorageApplySystem` → `ItemOwnershipApplySystem`이 반영한다.
- 출고: 일반 창고는 `StorageItemOutputDecisionSystem`이 첫 보관품을, 생산 건물은 `ProductItemOutputDecisionSystem`이 슬롯 0 우선으로 생산품을 선택한다. 외향 벨트의 입구 여유를 검사하고 공통 예약/반영 경로를 사용한다.
- 벨트 이동: `BeltMovementDecisionSystem`이 현재/다음 셀의 간격과 수용량으로 이동량을 계산하고 `BeltMovementExecutionSystem`이 격자·진행도·시각 위치를 변경한 뒤 계획을 소비한다. 현재 `BeltDestinationReservationSystem`의 후보는 건물 출고와 라우팅 전달뿐이다. 일반 벨트 이동까지 통합 예약한다고 가정하지 않는다.
- 2026-09-30 F-030 사용자 선택: 벨트 속도를 `GameConstants.MaxBeltSpeed = (1 - ItemSpacing) / MaxSimulationDeltaTime` 이하로 제한한다. 현재 상한은 초당 7.5칸이며 설정 속도도 7.5다. 설정 로더는 비유한 값과 상한 초과를 거부하고, 공통 완공 건물 생성 경로는 기존 기본속도 처리를 유지한 뒤 상한을 적용한다. 한 틱의 이동량은 최대 0.75칸이므로 유효한 progress 범위에서 다음 셀로 이동한 후에도 미조회 다다음 셀 입구까지 최소 간격을 남긴다. 기존 Decision/Execution과 Job/Fence 계약을 유지하며 전방 조회나 일반 이동 예약을 확장하지 않는다. 이 정책은 기존 겹침 복구나 일반 T형 직접 합류 중재를 보장하지 않는다.
- 벨트 진입: 출고 두 Decision과 Splitter/Merger Decision, 목적지 예약은 `BeltEntryUtility.HasEntrySpace`로 현재 공간 스냅샷을 검사한다. 월드 소유권과 활성 `BeltMovementState`를 모두 가진 아이템만 점유에 포함하며, `ItemSpacing`과 `AlignmentEpsilon`, 간격에서 파생한 수용량을 사용한다. 공간 검사와 신규 후보 중재는 별개다. 예약은 출고·Routing 후보 중 목적지당 한 틱 최대 한 개를 기존 `PlacementStamp` 비교로 승인한다.
- 2026-09-30 사용자 확정: 일반 벨트끼리의 T형 직접 합류는 자동 경합 중재·사전 대기·교착 해소 보장 대상에서 제외한다. 합류/분배 중재는 Merger/Splitter의 역할이며 이를 일반 벨트 예약으로 확장하지 않는다. 현재 외향 벨트의 바로 뒤 셀은 출처 건물 또는 라우터이므로, 해당 출력 셀에 추가 일반 벨트가 직접 진입하는 측면 합류도 통합 예약 구현의 근거로 삼지 않는다. 이 정책이 기존 겹침을 복구하거나 불변식 위반을 면제한다는 뜻은 아니다.
- 분배/합류: `SplitterDecisionSystem`은 입력 벨트 기준 forward→right→left, `MergerDecisionSystem`은 출력 벨트 기준 back→left→right 순환 후보를 선택한다. 예약을 통과한 `RoutingTransferDecision`은 `RoutingApplySystem`이 실제 이동과 커서 갱신에 사용한다. 설치 우선순위는 `PlacementStamp`와 해당 비교 구현을 따른다.
- 채굴: Decision이 footprint 아래 첫 유효 자원과 동일 품목 1스택 출력 여유를 검사한다. Execution은 진행도·자원량과 `ProductResult`를 갱신하며 유한 자원 고갈은 ECB로 삭제한다. `ResourceConfig` 부재 시 기본은 유한 자원이다.
- 제작: `CrafterDecisionSystem`이 실행 결정과 `CrafterStateDecision`을 나누어 기록한다. Preparation의 CrafterWorkPreparationSystem과 Execution은 기존 `RecipeConfigLookupUtility.HasRequiredIngredients`로 모든 유일한 재료 요구량과 비활성 `DestroyItemRequest`를 가진 보관 실물을 검사한다. Execution은 전체 확인이 성공한 뒤에만 재료를 선소비하고 진행/출력 결과를 만들며, 부족하면 버퍼·삭제 요청·진행도를 변경하지 않는다. `CrafterStateApplySystem`이 상태를 반영한다. 출력은 주생산품과 모든 부산물 슬롯의 여유를 함께 검사한다.
- 레시피 변경: `CrafterRecipeCommandSystem`이 새 입력 슬롯 계산을 검증한 뒤 진행을 초기화하고 슬롯 수·배정·필터를 함께 갱신한다. 남은 입력 재료는 기존 입력 슬롯 구분을 유지하여 생산물 버퍼의 빈 후속 슬롯으로 옮긴다. 설정 미게시 시 선택 요청을 대기시키고, 무효 레시피/계산 실패는 기존 상태를 보존한다. 해제는 0슬롯/빈 Whitelist이며 설정 없이도 처리한다. `WaitingForByproductOutput` 동안 입고와 새 제작을 막는다. Execution은 이 변경을 다시 처리하지 않고 확정된 레시피와 실행 결정으로 선소비·진행·생산 결과를 기록한다.

## 주요 폴더와 테스트 탐색

개발용 `WorldInvariantValidationSystem`은 Storage 용량 검사와 독립적으로 모든 Stored/Product 버퍼의 실존·Identity·Owner·참조 유일성을 검사한다. Storage 없는 공사 현장도 포함하며 진단을 위해 상태를 수정하지 않는다. 보고 파일은 World별 세션 ID와 보고 순번을 사용하고 CreateNew로 기록한다. 쓰기 실패는 위반 수를 유지하고 디버그 출력으로 남기며 파일 보존까지 보장하지 않는다.

| 경로 | 역할 |
| --- | --- |
| `Assets/Scripts/Authoring/` | 건물·아이템·자원 프리팹 DB 및 아이템 정적 식별 Baker |
| `Assets/Scripts/Components/` | 격자·설정·요청·실제 상태·프레임 결정·버퍼·공간 인덱스 계약 |
| `Assets/Scripts/Phases/`, `Assets/Scripts/Systems/` | 위 실행 단계 및 각 단계의 시스템 |
| `Assets/Scripts/Common/` | 상수, 요청 인터페이스, 방향·라우팅·배치 검증 |
| `Assets/Scripts/Chunks/` | 청크 좌표 변환, 결정론적 자원 생성, 바닥 선택 |
| `Assets/Scripts/Config/` | 건물·월드·레시피 설정 파싱/검증/게시 |
| `Assets/Scripts/Debug/`, `Assets/Scripts/Validation/` | 바닥 진단 표시와 ECS 불변식 검사 |
| `Assets/Scenes/`, `Assets/Resources/Prefabs/` | 장면/SubScene 및 도메인별 프리팹 |
| `Assets/Resources/Config/`, `Assets/StreamingAssets/` | 위 설정 입력과 기존 설정 파일 |
| `Assets/Editor/` | 건물 DB의 명시 등록, 아이템 DB의 Resources 목록 채우기 Inspector와 EditMode 테스트 |
| `Tools/Codex/Verify-Unity.ps1` | PowerShell 7 이상용 Unity 컴파일/선택 테스트 검증 래퍼 |

`Components/`는 도메인별로 나눈다. `Common/`은 격자·방향·공통 요청 계약, `Prefabs/`는 프리팹 DB, `Items/`는 아이템 식별·소유권, `Storage/`는 저장·필터·입력 슬롯, `Buildings/`는 건물 공통·생성·철거·배치 순서, `Construction/`은 배치 검증·공사·자재 요청을 둔다. `Belts/`, `Routing/`, `Resources/`, `World/`는 각각 벨트, 분배·합류, 자원, 청크·월드 생성 계약을 둔다. `Production/`에는 공통 생산 결과와 출력 대기 버퍼를, 그 아래 `Mining/`과 `Crafting/`에는 채굴·제작 전용 계약을 둔다.

각 도메인에서는 상태·버퍼를 `*Components.cs`, 일회성/enableable 요청을 `*Requests.cs`, 프레임 결정을 `*Decisions.cs`, 실행 후 소비되는 임시 결과를 `*Results.cs`, 설정을 `*ConfigComponents.cs`로 구분한다. 공통 인터페이스·독립 계약 파일은 타입 이름을 유지하며, 공간 인덱스와 Fence는 해당 도메인의 `*SpatialIndex.cs`에 함께 둔다. 입출고 결정은 `Storage/BuildingItemDecisions.cs`, 월드 공통 시드·초기 청크 설정은 `World/WorldGenerationConfigComponents.cs`, 임시 생산 결과는 `Production/ProductResults.cs`에 둔다. 파일 분류는 실행 순서나 상태 소유권을 바꾸지 않는다. 현재 파일별 타입은 [C# 파일 색인](<Docs/architecture v2 plan/CodeMemory/CSharpFileIndex.md>)에서 찾는다.

공통 정적 Utility와 확장 클래스의 기본 위치는 `Assets/Scripts/Common/`이다. 청크·바닥 전용인 ChunkUtility와 FloorBiomeSampler는 `Chunks/`의 기존 관례를 유지한다. 건물 통합 설정과 호환용 런타임 설정의 조회는 `BuildingConfigLookupUtility`의 버퍼 타입별 오버로드로 제공하며, 프리팹 조회와 자원 생성 설정 조회는 각각 `PrefabLookupUtility`, `ResourceGenerationConfigLookupUtility`가 담당한다. 컴포넌트 생성자·변환 연산자와 인덱스 조회·Fence 의존성 추적 API는 데이터 정의와 함께 둔다. 공유 맵의 생성·내용 갱신·Dispose 및 JobHandle 등록 호출의 책임은 앞의 수명 관리 절을 따른다.

관련 테스트는 `Assets/Editor/Tests/`에서 다음 범위로 먼저 좁힌다. 이름은 존재하는 검증 코드의 위치이며 최신 통과 결과를 뜻하지 않는다.

| 변경 영역 | 우선 확인할 테스트 클래스/파일 묶음 |
| --- | --- |
| 베이킹·아이템 생성·소유권 | `PrefabDatabaseInitializationTests`, `Phase1ItemIntegrationTests`, `Phase1ItemAuthoringPrefabTests` |
| 벨트 이동 | `Phase2BeltExecutionTests`, `Phase2BeltIntegrationTests` |
| 저장·입출고·공간·설정 | `Phase3Storage*`, `Phase3Building*`, `Phase3ItemConfigTests` |
| 월드·청크·자원·바닥 생성 | `Phase4WorldGenerationConfigTests`, `Phase4ChunkLifecycleTests`, `Phase4ResourceGenerationTests`, `Phase4ResourceAuthoringAndSpawnTests`, `Phase4FloorBiomeGenerationTests` |
| 채굴 통합 | `Phase4MinerPipelineTests`, `Phase4EndToEndPipelineTests` |
| 제작·레시피 | `Phase5RecipeConfigTests`, `Phase5CrafterExecutionTests`, `Phase5RecipeChangePipelineTests`, `Phase5CrafterInputSlotTests`, `Phase5CrafterInputPipelineTests` |
| 분배·합류·목적지 예약 | `Phase6*` |
| 배치·현장·자재·건물 스폰 | `Phase7PlacementCommandTests`, `Phase7ConstructionCancelTests`, `Phase7ConstructionCompletionTests`, `Phase7BuildingLifecycleTests`, `Phase7BuildingAuthoringPrefabTests`, `Phase7EndToEndConstructionPipelineTests` |

2026-09-30 사용자 선택에 따라 핵심 통합 흐름과 실제 결함 회귀 중심으로 테스트를 축소했다. 개별 컴포넌트·조회·방향 계산·불변식 검사기 및 반복 정상 경로의 독립 검증 일부는 제거했다. 아이템 렌더 태그·공간 등록과 벨트 시각 위치 검사는 기존 통합 테스트에 흡수했다. F-037 제작기 입력, F-005 생성/철거 경합, F-024 완공 실패 보존 및 프리팹 초기화 검증을 유지한다. 과거 문서의 삭제된 테스트 이름과 실행 건수는 당시 기록이며 현재 검증 범위로 사용하지 않는다.

`TestSupport/EcsWorldTestFixture`가 독립 World를, `TestEntityFactory`가 테스트용 엔티티를, `TestSimulationDriver`가 시간·시스템 실행·ECB 경계를 제공한다. Factory 구성과 실제 Spawn/Baker 구성이 같은지 확인한다. 개별 시스템 직접 호출 테스트와 실제 정렬된 그룹 테스트, 테스트용 ECS 프리팹과 실제 SubScene 베이킹 결과를 구분한다.

`WorldInvariantValidationSystem`은 `UNITY_EDITOR || DEVELOPMENT_BUILD`에서만 동작하며 기본적으로 매 프레임 아이템·공간·요청 소비·벨트 간격·저장 버퍼·자원 정합성을 검사한다. 위반은 `Logs/InvariantErrors/`에 기록하고 `Debug.Break()`를 호출한다. 현재 검사 범위를 모든 건설/후속 도메인에 대한 완전한 보증으로 확대 해석하지 않는다.

## 상세 문서와 적용 범위

| 문서 | 용도와 주의 |
| --- | --- |
| [ECS 컴포넌트 색인](Docs/CodeMemory/Components/README.md) | 컴포넌트별 목적·부착 엔티티·생성·Reader/Writer·단계·소비와 종료를 추적하는 현재 소스 기반 문서. 정적 분석과 실행 검증을 구분한다. |
| [건설 현장과 드론 자재 공급 명세](Docs/Specifications/ConstructionAndDroneSupply.md) | 확정한 건설·공급·회수·예약 규칙과 2026-10-05 phase 책임 분리. 기획 전체 및 실제 드론 운송의 구현 완료를 뜻하지 않는다. |
| [V2 코드 지도](<Docs/architecture v2 plan/CodeMemory/README.md>), [C# 파일 색인](<Docs/architecture v2 plan/CodeMemory/CSharpFileIndex.md>) | 2026-10-06 도메인 그룹·현재 주요 파일 경로에 맞춘 탐색 보조. 날짜가 붙은 과거 실행 기록은 당시 범위이며 현재 검증으로 합산하지 않는다. 전체 파일의 개별 내용 재감사 목록은 아니다. |
| [V2 계획](<Docs/architecture v2 plan/Architecture V2 Plan_0.2.md>), [구현 작업 목록](<Docs/architecture v2 plan/Architecture V2 Tasks.md>) | 설계 의도와 후속 작업. 체크 표시를 실행 증거나 현재 전체 구현으로 간주하지 않는다. |
| [Command/Event 규약](<Docs/architecture v2 plan/Architecture V2 Command Event Standard.md>) | 요청·결정·이벤트의 설계 배경. 현재 Producer/Consumer와 소비 시점을 함께 확인한다. |
| [Authoring/Prefab 규약](<Docs/architecture v2 plan/Architecture V2 Authoring Prefab Contract.md>) | 베이킹/스폰 책임 분리. 프리팹 누락 정책은 위 현재 코드의 도메인별 동작을 우선한다. |
| [연구건물 명세](Docs/Specifications/ResearchBuilding.md) | 확정된 연구 게임 규칙과 미확정 UI·밸런스를 구분한다. 현재 V2 연구 진행 구현의 증거는 아니다. |

CodeMemory 최상위의 레거시 문서 8개는 정리했다. 현재 계약은 컴포넌트 문서에, 미이식 게임 동작과 상충하는 과거 규칙은 구현 작업 목록의 해당 Task에 보존한다. 레거시 동작의 이관은 현재 V2의 확정 계약 변경이나 구현 승인이 아니며, 관련 영역을 실제로 변경할 때 필요한 절만 갱신한다.

## 기획 불명확성 및 확인

- 기능 구현에 필요한 기획이 부족하거나 선택지에 따라 게임 동작 또는 설계가 달라지는 경우 임의로 결정하지 않는다.
- 구현 전에 현재 코드, 이 문서, 위 적용 범위에 맞는 V2 설계/코드 지도와 명세에서 의도와 기존 규칙을 확인한다. 미이식 동작은 구현 작업 목록의 해당 Task에 보존한 레거시 검토 항목을 추가로 확인한다.
- 위 자료에서도 의도를 확인할 수 없으면 구현을 진행하지 말고, 동작이나 설계를 결정하는 선택지를 사용자에게 질문하여 확인받는다.
- 명확하게 정의되지 않은 게임 규칙이나 동작을 새로 추가하지 않는다.
- 기존 규칙 안에서 판단할 수 있는 일반적인 코드 구현 세부사항은 사용자에게 묻지 않고 진행하되, 기존 동작과 책임 경계를 유지한다.

## 변경 및 검증 시 주의

- Unity 버전은 `ProjectSettings/ProjectVersion.txt`, 패키지 버전은 `Packages/manifest.json`을 따른다.
- C# 또는 에셋을 변경한 경우에는 변경 범위에 맞는 Unity CLI 컴파일/재컴파일 확인을 수행한다. C# 변경은 크기와 무관하게 항상 확인한다. 읽기 전용 분석이나 문서만의 변경에는 새 컴파일을 실행하지 않는다. 비동기 명령은 제출만으로 완료로 간주하지 말고 최종 상태를 확인한다.
- 새 테스트는 실제 발견한 버그의 재현·재발 방지 또는 기존 테스트가 다루지 않는 핵심 게임 흐름을 검증할 때만 추가한다. 코드의 복잡성이나 변경량만으로 테스트를 추가하지 않는다.
- 새 테스트를 만들기 전에 관련된 기존 테스트를 확인한다. 같은 흐름의 기존 테스트에 필요한 assertion을 추가할 수 있으면 이를 우선하고, 동일한 실패를 잡는 테스트를 중복 생성하지 않는다.
- 단순 대입·조회, 컴포넌트 존재 확인, 코드 이동·이름 변경만을 위한 테스트, 구현을 그대로 따라 쓰는 검사, 통합 테스트가 이미 검증하는 정상 경로의 별도 테스트는 만들지 않는다. 핵심 흐름 검증에 필요한 상태 확인은 해당 통합 테스트 안에서 수행한다.
- 세부 단위 테스트는 소유권 손실·중복 생성·데이터 유실·계산 오류 등 중요한 실패를 통합 테스트로 재현하기 어려울 때만 예외적으로 작성한다. 정상·실패·경계 사례를 형식적으로 나열해 테스트를 늘리지 않는다.
- 새 테스트를 추가할 때는 잡으려는 구체적인 실패와 기존 테스트만으로 부족한 이유를 작업 설명에 짧게 밝힌다. 이 기준 안의 테스트 추가에 별도 승인을 반복해서 요청하지 않는다.
- 동작과 데이터 계약을 유지하는 이름 변경·파일 분리 등 단순 리팩터링은 기본적으로 컴파일만 확인한다. 동작·계약 변경이나 구체적인 회귀 위험이 있으면 영향을 받는 가장 작은 기존 EditMode 테스트 묶음을 실행한다. 관련성은 폴더·Phase 이름보다 실제 호출 관계와 데이터 계약으로 판단한다.
- 실제 플레이 동작, UI·입력, 시각적 결과 등의 수동 작동 테스트는 사용자가 직접 수행한다. 명시적으로 요청받지 않은 경우 Play Mode 기반 작동 검증을 수행하지 않는다.
- 관련 테스트 실패 또는 다른 시스템에 영향을 준다는 구체적인 근거가 있을 때만 검증 범위를 확대한다. 명시적인 요청 없이 변경과 무관한 검증을 추가하지 않으며, 전체 EditMode 테스트와 Play Mode 검증은 사용자가 명시적으로 요청한 경우에만 수행한다.
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
5. 위 기준에 따라 테스트 실행이 필요한 경우에만, 컴파일 확인 후 가장 작은 관련 테스트 필터를 지정해 실행한다. `<관련 테스트 클래스 또는 필터>`는 실제 대상 이름으로 바꾼다. 컴파일만으로 충분한 변경에는 이 단계를 생략한다.

   ```powershell
   & $unityCli command run_tests --mode editor --filter '<관련 테스트 클래스 또는 필터>' --async_tests true --project-path $planetMinerProject --format json
   & $unityCli command test_status --project-path $planetMinerProject --format json
   ```

6. 테스트도 최종 완료를 확인한다. 성공 시 필터와 Total/Passed/Failed/Skipped/Inconclusive 등 제공된 통계만 보고하고 전체 통과 목록·원본 로그를 다시 넣지 않는다. 실패 시 해당 테스트의 메시지와 필요한 스택을 확인한다. 수정 전 assertion이 실행되었다면 테스트 어셈블리 반영 여부부터 확인한다.
7. 추가 코드/에셋 변경, 검증 실패, 오래된 어셈블리 등 결과를 무효화하는 근거가 없다면 같은 컴파일·테스트를 반복하지 않는다. 새 변경이 있으면 그 변경 이후의 컴파일을 확인하고, 테스트 재실행 여부와 범위는 위 기준으로 판단한다. 완료된 결과를 다시 보고하려는 목적만으로 재실행하거나 전체 테스트로 확대하지 않는다.
