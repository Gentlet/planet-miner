# F-003 구현 및 검증 기록

- 날짜: 2026-10-02
- [Task](../V2%20Quality%20Improvement%20Tasks.md#f-003--공사-자재의-중복-공급과-기존-소유-버퍼-검증-누락) · 최초 평가 [Q02](Q02.md) · 수령 평가 [Q24](Q24.md)
- 상태: 확정 정책 구현, 후속 리뷰 2건 보완 및 중복 방어 정리 완료. 초기 EditMode 43/43, 후속 변경 관련 EditMode 47/47, 중복 방어 정리 후 자재 테스트 26/26 확인.

## 확정 정책

공사 자재 공급원은 Storage, DroneStation, MainFacility의 보관 자재다. 다른 공사 현장의 도착 자재와 Miner/Crafter 생산품 직접 인수는 허용하지 않는다. 세 공급원은 공통 생성 경로에서 항상 Storage/Stored 버퍼를 갖는다. MainFacility 설정 용량 50은 유지하고 DroneStation 설정은 기존 저장 기본값 20을 사용한다. 로더는 세 공급원의 용량을 1~MaxStorageSlots로 검증한다. 설정 없는 직접 생성의 기존 기본 용량 20도 유지한다.

정상 인수는 이전 보관자의 버퍼 제거, 현장 등록, Delivered/Progress, 해당 운송의 예약 종료, Owner/렌더 태그를 한 승인 단위로 반영한다. 일반 최종 거부는 실물을 변경하지 않고 그 운송의 활성 예약만 해제한다. 무예약 공급은 다른 운송의 예약을 소진하지 않는다. F-004의 유효한 Destroy/철거 반환 충돌은 수령을 거부하고 활성 예약을 보존하며 자동 재시도하지 않는다. 현장 자체의 취소/완공/삭제 및 명시적인 운송 취소는 별도의 예약 종료 원인이다.

## 공개 입력과 상태 소유권

`ConstructionMaterialDelivery` 엔티티가 운송 식별자다. Producer는 TargetSite/SourceBuilding/ItemEntity/ItemType/ReserveMaterial을 생성 시 지정하고 이후 수정하지 않는다. 초기 State는 PendingRegistration, ReservationActive는 false다. 기존 ConstructionLifecycleApplySystem이 명시된 운송의 공급원/실물/요구량을 검증하여 등록하고 필요하면 예약을 1 증가시킨다. 공급원 탐색, 자동 작업 배정, 운송 출발 판단은 구현하지 않는다. 실제 운송 Producer/드론 시뮬레이션은 여전히 미구현이다.

`SupplyConstructionMaterialRequest(Delivery, ExpectedOwner)`는 도착 시 실제 보관자를 명시한다. SourceBuilding은 등록 당시 허용된 공급원이며 운송 중 Owner와 구분한다. 다른 허용 건물 또는 비건물 보관자의 Stored 버퍼에서 인수할 수 있다. 등록 후 정상적으로 월드로 방출된 실물도 받을 수 있지만 원래 공급원에 참조가 남아 있거나 활성/같은 StateApply에서 처리된 Transfer가 있으면 거부한다. 운송자가 중간 인계의 Owner/버퍼를 일치시키는 기존 공개 계약은 유지하며 전역 잔류 버퍼 자동 보정은 하지 않는다.

State/ReservationActive 및 현장 예약 합계는 공사 시스템의 Job만 변경한다. 내부 State는 중복 재실행을 막기 위해 즉시 갱신한다. `ConstructionMaterialDeliveryResult`는 등록 시 추가하고 EndStateApply ECB로만 게시/갱신하므로 Producer는 Playback 이후 결과를 확인한다. 취소/완공으로 현장이 없어져도 운송 결과는 남는다. 결과 소비자는 활성 예약이 없는 최종 결과를 확인한 뒤 운송 엔티티를 삭제한다. 활성 예약은 직접 삭제하지 말고 `CancelConstructionMaterialDeliveryRequest`로 종료한다. 결과 소비자가 다시 Reserved를 차감하지 않는다. 자동 만료/재시도/범용 메시지 프레임워크는 추가하지 않았다.

## 코드로 확인한 적용 경계

- 기존 시스템 안에서 현장 취소 → 운송 취소/닫힌 현장 정산 → 실물 선점 재구성/운송 등록 → 수령 → 완공 → 닫힌 현장의 운송 종료를 Job 의존성으로 연결한다. 모든 구조 변경은 기존 EndStateApply를 사용한다.
- 공급원은 등록 시 세 건물 타입과 Storage/Stored, 실존 Identity/Owner, 정확히 한 보관 참조를 확인한다. 미등록/종료/없는 운송의 공급 요청은 추가 효과 없이 소비한다.
- 수령은 F-004 충돌을 일반 실패 정산과 분리한다. 그 외에는 대상 필수 버퍼/요구량, 실물, 기대 Owner, 보관 버퍼, 원래 공급원의 잔류 참조, 활성 Transfer를 변경 전에 검증한다.
- 같은 운송 중복은 운송 State로 막는다. 등록은 Ready 또는 활성 예약 보존 운송의 실물 선점을 임시 집합에 재구성하고 새 운송의 중복을 예약 증가 전에 거부한다. 수령은 등록 단계의 실물 유일성과 운송 State를 사용하며 실제 소유 버퍼를 재검증한다. 등록 검증을 통과한 운송만 선점하며 요청 쿼리 순서를 공개 FIFO나 현장 우선순위로 보장하지 않는다.
- 이전 Stored 버퍼를 제거한 뒤 대상 버퍼를 다시 얻어 등록한다. 수령한 실물의 BeltMovementState도 비활성화한다. 요구량은 예약 해제 후 다시 읽어 Delivered를 갱신하므로 차감한 Reserved를 오래된 값으로 덮지 않는다. Owner/태그와 결과는 ECB에 기록한다. Job 변경과 ECB 실패를 통틀어 전역 rollback을 보장하지 않는다.
- 기존 Storage Apply 이후의 가시성을 유지하며 Ownership/Building Lifecycle 사이에 새 순서를 추가하지 않았다. 활성 Transfer와 ProcessedInStateApply 표시를 함께 읽어 같은 틱의 일반 인계는 Ownership 순서에 관계없이 거부한다. 요청 비활성화는 즉시 유지하며 처리 표시만 EndStateApply에서 초기화한다. F-004의 유효한 Destroy는 삭제 엔티티에 초기화 ECB를 기록하지 않는다.
- 완료된 현장은 해당 실행의 임시 집합으로 후속 정산 Job에 전달한다. 새로운 시스템, 영속 공간 캐시, 중간 Playback, 전체 World 완료를 추가하지 않았다.

## F-042 및 다른 이슈와의 경계

직접 인수 전에 대상 Stored 버퍼가 필요하므로 등록/수령에 선행 검사를 추가했다. 이는 F-042와 겹치는 승인 방어이며 대상 버퍼가 없으면 이전 버퍼 제거·Delivered/Progress/Owner 변경을 진행하지 않는다. 이미 등록된 운송의 일반 거부 시 해당 예약은 F-003 정책대로 해제한다. 불완전 현장의 자동 복구 및 F-042 전체 완료 처리는 포함하지 않는다.

F-044 진단은 변경하지 않았다. F-004 원본 기록의 당시 컴파일/실행 미검증 한계도 보존한다. 이 작업의 특정 거부 분기 테스트가 철거 전체, 실제 Destroy 삭제, 양쪽 전체 그룹 순서의 모든 F-004 경로를 검증한 것은 아니다.

## 검증

- 새 테스트를 일괄 선행 조건으로 삼지 않았다. 이번 변경은 실제 F-003의 실물 중복·소유 인계·예약 정산 결함이므로 기존 수령 테스트에 해당 회귀 사례를 추가했다.
- 기존 공급 테스트의 요청 생성부를 운송 등록 방식으로 바꾸고, 거부된 보관품이 원래 공급원에 남는 계약에 맞춰 기대값을 조정했다.
- 초기 복수 클래스 구분자 필터가 0건을 선택했다. 이를 통과로 세지 않고 클래스별로 실행했다. 해당 응답은 `Logs/QualityImprovement/F003/test-filter-no-match.json`에 보존했다.
- Unity 6000.4.11f1 연결 Editor 재컴파일: completed, failed:false, errors:[], compilationFailed:false. Editor ready/compiling:false/domainReloadInProgress:false/playMode:stopped. Assembly-CSharp.dll 및 Assembly-CSharp-Editor.dll이 각 최신 소스보다 새 파일임을 확인했다. [컴파일](../../../../Logs/QualityImprovement/F003/recompile-status.json) · [Editor 상태](../../../../Logs/QualityImprovement/F003/editor-status.json) · [어셈블리 최신성](../../../../Logs/QualityImprovement/F003/assembly-freshness.json)
- 관련 EditMode 합계 43/43, 실패/Skipped/Inconclusive 0. 최초 42건 통과 후 월드 인계 실물의 벨트 이동 비활성화를 보완했고, 영향받는 수령 및 공사 연결 묶음을 다시 실행했다. 나머지 경로의 동작 변경은 없었다.
- `Phase7ConstructionMaterialTests`: **17/17**. 실제 공통 Spawn으로 만든 세 공급원에서 동일 운송 중복·별개 운송의 동일 실물 경합·다음 프레임 재요청, 무예약/일반 거부/중복 취소의 특정 예약 정산, F-004 Destroy/철거 충돌의 예약 보존, 비건물 보관자/월드 인계, 두 수동 Ownership 실행 순서의 일반 Transfer 충돌, 대상 버퍼 누락, 같은 틱 완공 시 결과 보존/남은 예약 종료를 확인했다. Result의 Playback 전후 가시성도 검사했다. [최종 결과](../../../../Logs/QualityImprovement/F003/Phase7ConstructionMaterialTests-status.json)
- `Phase7ConstructionCompletionTests`: **9/9**. 기존 완공 및 Spawn 실패 시 현장/자재 보존 경로. [결과](../../../../Logs/QualityImprovement/F003/Phase7ConstructionCompletionTests-status.json)
- `Phase7ConstructionCancelTests`: **4/4**. 기존 취소 반환 및 수령 전 취소 거부. [결과](../../../../Logs/QualityImprovement/F003/Phase7ConstructionCancelTests-status.json)
- `Phase7EndToEndConstructionPipelineTests`: **4/4**. 테스트 World의 정렬된 그룹에서 기존 배치/공사 연결. [최종 결과](../../../../Logs/QualityImprovement/F003/Phase7EndToEndConstructionPipelineTests-status.json)
- `Phase7BuildingLifecycleTests`: **9/9**. 기존 직접 Spawn/철거 경로. [결과](../../../../Logs/QualityImprovement/F003/Phase7BuildingLifecycleTests-status.json)
- `git diff --check` 통과. 원본 로그는 로컬 검증 자료이며 저장소에 포함되지 않을 수 있다.

## 후속 파일 통합 (2026-10-02)

사용자 요청에 따라 수령 Job, 운송 등록/취소/현장 종료 Job 및 공유 연산을 모두 `ConstructionLifecycleApplySystem.cs`에 합쳤다. 별도로 만들었던 Job 소스 두 개와 해당 .meta를 제거하고 코드 지도의 경로를 갱신했다. 이동 시 using 선언을 제외한 두 파일의 본문이 그대로 포함되는지와 각 Job 정의가 한 번만 존재하는지 확인했다. 처리 로직과 Job 연결 순서는 변경하지 않았다.

파일 통합 후 재컴파일 completed/failed:false/errors:[]/compilationFailed:false, Editor ready/compiling:false/domainReloadInProgress:false 및 최신 Assembly-CSharp.dll을 확인했다. `git diff --check`도 통과했다. 동작 변경 없는 파일 배치 조정이므로 테스트는 재실행하지 않았으며 위 43/43은 통합 전 구현 검증 결과다. [후속 컴파일](../../../../Logs/QualityImprovement/F003/job-colocation-recompile-status.json) · [Editor 상태](../../../../Logs/QualityImprovement/F003/job-colocation-editor-status.json) · [어셈블리 최신성](../../../../Logs/QualityImprovement/F003/job-colocation-assembly-freshness.json)

## 후속 리뷰 보완 (2026-10-02)

초기 43/43에는 `ExpectedOwner = Entity.Null`인 같은 틱의 일반 출고/공급과, 예약 여유가 작은 현장에서 동일 실물의 여러 등록이 정상 다른 실물을 막는 조건이 빠져 있었다. 코드 검토에서 발견한 두 경계를 아래와 같이 수정했다. 기존 성공 결과를 이 반례의 수정 전 실행 증거로 취급하지 않는다.

- Transfer 충돌: 기존 TransferOwnershipRequest에 ProcessedInStateApply를 추가했다. Ownership은 일반 요청을 즉시 비활성화하면서 표시를 남기고 같은 EndStateApply ECB에서 초기화한다. 공사 등록/수령은 활성 여부와 표시를 함께 읽으므로 이미 소비된 같은 틱의 Transfer도 일반 충돌로 거부한다. 표시 자체는 성공 결과가 아니며 무효 TargetOwner의 Drop도 포함한다. 유효한 Destroy 대상은 기존처럼 요청만 비활성화하고 삭제 실물에 초기화 ECB를 기록하지 않는다. 새로운 컴포넌트/시스템/시간 기반 프레임 번호/Ownership 순서 지정은 추가하지 않았다.
- 등록 중복: 기존 운송의 Ready 또는 ReservationActive 상태에서 실물 선점을 매 실행의 임시 집합으로 재구성한다. 새 등록은 검증 후에만 선점하고 예약을 증가시키며 같은 실물은 ItemAlreadyClaimed로 거부한다. 기존 활성 운송과 같은 실행의 신규 운송 모두 포함한다. F-004로 거부되어도 활성 예약을 보존한 운송의 선점은 유지된다.
- 해제와 재등록: 운송 취소와 이미 닫힌 현장의 정산을 새 등록보다 먼저 처리한다. 아직 등록되지 않은 운송을 취소하면 Result를 ECB에서 처음 추가하며, 기존 등록 운송은 Result를 갱신한다. 완료 직후의 남은 운송 정산도 기존대로 수행한다. 모든 Job과 공유 연산은 ConstructionLifecycleApplySystem.cs 안에 유지했다.

후속 관련 EditMode는 **47/47**, 실패/Skipped/Inconclusive 0이다.

- `Phase7ConstructionMaterialTests`: **26/26**. 기대 Owner가 공급원/월드인 경우 × Ownership 선행/후행 네 조건, 처리 표시의 Playback 전후와 다음 틱 해제, 동일/다음 틱 중복 등록과 다른 실물 예약, 등록 전/후 취소 후 재등록, 현장 취소 후 재등록, 두 ECB 생성 순서의 유효한 Destroy 충돌을 포함한다. [결과](../../../../Logs/QualityImprovement/F003/review-fixes/Phase7ConstructionMaterialTests-status.json)
- `Phase3StorageOwnershipTests`: **4/4**. 즉시 요청 비활성화를 포함한 기존 일반 입출고/소유권 경로. [결과](../../../../Logs/QualityImprovement/F003/review-fixes/Phase3StorageOwnershipTests-status.json)
- `Phase7ConstructionCompletionTests`: **9/9**, `Phase7ConstructionCancelTests`: **4/4**, `Phase7EndToEndConstructionPipelineTests`: **4/4**. [완공](../../../../Logs/QualityImprovement/F003/review-fixes/Phase7ConstructionCompletionTests-status.json) · [취소](../../../../Logs/QualityImprovement/F003/review-fixes/Phase7ConstructionCancelTests-status.json) · [공사 연결](../../../../Logs/QualityImprovement/F003/review-fixes/Phase7EndToEndConstructionPipelineTests-status.json)
- 재컴파일 completed/failed:false/errors:[]/compilationFailed:false. [컴파일](../../../../Logs/QualityImprovement/F003/review-fixes/recompile-status.json)
- Editor ready/compiling:false/domainReloadInProgress:false/playMode:stopped, 런타임/Editor 어셈블리 최신성 및 `git diff --check` 통과를 확인했다. Git 줄바꿈 변환 안내는 diff 검사 로그에 보존했다. [Editor 상태](../../../../Logs/QualityImprovement/F003/review-fixes/editor-status.json) · [최신성](../../../../Logs/QualityImprovement/F003/review-fixes/assembly-freshness.json) · [diff 검사](../../../../Logs/QualityImprovement/F003/review-fixes/diff-check.txt)

## 중복 방어 정리 (2026-10-02)

- 수령 Job의 `AcceptedItems` 집합 할당·추가·조회·해제를 제거했다. 등록 단계의 실물 선점이 같은 실물의 여러 Ready 운송을 차단하고, 수령 시 즉시 갱신하는 운송 State가 같은 운송의 중복 요청을 차단한다. 현재 소유권·보관 버퍼 검증은 유지한다. 외부에서 운송 State/입력 필드를 임의로 변경하지 않는 기존 공개 계약을 전제로 한다.
- 등록 검증에서 찾은 요구 자재 인덱스를 `out int requirementIndex`로 전달하여 예약 증가 시 재검색하지 않는다. Job과 공유 연산은 같은 시스템 파일에 유지했다.
- 신규 등록이 없을 때의 등록 준비 작업 생략은 이번 변경에 포함하지 않았다. 별도 상태나 시스템을 추가하지 않았으며 성능 개선 폭은 측정하지 않았다.
- Unity 6000.4.11f1 재컴파일 completed/failed:false/errors:[]/compilationFailed:false, Editor ready/compiling:false/domainReloadInProgress:false/playMode:stopped 및 어셈블리 최신성을 확인했다. `Phase7ConstructionMaterialTests` **26/26**, 실패/Skipped/Inconclusive 0, 새 테스트 추가 없음. 이번 정리는 중복 공급·실물 예약에 직접 관련된 기존 묶음만 다시 실행했으며 이전 47건을 전부 재실행한 결과는 아니다. [검증 결과](../../../../Logs/QualityImprovement/F003/cleanup/verification-live.json) · [컴파일](../../../../Logs/QualityImprovement/F003/cleanup/recompile_status.json) · [Editor 상태](../../../../Logs/QualityImprovement/F003/cleanup/editor_status.json)
- 최초 샌드박스 실행은 Editor 연결 파일 읽기 권한 오류로 컴파일/테스트 시작 전에 종료됐다. 사용자 실행 환경에서 같은 검증을 완료했다. [최초 시도](../../../../Logs/QualityImprovement/F003/cleanup/verification.json)
- 변경 파일의 `git diff --check`를 통과했다. 기존 Git 줄바꿈 변환 안내는 로그에 보존했다. [diff 검사](../../../../Logs/QualityImprovement/F003/cleanup/diff-check.txt)

## 실행 미검증

실제 드론 운송/작업 배정, 결과 소비자의 런타임 연동, 실제 SubScene 베이킹, Play Mode, 화면 렌더링, 입력, 성능은 검증하지 않았다. 테스트에서 사용하는 비건물 보관자는 운반자 계약을 나타내는 ECS fixture이며 드론 구현의 증거가 아니다. 잘못된 외부 Producer가 활성 운송 기록을 직접 삭제하거나 입력 필드를 변경하는 경우의 전역 복구도 제공하지 않는다.
