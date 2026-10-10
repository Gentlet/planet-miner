# F-004 구현 및 검증 기록

> 2026-10-10 F-032 관련 후속 안내: 아래 2026-10-02 입고 후 반환 구현·2/2 등은 당시 기록이다. 현재 철거는 [2026-10-04 승인·앞단 동작 중단 정책](BuildingDemolitionStop-Verification.md)으로 입고/출고 후보를 차단한다. F-032는 비활성 원본의 잔여 진행도 선택도 거절하지만 Owner/렌더/버퍼 Writer나 ECB 순서를 새로 조정하지 않았다. 기존 완료를 유지하며 새 입고/철거 실행은 없다. [직접 영향과 한계](F032-Verification.md#2026-10-10-관련-이슈-후속-정리)를 따른다.

- 날짜: 2026-10-02
- [Task](../V2%20Quality%20Improvement%20Tasks.md#f-004--같은-프레임-입고철거의-렌더-태그-순서-의존) · 최초 평가 [Q02](Q02.md) · 철거 평가 [Q28](Q28.md)
- 상태: 승인된 정책 구현·컴파일 완료. 후속 F-003에서 공사 공급 충돌과 Destroy/Transfer 일부 경계를 EditMode로 검증했다. 일반 입고+철거는 실제 시스템을 두 상대 순서로 수동 실행하는 회귀 테스트 2/2를 추가 확인했다. 이 경로의 자동 정렬 그룹 실행은 미수행.

## 확정 정책과 범위

정책의 기준은 [AGENTS.md](../../../../AGENTS.md)의 F-004 계약이다. 같은 틱 입고·철거는 입고 후 건물 자리로 반환한다. 철거 버퍼에 남은 실물의 공급을 거부하며, 유효한 Destroy를 유지하고 충돌 인계를 거부한다. 각 기존 시스템은 자신의 적용 대상만 변경한다.

F-005의 Command 승인, 완료 생산물 폐기, Storage/Product Spawn 거부, 선소비 미보상, 기존 실물 반환·건축 비용 환급·World Spawn 유지 계약은 바꾸지 않았다. F-004 충돌의 공급 거부는 요청을 소비하고 활성 예약을 보존한다. 최초 F-004 구현 당시에는 요청에 운송/예약 식별 정보가 없어 임의의 예약 차감·재시도를 추가하지 않았다. 후속 [F-003](F003-Verification.md)은 운송 키와 건별 정산을 도입했으며, 일반 최종 거부의 해당 예약 해제와 F-004 충돌의 예약 보존을 구분한다. F-004 충돌의 자동 재시도는 지원하지 않는다.

## 최초 F-004 구현에서 변경한 코드

- `Common/DemolishBuildingRequestLookup.cs`: Item Lifecycle 파일에 있던 기존 조회를 이동하고, 승인된 철거 요청의 Stored/Product 버퍼에 실물이 남아 있는지 확인하는 조회를 추가했다. 별도 승인·반환 목록이나 영속 상태는 생성하지 않는다.
- `BuildingItemStorageApplySystem`: 활성 Destroy 대상은 입출고 결정만 소비하고 버퍼·위치·이동·Transfer를 변경하지 않는다. 그 외의 기존 입출고와 벨트 Fence는 유지한다.
- `ItemOwnershipApplySystem`: 활성 Destroy 대상과 철거 버퍼의 실물은 Transfer만 소비한다. 해당 아이템의 Owner·렌더 태그를 쓰지 않으며, 일반 이전의 기존 분기는 유지한다.
- `ConstructionLifecycleApplySystem`: Storage Apply 이후의 소유 버퍼를 읽도록 순서를 명시했다. Destroy/철거 반환 실물의 공급은 도착량·예약량·현장 버퍼·Owner·태그 변경 전에 거부한다. 취소 반환에서도 Destroy 대상은 제외한다. 사용하지 않던 Owner 조회 대신 실제 버퍼 소속을 읽는다.
- `BuildingLifecycleApplySystem`: 기존 반환 위치·Owner·태그 처리를 유지하고 Destroy 대상의 반환을 제외한다. 같은 삭제 실물에 벨트 정지 ECB가 적용되는 것을 막기 위해 철거 벨트 Cleanup도 Destroy 대상을 제외한다.
- `ItemLifecycleApplySystem`: 기존 조회 클래스의 파일 이동만 반영했다. 생성·완료 생산물·Destroy의 적용 정책은 유지한다.
- `ItemRequests`, `ConstructionRequests`: 충돌 시 요청 소비와 적용 책임을 주석에 반영했다.

## 코드로 확인한 실행 경계

Storage Apply의 Input→Output Job과 기존 Fence/Dependency를 유지한다. Ownership은 기존처럼 Storage/Route 이후 실행하며, 공사 수령은 새로 Storage 이후의 버퍼 가시성을 요구한다. Construction→Building Lifecycle은 기존 순서다. Ownership과 Building Lifecycle의 상대 순서 및 Building/Item Lifecycle 간 승인 전달 순서는 추가하지 않았다.

Ownership과 Construction의 철거 요청 임시 복사본은 `ToComponentDataListAsync` 핸들 뒤에 소비 Job을 연결하고 사용 후 해제한다. 마지막 핸들은 `state.Dependency`와 EndStateApply의 producer에 전달한다. 전체 World 완료, 중간 Playback, 새로운 Sync Point는 추가하지 않았다.

다음은 최초 F-004 구현 당시의 소스 경로 분석이다. 후속 실행으로 확인한 부분은 아래 별도 절에 기록한다.

1. **입고+철거:** Storage가 실물을 버퍼에 추가한다. Ownership은 버퍼 소속으로 철거 반환임을 확인해 Transfer만 소비한다. 철거 ECB만 최종 World Owner·건물 위치·태그 제거를 기록하므로 기존 두 ECB의 Add/Remove 경합이 제거된다. 새 입고품의 이동은 Storage Apply가 이미 비활성화한다. 건물과 소유 버퍼는 EndStateApply에서 삭제된다.
2. **출고+철거:** 정상 출고는 원본 버퍼에서 실물을 제거하므로 조회상 철거 반환 대상이 아니다. 일반 Transfer가 World Owner·태그 제거를 적용하고 기존 출고 위치·이동을 유지한다. 출력 벨트도 철거되면 기존 Cleanup이 정지시킨다.
3. **공급+철거:** 입출고 이후에도 철거 Stored/Product 버퍼에 남은 실물은 수령 승인 전에 거부한다. 도착량·예약량·현장 버퍼·렌더 명령을 기록하지 않고 요청만 소비한다. 현재 Owner가 월드인 이번 틱 입고품도 버퍼 소속으로 식별한다.
4. **Destroy+인계/반환:** 대상의 활성 Destroy를 각 변경 경계에서 확인하여 충돌한 입출고·Transfer·Supply·반환/벨트 정지 명령을 제외한다. Item Lifecycle의 삭제는 유지한다. 유효한 Destroy는 Producer의 선행 소유 버퍼 제거를 전제로 하며, 잘못된 잔류 버퍼의 전역 복구나 공사 도착량 재산정은 구현하지 않았다.

## 최초 F-004 구현 시 수행한 검증

- `Verify-Unity.ps1 -CompileOnly`는 모드 선택 오류로 컴파일 시작 전에 실패했다. 래퍼 자체는 수정하지 않았으며 직접 Unity CLI로 전환했다.
- Unity 6000.4.11f1의 연결된 Editor에서 `recompile --focus false` 후 `recompile_status`를 확인했다.
- 결과: `completed`, `failed:false`, `errors:[]`, `compilationFailed:false`.
- Editor: `ready`, `compiling:false`, `domainReloadInProgress:false`, `playMode:stopped`.
- `Assembly-CSharp.dll`은 최신 프로젝트 C# 소스보다 새 파일이며 freshness 확인은 true다.
- `git diff --check`: 오류 없음. 기존 파일의 줄바꿈 변환 경고는 저장소 설정에 따른 것이며 컴파일 오류가 아니다.

원본 로그: [컴파일 시작](../../../../Logs/QualityImprovement/F004/recompile-start.json), [최종 상태](../../../../Logs/QualityImprovement/F004/recompile-status.json), [Editor 상태](../../../../Logs/QualityImprovement/F004/editor-status.json), [어셈블리 최신성](../../../../Logs/QualityImprovement/F004/assembly-freshness.json). 로그는 로컬 검증 자료다.

## F-003 후속 검증 근거 (2026-10-02)

최초 F-004 구현에서는 테스트를 추가하거나 실행하지 않았다. 이후 F-003에서 추가한 아래 사례가 후속 리뷰 검증 및 중복 방어 정리 후 `Phase7ConstructionMaterialTests` 실행에 포함됐다. 자재 테스트 클래스 전체는 **26/26**, 실패/Skipped/Inconclusive 0이며 아래 F-004 관련 조건은 그중 4건이다. 이번 문서 갱신은 기존 소스와 실행 기록을 연결한 것이며 새 컴파일/테스트 실행이 아니다.

- `F004Conflict_PreservesRegisteredReservationUntilExplicitCancellation`의 철거/Destroy 두 조건: 공급 요청 소비, Delivered=0, 현장 Stored 비움, 기존 Owner 및 운송의 활성 예약 보존, 해당 충돌 결과, 명시적 운송 취소 후 예약 해제를 확인했다. 철거 요청은 fixture가 직접 구성하므로 실제 Command 승인→입고→철거 전체 그룹의 재현은 아니다.
- `DestroyedTransfer_DoesNotRecordCleanupCommandsAgainstDeletedItem`의 Ownership 선행/후행 두 조건: Item Lifecycle과 Ownership의 실행 순서를 바꿔 ECB를 재생한 뒤 실물 삭제, 공사 예약 보존 및 DestroyConflict 결과를 확인했다. Producer가 원래 소유 버퍼에서 실물을 먼저 제거하는 유효한 Destroy 계약을 전제로 한다.
- 근거: [테스트 소스](../../../../Assets/Editor/Tests/Phase7ConstructionMaterialTests.cs) · [F-003 구현/검증 기록](F003-Verification.md) · [정리 후 26/26 결과](../../../../Logs/QualityImprovement/F003/cleanup/verification-live.json) · [후속 재컴파일](../../../../Logs/QualityImprovement/F003/cleanup/recompile_status.json)

## 일반 입고·철거의 두 적용 순서 회귀 검증 (2026-10-02)

- 기존 공사 공급 거부와 벨트 라우팅·철거 테스트는 일반 Storage 입고 후 Ownership/Building Lifecycle의 렌더 태그 경합을 직접 확인하지 않는다. 이 구체적인 회귀를 잡기 위해 `Phase7BuildingDemolishTests.SameTickStorageInputAndDemolition_ReturnsPhysicalItem_InEitherApplyOrder`에 두 실행 순서만 추가했다.
- 실제 Command 검증과 EndCommand 재생 뒤 `BuildingItemStorageApplySystem`을 실행한다. Stored 버퍼의 동일 실물 등록과 활성 Transfer를 중간 assertion으로 확인한 다음, Ownership→Building Lifecycle 또는 Building Lifecycle→Ownership 순서로 실행하고 EndStateApply를 재생한다.
- 두 조건 모두 **2/2 통과**, 실패/Skipped/Inconclusive 0. 동일 아이템의 생존, World Owner, 철거 건물 원점의 GridPosition/LocalTransform, DisableRendering 제거, 벨트 이동·Transfer 비활성화, 처리 표시 초기화, 건물·철거 요청 삭제를 확인했다. 상대 순서는 테스트가 직접 제어하며 자동 정렬 그룹의 실행 결과로 표현하지 않는다.
- 같은 변경 묶음에서 공사 테스트 준비의 숨은 소유권 변경을 제거하고 제작기 레시피 변경의 중복 검사를 정리했다. 관련 EditMode는 총 **47/47**(공사 수령 26, 취소 4, 완공 9, 공사 연결 4, 새 입고·철거 회귀 2, 레시피 변경 2), 실패/Skipped/Inconclusive 0이다.
- Unity 6000.4.11f1 재컴파일 `completed`, `failed:false`, `errors:[]`, `compilationFailed:false`, Editor ready/컴파일·리로드 중 아님/Play Mode stopped 및 런타임·Editor 어셈블리 최신성을 확인했다. `git diff --check`는 통과했고 기존 줄바꿈 변환 안내만 있었다.
- 근거: [검증 결과](../../../../Logs/QualityImprovement/StyleAlignment/verification-wrapper.json) · [컴파일](../../../../Logs/QualityImprovement/StyleAlignment/recompile-status.json) · [Editor 상태](../../../../Logs/QualityImprovement/StyleAlignment/editor-status.json) · [어셈블리 최신성](../../../../Logs/QualityImprovement/StyleAlignment/assembly-freshness.json) · [diff 검사](../../../../Logs/QualityImprovement/StyleAlignment/diff-check.txt)

## 실행 미검증과 남은 범위

일반 입고+철거의 두 상대 실행 순서와 해당 ECB 재생 결과는 위 회귀 테스트에서 확인했다. Decision/Reservation을 포함한 전체 자동 정렬 그룹 실행은 이번에 검증하지 않았다. 위 결과와 공급 거부·Destroy 일부 경계의 통과를 전체 물류/반환 경로의 런타임 일치 증거로 확대하지 않는다. Play Mode, 실제 렌더링·베이킹·성능도 검증하지 않았다.

일반 중복 공급·소유 버퍼 인계·운송별 정산의 구현과 검증은 F-003 기록에서 별도로 다룬다. 잘못된 외부 요청의 전역 보정은 구현하지 않았다. Q02/Q28의 원본 평가 기록은 수정하지 않았다.
