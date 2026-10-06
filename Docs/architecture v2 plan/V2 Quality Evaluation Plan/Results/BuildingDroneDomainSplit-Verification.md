# 건물·드론 도메인 실행 그룹 분리 검증

작성일: 2026-10-06. 사용자 확정 `1A, 2A, 3A, 4B, 5A, 6A`와 현재 소스 변경을 기록한다. 소스 확인과 연결된 Unity Editor의 컴파일·선택 EditMode 실행 결과를 구분한다.

## 승인된 동작

| 결정 | 적용 계약 |
| --- | --- |
| 1A | 건물 종료의 입출고·생산 실물 생성·철거 반환/환급·건물 생성/삭제를 같은 틱 드론 입력으로 공개한다. 기존 공급원 종류·품목 제한을 유지한다. |
| 2A | 각 도메인을 한 번 실행한다. 건물 입고 재료로 재차 생산 판단하지 않고 드론 적용 중 새 수집품·새 공간을 준비된 계획에 추가하지 않는다. |
| 3A | 작업·경로·배정은 EndSimulation에 공개하고 다음 틱부터 이용한다. 행동 결과와 요청/계획 정리도 이 최종 경계를 사용한다. |
| 4B | 공사 완공은 BuildingStateApply 마지막에서 판단한다. 이번 틱 드론의 마지막 납품·방해물 회수는 다음 틱 완공에 반영된다. 완공 건물은 같은 틱 드론에 보이나 자체 동작은 다음 틱부터다. |
| 5A | Ready/Fatal은 GameSimulationGroup 틱 시작에만 검사한다. 중간 EndBuilding의 Fatal 확정 이후에도 현재 틱을 완주하며 다음 틱부터 차단한다. 전체 rollback은 보장하지 않는다. |
| 6A | 그룹·ECB 경계, 후보/공개 대기 예약 분리·이름·도메인 폴더와 직접 영향을 받는 현재 문서·테스트를 함께 변경한다. |

## 소스 계약

```text
Command / EndCommand
  → BuildingSimulation(Decision → Reservation → Execution → StateApply·마지막 완공)
  → EndBuilding
  → DroneSimulation(Decision → Reservation → Execution → StateApply)
  → SimulationCommit / EndSimulation
  → Synchronization
```

- Building/Drone의 하위 그룹은 각각 자기 도메인의 페이즈에 등록한다. 최상위 형제 그룹의 순서와 내부 페이즈 순서를 구분하며 그룹 순서만으로 Job 완료·NativeContainer 의존성이 해결된다고 해석하지 않는다.
- EndBuilding은 BuildingSimulationGroup의 OrderLast 직접 자식이다. Construction은 BuildingStateApply의 OrderLast이므로 완공 기록 뒤에 건물 결과를 확정한다.
- Synchronization은 GameSimulationGroup의 OrderLast이며 SimulationCommit 이후다. 드론/Commit을 생략한 건물 전용 검증 World에서도 공간 동기화가 건물 뒤에 실행된다.
- 드론 실물 인계는 기존 ItemOwnershipApplySystem.TryTransferItem의 버퍼·Owner·좌표·벨트·렌더 동시 반영을 유지한다. Lifecycle은 성공 수량으로 배정·적재 출처·DeliveredQuantity·개별 예약/합계·행동 번호·결과를 정산한다. 새 TransferOwnershipRequest를 생성하지 않는다.
- DroneTaskCandidateDecisionElement는 Decision이 매 틱 다시 만드는 순수 후보다. 별도 DroneTaskPendingPublicationElement는 Candidate 스냅샷·Quantity·CommittedQuantity·PublicationQueued를 보관한다. 공개 기록과 미공개 예약은 다음 Reservation이 정산한다. PublicationQueued는 최종 ECB 기록 완료이며 실제 공개 완료가 아니다.
- 공급원 재고·보관 공간의 영속 예약은 추가하지 않는다. 드론 계획은 건물 결과 확정 후 전체 요청에 대해 먼저 준비하고 접수 순서·현재 원본 재검사·성공분 공간 예산 소비를 유지한다.
- 현장 내부의 새 World Spawn·드론 방출 금지와 외부 평가자가 제공하는 안전 방출 위치 계약을 유지한다. 예정 위치 저널은 없다. 공간 인덱스는 중간에 재구축하지 않고 최종 Synchronization에서 갱신한다.
- ECS 정의는 IComponentData/IBufferElementData/IRequestComponent/IEnableableRequest를 포함해 103개다. 드론 폴더의 22개와 현장 개별 예약 ConstructionSupplyReservation 1개가 드론 계약 23개다. DroneActionIdentity 등 일반 구조체와 그룹·ECB 시스템은 이 수에 포함하지 않는다.

소스 진입점: [GameSimulationGroup](../../../../Assets/Scripts/Phases/GameSimulationGroup.cs), [BuildingSimulationGroup](../../../../Assets/Scripts/Phases/Buildings/BuildingSimulationGroup.cs), [DroneSimulationGroup](../../../../Assets/Scripts/Phases/Drones/DroneSimulationGroup.cs), [건물 ECB](../../../../Assets/Scripts/Systems/Buildings/StateApply/EndBuildingEntityCommandBufferSystem.cs), [최종 ECB](../../../../Assets/Scripts/Systems/Commit/EndSimulationEntityCommandBufferSystem.cs), [완공](../../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [후보](../../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCandidateDecisionElement.cs), [공개 대기 예약](../../../../Assets/Scripts/Components/DroneLogistics/DroneTaskPendingPublicationElement.cs).

## 실행 검증

| 항목 | 상태 | 근거 |
| --- | --- | --- |
| 현재 소스의 그룹·ECB 등록, 후보/공개 대기 수명·ECS 정의 수 | Source-reviewed | 현재 파일의 선언·그룹 속성·데이터 필드를 확인했다. |
| Unity 컴파일 | Passed | 최종 수정 후 recompile_status=completed, failed=false, errors=[]; Editor ready, compiling=false, domainReloadInProgress=false. 제품/테스트 어셈블리 모두 최신 소스보다 새롭다. |
| 영향을 받는 EditMode 회귀 | Passed | 최종 어셈블리에서 13개 클래스 181/181. Failed/Skipped/Inconclusive=0. 전체 EditMode가 아닌 선택 필터 실행이다. |
| Play Mode의 V2 장면·베이킹 DB·도메인 처리 | Passed, controlled inputs | 사용자 일회 허용으로 실제 Default World와 Player Loop에서 아래 인계·완공·오류 차단을 확인했다. 수행자·관측·경로·행동 신호는 검증 입력이다. |
| 실제 드론 이동·경로 계산·관측/행동 Producer | Inconclusive | 미구현 수행부를 임시 입력으로 대체했으며 자동 비행·실제 도착·게임 입력의 증거는 아니다. |
| 성능 | Sampled, limited | 유휴 장면의 실제 그룹/ECB 마커 326개를 측정했다. 대규모 부하·구조 변경 집중 프레임·변경 전후 비교는 미측정이다. |

기존 [142사례](DroneSafeDropAndJournalRemoval-Verification.md)와 [통합 4사례](DroneLifecycleIntegration-Verification.md)는 이전 공통 여섯 phase의 별도 실행 기록으로 보존한다. 새 구조의 실행 수치로 합산하거나 입력·완공 시점을 소급 해석하지 않는다.

### 원본 실행 근거와 복구

원본 JSON은 `Logs/Codex/BuildingDroneDomainSplit/`에 보존했다. `compile-status.json`, `editor-status.json`, `assembly-freshness.json`과 각 필터의 `*-start.json`/`*-status.json`이 개별 실행 근거다. `selected-test-summary.json`은 아래 최종 실행 통계이며 CLI 경고는 0개다.

| 선택 필터 | Total | Passed | Failed | Skipped | Inconclusive |
| --- | ---: | ---: | ---: | ---: | ---: |
| Phase7EndToEndConstructionPipelineTests | 4 | 4 | 0 | 0 | 0 |
| DroneTaskSchedulingTests | 36 | 36 | 0 | 0 | 0 |
| DroneItemTransferTests | 33 | 33 | 0 | 0 | 0 |
| DroneRetargetingTests | 17 | 17 | 0 | 0 | 0 |
| DroneLifecycleIntegrationTests | 6 | 6 | 0 | 0 | 0 |
| ConstructionClearanceTests | 13 | 13 | 0 | 0 | 0 |
| Phase3StorageOwnershipTests | 4 | 4 | 0 | 0 | 0 |
| Phase4EndToEndPipelineTests | 3 | 3 | 0 | 0 | 0 |
| Phase6EndToEndPipelineTests | 3 | 3 | 0 | 0 | 0 |
| Phase7ConstructionCompletionTests | 8 | 8 | 0 | 0 | 0 |
| Phase7BuildingDemolishTests | 19 | 19 | 0 | 0 | 0 |
| Phase7ItemCreationDemolitionTests | 16 | 16 | 0 | 0 | 0 |
| Phase5CrafterInputPipelineTests | 19 | 19 | 0 | 0 | 0 |
| **합계** | **181** | **181** | **0** | **0** | **0** |

최초 실행에서 Phase7EndToEndConstructionPipelineTests의 3개 공간 인덱스 assertion이 실패했다. 해당 fixture는 건물 그룹만 등록해 Commit이 없었고, Synchronization의 UpdateAfter(Commit)만으로는 건물 이후 순서가 보장되지 않았다. Synchronization에 최상위 OrderLast를 명시한 뒤 재컴파일하고 위 선택 클래스를 모두 다시 실행했다. 최초 실패는 `Phase7EndToEndConstructionPipelineTests-initial-failure.json`과 `initial-test-summary.json`에 별도로 보존했다.

신규 통합 2사례는 실제 정렬 루트에서 건물 입고 직후 수집, 건물 출고 직후 열린 공간의 보관을 검증한다. 경로 결과·관측·행동 신호와 입출고 결정은 테스트 입력이며 실제 비행·입력 Producer의 증거는 아니다. 기존 통합의 실제 완공 흐름은 공급 틱 현장 생존 → 다음 건물 틱 완공 → 같은 틱 드론 예약 정산/Retargeting을 확인한다.

기본 샌드박스에서는 Unity 발견 파일 읽기 권한 오류가 발생했으나 권한을 높인 동일 프로젝트 연결로 복구했다. 기존 Verify-Unity.ps1의 CompileOnly는 모드 판정 오류로 컴파일을 실행하지 못하여, 연결된 CLI의 recompile/recompile_status/editor_status/run_tests/test_status를 순차 사용했다. 검증 래퍼는 수정하지 않았다. 최종 git diff --check 오류는 0이며 이동된 소스/그룹 범위의 중복 GUID는 0이다.

## 현재 문서

[AGENTS.md](../../../../AGENTS.md), [건설 명세](../../../Specifications/ConstructionAndDroneSupply.md), [컴포넌트 색인](../../../CodeMemory/Components/README.md), [드론](../../../CodeMemory/Components/DroneLogistics.md), [공사](../../../CodeMemory/Components/Construction.md)와 이동된 시스템을 가리키는 관련 컴포넌트 문서 링크를 갱신했다.

## 일회 Play Mode 실행 검증

사용자는 이번 요청에 한해서 직접 Play Mode 검증을 허용했다. 연결된 Unity 6000.4.11f1 Editor의 `Assets/Scenes/V2 Test Scene.unity`와 자동 로드된 `sub.unity`에서 실행했으며 빌드 진입 장면인 SampleScene 또는 Player 빌드는 검증하지 않았다. 프로젝트 소스·설정·장면을 변경하거나 저장하지 않았다.

초기 `editor_status`는 playing/paused로 표시했지만 실제 `EditorApplication.isPlaying=false`, `isPaused=true`와 World.All에 Game World가 없는 상태였다. 실제 API를 기준으로 Play Mode를 시작하고 종료 후 같은 중지/일시정지 플래그를 복원했다. 장면 2개는 전후 모두 dirty=false였다.

### 자연 실행과 실제 입력 범위

- 실제 Default World(Game)에 PrefabDatabaseReady=1, SimulationFatalError=0, ItemRegistry/RecipeRegistry/BuildingConfig 각 1개가 있었다. 베이킹된 DB의 창고·벨트·철 프리팹을 제품 생성 경로로 이용했다.
- 실제 등록 순서는 Command → BuildingSimulation → DroneSimulation → SimulationCommit → Synchronization이었다. Building 내부 네 페이즈와 EndBuilding, Drone 내부 네 페이즈도 런타임에서 조회했다.
- 자연 실행에는 DroneCapacityState와 DroneWorker가 0개였다. 공통 용량 2, 수행자 1개, 현장과 요청은 검증용 런타임 입력으로 준비했다. 외부 경로 결과·관측 위치·행동 신호도 직접 제공했다. 새 초기화·수행부를 구현한 것이 아니다.
- 실제 Game World를 일시정지하고 EditorApplication.Step으로 Player Loop를 진행했다. 테스트 전용 World, NUnit 실행 또는 개별 제품 시스템의 수동 Update로 대체하지 않았다.

| 런타임 확인 | 실제 관측 |
| --- | --- |
| 새 작업의 이용 시점 | 현장 작업은 실제 생성 시스템/최종 ECB에 공개됐지만 생성 틱의 수행자 배정은 Null이었다. 다음 판단·경로 결과 입력 뒤 제품 시스템이 배정과 예약을 공개했다. |
| 같은 틱 새 실물 수집 | 기존 재고를 검증 준비 단계에서 분리하고, 같은 틱 Storage Spawn 요청과 Collect 신호를 넣었다. EndBuilding에서 생성된 새 베이킹 실물의 Collect 결과는 Completed/1이었다. |
| 드론 내부 연쇄 제한 | 수집과 함께 미리 넣은 Supply 신호는 Rejected였고 현장 DeliveredQuantity는 0이었다. 새로 수집한 실물을 같은 틱 공급하지 않았다. |
| 공급과 다음 틱 완공 | 이후 정상 Supply는 Completed/1, Delivered=1, Reserved=0, cargo=0이었다. 공급 틱에는 현장과 실물이 남았다. 다음 Building 틱에 베이킹된 완공 창고가 생성되고 현장/자재가 삭제됐다. |
| 같은 틱 출고 공간 보관 | 실제 GroundRecovery 작업·배정·회수로 원실물을 적재했다. 검증 준비로 목적 창고 1슬롯을 실제 MaxStack=100개로 채우고 Store 신호를 넣었다. 제품 벨트 출고가 1개를 월드로 내보낸 같은 틱에 Store=Completed/1, 최종 보관 100개, cargo=0, 회수 원실물 Owner=창고를 확인했다. |
| 중간 오류의 현재 틱 완주 | 임시 Fatal 표식을 EndBuilding ECB에 지연 기록했다. 오류 실체화 후에도 그 틱의 Drone/Commit/Sync LastSystemVersion이 갱신됐다. |
| 다음 틱 오류 차단 | 다음 Step에서 Building/Drone/Commit/Sync 버전이 모두 유지돼 해당 도메인 처리가 차단된 것을 확인했다. 실제 잘못된 프리팹 오류를 발생시킨 검증은 아니다. |
| 정합성과 로그 | WorldInvariantValidationSystem 위반은 0이었다. 초기 콘솔 cursor/session 이후 새 Error는 0개였다. 새 Warning 1개는 초기 상태 판별 중의 “Cannot pause - Editor is not in play mode”로 도구 조작 경고다. |

### 유휴 성능 샘플

실제 Scripts 카테고리의 `Default World ...` 마커를 ProfilerRecorder로 수집했다. 자연 실행 상태의 326개 샘플이며 도구 입력·동적 평가가 동작한 Editor 환경이다. 당시 수행자/시설 작업이 없는 작은 장면이므로 활성 물류·대규모 공장 성능으로 일반화하지 않는다.

| 실제 마커 | Samples | 평균 ms | 최대 ms |
| --- | ---: | ---: | ---: |
| GameSimulationGroup | 326 | 1.0435 | 1.7351 |
| BuildingSimulationGroup | 326 | 0.4380 | 0.7025 |
| DroneSimulationGroup | 326 | 0.2502 | 0.4642 |
| SynchronizationGroup | 326 | 0.2085 | 0.4828 |
| EndBuildingEntityCommandBufferSystem | 326 | 0.0261 | 0.0493 |
| EndSimulationEntityCommandBufferSystem | 326 | 0.0142 | 0.0360 |

상위 그룹 시간은 하위 마커와 겹치므로 합산하지 않는다. 이 값은 시스템 업데이트 마커 시간이며 전체 프레임/GPU 또는 모든 병렬 Job의 총 CPU 시간이 아니다. 유휴 ECB 시간은 구조 변경이 많은 프레임의 동기화 비용이나 이전 구조 대비 증가량을 입증하지 않는다.

### 원본 근거와 복원

원본은 `Logs/Codex/BuildingDroneDomainSplitPlayMode/`의 `play-started.json`, `profile-start-and-config.json`, `idle-performance.json`, `task-created-next-tick-contract.json`, `same-tick-collection-and-chain-result.json`, `supply-tick-result.json`, `next-building-tick-completion.json`, `output-space-store-result.json`, `fatal-current-tick-result.json`, `fatal-next-tick-result.json`, `final-error.json`, `final-warn.json`, `state-restored.json`에 보존했다. 입력 평가 코드도 각 CLI 응답 parameters.code에 남아 있다.

동적 평가 준비/복원 과정에서 버전별 그룹 API 이름, 잘못 지정한 조회 타입, using 변수의 ref 제약, World.All 무할당 컬렉션의 boxing 제약 오류가 있었다. 조회/검증 코드만 고쳐 재시도했으며 제품 소스의 Unity 컴파일 실패와 구분한다. `output-space-store-input-probe-error.json`과 `state-restored-probe-error.json`에는 해당 도구 실패 원본을 별도로 보존했다.

Game View를 직접 캡처해 V2FloorBiomePreview의 진단 색상 텍스처를 확인했다. [캡처 원본](../../../../Logs/Codex/BuildingDroneDomainSplitPlayMode/game-view.png)은 실제 드론 비행·UI 입력·실제 바닥 청크 렌더링의 증거가 아니다. 캡처 도구가 Assets 아래 만든 임시 PNG/메타는 Logs로 이동·정리하고 AssetDatabase를 새로고침했다.

Play Mode 종료 뒤 실제 API에서 playing=false, paused=true, Game World=0, 두 장면 dirty=false, 측정기 해제와 임시 참조 제거를 확인했다. 저장된 장면·프리팹·설정·C# 파일은 변경하지 않았다. 이후 작업의 Play Mode 자동 실행 허용으로 확대하지 않는다.

## 문서 정확성 점검 (2026-10-06)

후속 사용자 요청으로 현재 계약 문서와 실제 소스의 정의·Writer/Reader·그룹·ECB 시점을 대조했다. [문서 안내](../../../../Docs/README.md)를 추가하고 현재 설명과 과거 설계/실행 기록의 읽는 순서를 명확히 했다.

- AGENTS·명세·컴포넌트·V2 코드 지도/파일 색인 등 현재 문서 17개에서 로컬 파일 링크 871개를 확인했고 끊어진 파일 연결은 0개다. 링크의 모든 줄 앵커를 재감사한 결과를 뜻하지 않는다.
- 주요 C# 파일 색인 148개 경로는 현재 존재하며 이전 시스템 경로 38개와 새 그룹/ECB/공개 대기 예약 설명을 갱신했다. 전체 199개 C# 파일 본문의 개별 재감사가 아닌 주요 경로 색인이다.
- 컴포넌트 정의와 색인 이름 103개가 일치하고 드론 계약 23개를 확인했다. 드론의 저장/Owner/좌표/벨트 상태 Writer, 레시피 잔여물/Product Writer, 취소/철거 반환 Writer의 누락을 보완했다.
- PendingPublication을 분리한 이유, 후보/선택/최종 공개 수량, PublicationQueued와 실제 ECB 공개의 차이, 공개 후 예약 중복 합산 방지와 해제 예정량의 읽기 계산을 명확히 했다.
- 이전 CodeMemory·설계/로드맵·피드백·리팩토링 기록은 본문과 과거 건수를 보존하고 현재 V2 구현과 구분하는 안내를 추가했다. 과거 Results의 실행 결과를 현재 구조로 소급 수정하지 않았다.

C# 11개 파일에서는 XML/행 주석의 EndStateApply 이름을 실제 EndBuilding/EndSimulation으로 정정했다. 주석을 제외한 코드 차이는 0이며 `Logs/Codex/BuildingDroneDomainSplitDocumentationAudit/comment-only-check.json`에 비교 결과를 남겼다. 이후 Unity 재컴파일은 completed/failed=false/errors=[]이며 제품 어셈블리는 최신 주석 소스 이후 생성됐다. 테스트 소스는 변경하지 않았다.

이번 점검에서는 EditMode/Play Mode를 재실행하지 않았다. 위 181/181과 제어 입력 Play Mode는 앞선 도메인 구조 변경의 실행 기록이며, 주석 수정 후 새 테스트 실행 수치로 표시하지 않는다. 컴파일 상태·Editor 실제 상태·파일 링크 검사 원본은 `Logs/Codex/BuildingDroneDomainSplitDocumentationAudit/`에 보존했다. git diff --check 오류는 0이며 LF→CRLF 변환 안내는 남아 있다. 기존 staged 변경은 유지하고 새 문서/주석 수정은 작업 트리에 두었다.
