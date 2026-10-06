# 드론 관리 3단계: 실물 인계와 결과 처리

이 문서는 최초 3단계의 동일 틱 즉시 인계 당시 실행 기록이다. 이후 사용자 결정으로 이전 틱 물류 입력에 한정하고 Decision/Execution 계획과 최종 반영을 분리했다. 현재 시점·기능과 실행 근거는 [이전 틱 인계 검증](DronePreviousTickTransfer-Verification.md)을 따르며 아래 70개 통과 기록은 당시 증거로 보존한다.

## 변경과 결과

2026-10-05 승인된 3단계를 구현했다. 드론 행동 완료 신호를 받아 기존 아이템 엔티티를 수집·회수·현장 공급·보관·월드 방출하고 실제 수량·보관 버퍼·소유권·현장 예약을 정산한다. 인계 경합은 사용자 결정에 따라 **행동 신호 접수 순서**로 처리한다.

최종 Unity 컴파일은 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`다. 이번 최종 소스에서 선별 EditMode **70/70 통과**(신규 실물 인계 26, 기존 드론 배정 36, 공사 완공 8)이며 실패·건너뜀·Inconclusive는 0이다. 실제 드론 이동·충전·경로 계산·관측 원본 연결·자동 재탐색·바닥 완공 차단 갱신은 이번 단계에서 구현하지 않았다.

## 입력 접수와 결과 소비

- [DroneActionReceiptSequence](../../../../Assets/Scripts/Components/DroneLogistics/DroneActionReceiptSequence.cs)는 World의 전역 접수 순번 원본이다. 배정 내부의 Action.Sequence와 별도로 관리한다.
- [DroneActionRequestUtility.Submit](../../../../Assets/Scripts/Common/DroneActionRequestUtility.cs)은 주 스레드의 시뮬레이션 전 외부 입력 경계에서 순번을 발급하고 독립 요청 엔티티를 게시한다. 실제 드론 수행부의 호출자는 후속이다. 수신자가 Entity.Index를 접수 시각으로 추정하지 않는다.
- [DroneActionReadyRequest](../../../../Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs)의 ReceiptSequence로 인계 순서를 정한다. 행동·배정·예약 revision, 양수 행동 번호, 수행자 연결, 예상 행동·대상, 관측 revision 및 적재 품목을 확인한다. 거절된 잘못된 신호는 LastAppliedActionSequence를 전진시키지 않는다.
- 유효한 부분/0개 처리도 결과를 남긴다. Request 제거와 DroneItemTransferResult 추가는 같은 EndStateApply ECB에 기록한다. 재생 전 Apply를 다시 호출해도 같은 요청을 중복 처리하거나 결과를 덮어쓰지 않는다.
- 외부는 EndStateApply 이후 다음 입력 경계에서 TryConsumeResult로 결과를 읽고 결과 엔티티를 소비한다. Ready가 아직 남아 있으면 소비할 수 없다. 실제 수행부가 없다고 자동 소비한 것으로 처리하지 않으며, 결과가 남으면 기존 Lifecycle이 배정 삭제를 보류한다.

## 실제 인계와 상태 소유

당시 DroneItemTransferApplySystem은 메인 스레드에서 요청을 접수 순서대로 처리했다. 현재 이 처리는 [DroneTaskLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)과 [공통 ItemOwnership API](../../../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs)로 이관했다. 아래 검증은 이관 이전의 실행 근거이며 현재 구조는 [소유자 통합 기록](DroneTransferOwnerConsolidation-Verification.md)을 따른다. 당시 [DroneItemTransferUtility](../../../../Assets/Scripts/Common/DroneItemTransferUtility.cs)는 유효 소유권·실물 이동·예약 감소·도착량 적용을 공유했다.

| 행동 | 현재 적용 |
| --- | --- |
| CollectFromStorage | 허용 공급원의 현재 실물을 기존 AssignedQuantity·자기 예약·현장 부족량 범위에서 수집한다. 부족분 예약은 해제하며 0개면 연결을 해제하고 취소/재탐색 대상으로 남긴다. 연구 증가가 기존 배정 수량을 늘리지 않는다. |
| RecoverWorldItem | 현재 유효 월드 실물을 수집하고 Recovery 출처를 기록한다. SiteClearance는 인계 당시 최신 GridPosition과 현장 footprint를 다시 확인한다. DroneDrop은 현장 범위 조건을 적용하지 않는다. |
| SupplyConstructionSite | 현장 부족량과 자기 예약까지 실제 적재품을 인계하고 DeliveredQuantity를 증가시킨다. 예약을 정산하며 남은 실물은 드론에 보존하고 Retargeting으로 전환한다. Recovery 출처는 직접 공급할 수 없다. |
| StoreCargo | 현재 필터·슬롯·MaxStack 범위에 들어가는 실제 실물만 입고한다. 남은 수량과 출처는 보존하여 Retargeting으로 전환한다. |
| DropCargo | 관측 revision과 관측 xy의 격자 셀을 대조한 방출 위치에 기존 실물을 월드 아이템으로 되돌리고 DroneRecoveryPending을 기록한다. 실제 드론 위치/도착 검증의 SoT 연결은 TODO로 남는다. |

활성 Destroy 실물은 인계하지 않는다. 같은 틱 벨트 적용 뒤 활성 TransferOwnershipRequest가 있으면 TargetOwner를 유효 소유권으로 해석한다. 출발 버퍼 제거·도착 버퍼 추가·위치/벨트 상태 갱신 후 같은 Transfer 요청에 최종 Owner를 기록하며 ItemOwnership/렌더 태그는 기존 Ownership Apply가 반영한다. 실물 엔티티를 수량 데이터나 새 엔티티로 대체하지 않는다.

필터·입력 슬롯·MaxStack 정책은 기존 [DroneSchedulingUtility.FindStorageSlot](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs)을 확장해 배정과 실제 보관이 함께 사용한다. 실제 수집 후 AssignedQuantity는 수집한 양으로 축소할 수 있으며, 현재 적재량의 원본은 계속 StoredItemElement/ItemOwnership이다.

회수품이 드론에 적재되면 상위 월드 회수 대상은 무효/Closed가 될 수 있다. AssignmentNeedsCleanup은 원회수품이 수행자에게 실제 보관되고 Recovery 출처·StoreCargo·유효 보관 목적지를 유지하는 경우 정상 보관 이동을 중단하지 않는다. 별도 자동 재탐색은 아직 없다.

## 단계 순서와 ECB

```text
BuildingItemStorageApply / RoutingApply
    → DroneItemTransferApply
    → ItemOwnershipApply
    → ConstructionLifecycleApply
    → EndStateApply 구조 변경 재생
```

ConstructionLifecycleApplySystem의 기존 Before(BuildingLifecycleApplySystem)는 제거했다. 인계·Ownership 이후에 실행해 같은 틱 World 아이템 입고→수집→공급→완공에서 렌더 태그 추가가 자재 삭제보다 먼저 기록되게 한다. Ownership과 Building Lifecycle의 별도 상대 순서는 추가하지 않았다. Building/Item Lifecycle의 예정 월드 변경을 읽어 바닥 차단을 갱신하는 것은 4단계 후속이다.

## 실행 근거

Unity 6000.4.11f1의 연결 편집기에서 컴파일 후 테스트 클래스를 순차 실행했다. 원본 출력은 프로젝트의 `Logs/Codex/DroneItemTransferStep3-20261005/`에 보관한다.

| 확인 | 최종 결과 | 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile-final.json`, `recompile-status-final.json` |
| DroneItemTransferTests | 26/26, 실패/건너뜀/Inconclusive 0 | `transfer-run.json`, `transfer-status.json` |
| DroneTaskSchedulingTests | 36/36, 실패/건너뜀/Inconclusive 0 | `scheduling-run.json`, `scheduling-status.json` |
| Phase7ConstructionCompletionTests | 8/8, 실패/건너뜀/Inconclusive 0 | `completion-run.json`, `completion-status.json` |
| 편집기 | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 어셈블리·메타 | 런타임/Editor DLL 최신, ECS 99개·색인 누락 0, 신규 메타 GUID 각각 1개 | `freshness-and-meta.json` |
| 문서·차이 검사 | 문서 10개·로컬 링크 523개 누락 0, git diff --check 종료 0, 대상 소스 공백/충돌 0 | `final-static-check.json`, `diff-check.txt` |

LF→CRLF 안내 12건이 있으며 공백 오류는 없다. 기존 staged/unstaged 작업은 유지했고 이번 작업에서 스테이징하거나 커밋하지 않았다.

초기 컴파일의 CS1654/CS1612는 using NativeArray 및 임시 DynamicBuffer의 setter 사용을 수정해 해결했다. 초기 오류를 최종 성공 결과와 구분한다. 테스트 실행은 최종 컴파일 이후이며 이번 70개는 이전 결과를 합산한 수치가 아니다.

신규 사례는 실물·Owner·렌더·예약/도착량 보존, 접수 순서 재고/공간 경합, 부분·0개 처리, 잘못된 revision/sequence/target/연결, 중복·재진입, 활성 Destroy, 같은 틱 유효 Transfer, 연구 증가의 비소급, 회수 후 정상 보관, 미소비 결과 보호와 소비 후 정리를 확인한다. 같은 틱 World 원본의 벨트 입고→수집→공급→최종 완공과, 요청 뒤 현장 밖으로 이동한 회수품 거절도 포함한다.

## 범위와 후속

테스트는 명시적인 ECS 테스트 프리팹 DB·수행자·도착/행동 신호로 실행한다. 실제 SubScene 베이킹, 실제 드론 생성·이동·경로/충전, 입력·화면·성능, 전체 EditMode와 Play Mode는 검증하지 않았다. 신호의 관측 일치 검사가 실제 위치/도착 검증을 대신하지 않는다.

다음 4단계는 취소·완공 후 운반품의 자동 재배정, 목적지 탐색, 현재 틱 월드 방출/환급 기록을 포함한 AwaitingItemClearance 갱신이다. 이번 완공 테스트는 기존 현장 플래그가 허용하는 범위의 동일 틱 자재 소비를 확인하며 바닥 차단 갱신 전체를 증명하지 않는다.

현재 계약은 [드론 문서](../../../CodeMemory/Components/DroneLogistics.md)를 따른다. 이 검증 당시 확정 기획과 후속 순서는 당시 구현 계획을 기준으로 했다. 위치 SoT 연결 공백의 당시 검토는 [스냅샷 검토](DroneSnapshotSotReview.md)에 보존한다.
