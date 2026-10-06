# 기존 공사 자재 운송 제거 검증 기록

후속 상태: 현장 취소는 [Command 이동 작업](ConstructionCancelCommand-Verification.md)에서 옮겼다. 이 문서의 실행 결과와 당시 후속 범위는 원래 검증 시점을 보존한다.

이 기록은 운송 제거 직후의 실행 근거다. 이후 [ConstructionSite.Progress 제거](ConstructionProgressRemoval-Verification.md)를 별도 변경으로 완료했으며, 아래 Progress 유지/후속 범위 설명은 당시 상태로 보존한다.

## 결과와 범위

2026-10-04 사용자가 승인한 기존 공사 자재 운송 제거 범위를 구현했다. 운송 전용 데이터·요청·등록·수령·예약 정산·결과 및 전용 테스트/헬퍼를 제거하고, 현장 취소·완공과 일반 아이템 소유권·철거 보호를 보존했다.

- Unity 재컴파일: `completed`, `failed:false`, 오류 0.
- 관련 5개 EditMode 클래스: **41/41 통과**, Failed/Skipped/Inconclusive 0.
- 프로젝트 ECS 선언과 컴포넌트 상세 문서: **79/79**, 누락/추가 항목 0.
- 이 결과는 새 드론 공급/회수의 구현 또는 실행 성공을 뜻하지 않는다. 현재 제품 코드의 공사 자재 수령·등록·예약 경로는 없다.

## 제거한 코드

| 위치 | 변경 |
| --- | --- |
| `Assets/Scripts/Components/Construction/ConstructionMaterialDeliveryComponents.cs` 및 .meta | 운송·결과 ECS 2개와 상태/Outcome enum 2개를 파일과 함께 제거 |
| [ConstructionRequests.cs](../../../../Assets/Scripts/Components/Construction/ConstructionRequests.cs) | SupplyConstructionMaterialRequest/CancelConstructionMaterialDeliveryRequest 제거, CancelConstructionRequest 유지 |
| [ConstructionLifecycleApplySystem.cs](../../../../Assets/Scripts/Systems/5_StateApply/ConstructionLifecycleApplySystem.cs) | 수령·선점 수집·등록·운송 취소·종료 Job 5개, Operations, 전용 쿼리/Lookup, 철거 스냅샷, claims/CompletedSites 컨테이너 제거 |
| [ItemRequests.cs](../../../../Assets/Scripts/Components/Items/ItemRequests.cs), [ItemOwnershipApplySystem.cs](../../../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs) | 공사 운송만 읽던 ProcessedInStateApply와 설정·ECB 초기화 제거. 일반 Transfer 및 Destroy/철거 버퍼 보호 유지 |
| [ConstructionComponents.cs](../../../../Assets/Scripts/Components/Construction/ConstructionComponents.cs) | 현재 사용하지 않는 Progress 및 도착/예약량 Writer 부재를 주석에 명시. 필드 구성은 유지 |

취소 Job → 완공 Job 순서는 유지한다. 성공한 완공 Spawn 이후에만 현장과 실물 삭제를 기록하고, 실패하면 현장·자재를 보존한다. 취소는 아직 StateApply이며 EndStateApply에서 실물을 반환하고 현장/요청을 삭제한다.

## 테스트 변경과 실행 근거

기존 운송 계약 전용 Phase7ConstructionMaterialTests와 .meta를 제거했다. 해당 파일의 운송 전용 15개 메서드/24개 선언 케이스, 별도 취소/공급 경합 1개와 실패 보존의 같은 틱 공급 분기 1개는 제거 범위다. 삭제와 Transfer의 ECB 경합 2개는 기존 Phase1ItemIntegrationTests로 이동해 보존했다.

완공·취소·통합 테스트는 도착한 실물, Owner/보관 버퍼 및 DeliveredQuantity를 직접 준비하도록 바꿨다. 기존 정상 물류·철거·취소·완공·생성 실패 보존을 검증하며 자재 운송은 검증하지 않는다. F-004의 일반 입고와 철거 실물 반환 회귀도 보존했다.

| 필터 | Total | Passed | Failed | Skipped | Inconclusive |
| --- | ---: | ---: | ---: | ---: | ---: |
| Phase1ItemIntegrationTests | 13 | 13 | 0 | 0 | 0 |
| Phase7BuildingDemolishTests | 13 | 13 | 0 | 0 | 0 |
| Phase7ConstructionCompletionTests | 8 | 8 | 0 | 0 | 0 |
| Phase7ConstructionCancelTests | 3 | 3 | 0 | 0 | 0 |
| Phase7EndToEndConstructionPipelineTests | 4 | 4 | 0 | 0 | 0 |
| 합계 | 41 | 41 | 0 | 0 | 0 |

연결된 Unity **6000.4.11f1** Editor에서 `run_tests --mode editor --filter <각 클래스> --async_tests true`를 순차 실행하고 `test_status`의 최종 completed 결과를 확인했다. 테스트 시작 응답의 0개 Summary는 실행 중 상태였으며 최종 결과와 구분했다.

### 최초 실패와 수정

최초 Phase1 실행은 13/13 실패했다. 반환된 예외는 GatherComponentDataJob의 DemolishBuildingRequest 핸들이 ECB 재생 전 완료되지 않았다는 내용이었다. Editor 원본 로그를 확인한 실제 선행 원인은 Ownership Job의 `in TransferOwnershipRequest`와 `EnabledRefRW<TransferOwnershipRequest>`에서 같은 컴포넌트의 RO/RW 핸들이 생성된 aliasing 예외다. 스케줄이 중단되어 뒤의 Producer 등록에 도달하지 못했다.

기존 `RefRW<TransferOwnershipRequest>` 접근 계약을 복원했고, 처리 표시 필드/ECB 초기화 제거는 유지했다. 수정 후 재컴파일 완료를 확인한 뒤 Phase1을 재실행하여 13/13 통과했고 나머지 네 클래스도 통과했다. 전역 Complete 호출이나 단계 순서 변경으로 우회하지 않았다.

## 원본 실행 기록

프로젝트의 `Logs/Codex/ConstructionTransportRemoval-20261004/`에 원본 CLI 응답을 보존했다.

- [최종 재컴파일 상태](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/recompile-status-final.json)
- [최초 실패](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/Phase1ItemIntegrationTests-status.json), [alias 로그 발췌](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/ownership-alias-excerpt.txt)
- [Phase1 수정 후 결과](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/Phase1ItemIntegrationTests-retry-status.json)
- [철거 결과](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/Phase7BuildingDemolishTests-status.json)
- [완공 결과](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/Phase7ConstructionCompletionTests-status.json)
- [취소 결과](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/Phase7ConstructionCancelTests-status.json)
- [통합 결과](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/Phase7EndToEndConstructionPipelineTests-status.json)
- [어셈블리 최신성](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/assembly-freshness.json), [Editor 실제 모드](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/editor-mode-check.json), [요약](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/summary.json)

최종 런타임·Editor 테스트 어셈블리가 각 최신 C# 소스보다 최신임을 확인했다. 테스트 후 Editor 상태가 paused로 표시되어 실제 EditorApplication을 읽었으며, `isPlaying:false`, `isPlayingOrWillChangePlaymode:false`, `isCompiling:false`, `isUpdating:false`였다. 실제 Play Mode 검증을 수행한 것으로 간주하지 않는다.

Edit Mode에 남은 pause 플래그만 복원한 뒤 [최종 Editor 상태](../../../../Logs/Codex/ConstructionTransportRemoval-20261004/editor-status-restored.json)가 ready/stopped, compiling:false, domainReloadInProgress:false임을 확인했다.

## 남은 구현과 검증 한계

- ConstructionSite.Progress 필드 제거와 취소의 Command 이동은 별도 후속 변경이다.
- 건물 파괴 요청 이름/의존성 정리와 새 드론 공급·예약·재배정·회수는 구현하지 않았다.
- 현장의 Required/Delivered/Reserved 데이터와 StoredItemElement는 남아 있으나 현재 런타임 자재 수령·도착/예약량 갱신 API는 없다.
- 전체 EditMode, Play Mode, 실제 SubScene 베이킹·프리팹, 입력·시각·성능은 검증하지 않았다.
- F003/F004/F037 기존 검증 문서는 당시 소스의 실행 기록으로 보존한다. 현재 제거 이후 계약은 [AGENTS.md](../../../../AGENTS.md), [컴포넌트 색인](../../../CodeMemory/Components/README.md), [합의 명세](../../../Specifications/ConstructionAndDroneSupply.md)를 따른다.
