# F-006 — 중단된 월드의 ProductResult 소비 정책 확정

2026-10-12. [최초 평가 Q03](Q03.md), [추가 근거 Q11](Q11.md), [현재 Task](../V2%20Quality%20Improvement%20Tasks.md). 사용자 지시로 **A: 현행 소비 유지**를 확정했다. 제품 코드는 유지하고 정책과 한계를 문서화하여 완료하며 새 컴파일·테스트는 실행하지 않는다.

## 확정 정책과 완료 이유

치명적 오류로 중단된 월드는 계속 사용하지 않고 종료하거나 새 월드를 시작하는 전제다. 이 월드에서는 예상 밖 Item 프리팹 누락/Null로 만들지 못한 결과도 포함한 현재 `ProductResult` 전체 소비를 허용한다. 조사용 미생성 기록을 보존하지 않으며 일부 출력만 생성되는 상태도 허용한다. 성공분 취소, 선소비 재료/광물 보상, 현재 틱 rollback, 자동 재시도/복구, 저장/로드·중단 월드 재개는 제공하지 않는다.

F-006은 이 손실 허용 여부가 미확정이었던 정책 항목이다. 사용자 선택과 현재 코드 계약이 일치하므로 제품 수정 없이 소비 정책·부분 생성·검증 한계를 문서화하여 완료한다. 이는 생성 실패를 방지하는 새 구현이나 실패 실행 검증의 완료가 아니다. 중단 월드의 재사용이나 미생성 기록 보존이 필요해지면 성공분과 미생성분의 경계를 별도로 설계해야 한다.

## 코드로 확인한 책임과 시점

- [CrafterExecutionJob.Execute](../../../../Assets/Scripts/Systems/Buildings/Execution/CrafterExecutionSystem.cs)는 착수 전에 재료를 전체 확인한 뒤 Stored 참조 제거·Destroy 요청 활성화로 선소비한다. 실물 삭제는 ItemLifecycle과 EndBuilding이 담당한다. 완료 때 출력별 ProductResult를 기록하고 제작 활성 상태·진행도를 초기화한다.
- [MinerExecutionJob.Execute](../../../../Assets/Scripts/Systems/Buildings/Execution/MinerExecutionSystem.cs)는 완료 결과를 기록하면서 유한 광맥의 잔량을 직접 차감하고, 고갈 시 EndBuilding에 자원 삭제를 기록한다. 결과 실체화 실패에 따른 복원 처리는 없다.
- [ProductResultApplyJob.Execute](../../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs)는 유효한 각 행의 프리팹을 조회한다. 누락/Null이면 Fatal 기록 후 다음 행을 처리하며, 마지막에는 성공/실패 행을 모두 Clear한다. 프리팹이 있는 결과는 Count개의 생성·생산자 소유권 초기화·ProductItemElement append를 같은 ECB에 기록한다.
- [ItemLifecycleUtility.SpawnPrefabItem](../../../../Assets/Scripts/Common/ItemLifecycleUtility.cs)은 전달된 원형의 초기화 명령을 기록한다. 반환 엔티티는 ECB 기록 단계의 참조이며 실제 생성 성공을 확인한 결과가 아니다. DB 조회·실패 처리·결과 소비와 Product 버퍼 등록은 기존 ItemLifecycle 호출자가 계속 소유한다.
- 실물·Owner·Product 참조는 EndBuilding의 정상 Playback 뒤에 확정된다. ProductResult.Clear는 그보다 앞선 Job의 직접 변경이며 같은 ECB 사용은 생산 묶음 전체의 원자성이나 rollback을 뜻하지 않는다.
- [SimulationFailureUtility.Record](../../../../Assets/Scripts/Common/SimulationFailureUtility.cs)는 오류를 로그로 남기고 ECB에 SimulationFatalError를 기록한다. [GameSimulationGroup.OnUpdate](../../../../Assets/Scripts/Phases/GameSimulationGroup.cs)는 틱 시작에만 Fatal을 검사하므로 EndBuilding에서 오류가 확정된 현재 틱은 Drone·Commit·Synchronization까지 진행하고 다음 틱부터 차단된다. 이것은 앱 종료나 World 폐기가 아니다.

## 한 생산 묶음의 세 경우

설명용으로 철 2개와 돌 1개를 만드는 제작 묶음을 가정한다. 다음은 프리팹 누락/Null 분기를 따른 조건부 코드 분석이며, 실제 발생이나 실행 재현 결과가 아니다. 실제 아이템/소유 버퍼 설명은 EndBuilding Playback이 정상 완료된다는 조건이다.

- **전체 성공:** 재료는 이미 소비했다. 철 2개·돌 1개의 생성과 생산자 Product 버퍼 등록 명령을 기록하고 결과를 Clear한다. 정상 Playback 뒤에는 세 실물과 대응하는 소유 버퍼 참조가 확정된다.
- **전체 누락:** 재료는 이미 소비했다. 두 품목 모두 생성 명령을 기록하지 않고 오류와 Fatal 게시 명령만 남긴다. 결과는 Clear하므로 만들지 못한 품목·수량·슬롯의 생산 기록은 남지 않는다.
- **일부 누락:** 철만 유효하면 철 2개의 생성·등록 명령을 유지하고 돌의 생성은 건너뛴다. 정상 Playback 뒤에는 철 실물과 소유 버퍼 참조가 남으며 돌 1개의 생산 기록은 삭제된다. 전체 묶음을 취소하거나 재료를 돌려주지 않는다. 앞 행이 실패해도 뒤의 유효한 행 처리는 계속한다.

## 위험 범위와 한계

[PrefabDatabaseInitializationSystem](../../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs)은 시작 전에 필수 DB와 아이템의 모든 정식 품목, 원형 생존·Prefab·LocalTransform·ItemIdentity를 검사한다. 검증 후 DB 불변 계약을 유지한다. 직접 참조 분석에서 Item DB 작성자는 Baker였고 정상 게임 중 등록을 제거/Null로 바꾸는 Writer는 확인하지 못했다. 현재 결과 Producer는 검증된 레시피 출력을 읽는 Crafter와 생성된 ResourceNode의 품목을 읽는 Miner다. 이미 금지된 DB 변형이나 격리 World에서 준비 검사를 우회하는 오류 주입 조건을 흔한 정상 입력의 생산 결함으로 설명하지 않는다.

- ProductResult는 영속 생산 이력이 아니며 품목·Count·SlotIndex만 가진다. 삭제된 결과를 오류 로그만으로 원래 생산 묶음·재료 내역까지 복원할 수 없다. 현재 누락 로그도 품목 중심이며 생산자·수량·슬롯을 모두 남기지 않는다.
- 부분 생성된 실물과 소비된 재료/광맥 상태, 로그·Fatal은 남을 수 있지만 미생성 결과 기록은 남지 않는다. 중단된 월드를 계속 실행하기 위한 일관성·복구 계약은 제공하지 않는다.
- 이 확인은 명시적 누락/Null 처리 분기다. Null이 아닌 무효 원형이나 ECB Playback 예외가 모두 같은 Fatal 경로로 처리되거나 동일한 최종 상태를 남긴다고 보장하지 않는다. 예외가 Clear 전에 발생하는 경우까지 항상 전체 소비된다고 확대하지 않는다.
- 현재 틱 완주와 다음 틱 차단은 기존 도메인 계약이다. DB 불변은 시작 검사의 전제이며 매 틱 재검증이나 외부 변형 복구 기능이 아니다.
- 승인 철거의 완료 생산물 폐기/선소비 재료 비보상(F-005), 공사 완료 Spawn 실패의 현장/보관 자재 보존(F-024), 초기화/정의 오류의 시작 차단(F-011/F-013/F-015)은 기존 별도 계약을 유지한다. 다른 생성·환급·요청 실패 정책으로 A를 자동 확장하지 않는다.

## 레거시 근거의 의미

앞선 읽기 전용 분석에서 삭제 직전 Git `f85d2ca`의 CrafterSystem.TryCompleteCraft, MiningSystem.Mine/ConsumeDeposit, ItemStorageSystem.DestroyStoredItem과 ItemSpawnSystem.OnUpdate/ProcessRequest/SpawnStoredItem을 확인했다. 생산 완료 시 생성 요청과 재료/광맥 소비를 기록하고 후속 ItemSpawnSystem 업데이트에서 실물과 ProducedItemElement를 만들었다. 요청 처리 중 품목 프리팹이 없거나 예외가 나면 로그를 남긴 뒤 finally에서 요청을 삭제했으며 그 처리 경로에는 V2의 Fatal 게임 그룹 중단이 없었다. DB 자체 부재로 RequireForUpdate를 충족하지 못하면 처리 전 요청이 남을 수 있지만 의도적 생산 이력 보존으로 설명한 근거는 없었다.

레거시의 실패 요청 소비 동작은 코드로 확인됐지만 경제적 손실을 허용한다는 게임 정책 의도는 미확인이다. 기존 결함을 유지해야 한다는 근거로 채택하지 않았으며, 이번 A 정책의 근거는 2026-10-12 사용자 결정과 중단 월드 비재사용 전제다. 레거시 실행도 새로 수행하지 않았다.

## 변경과 확인 범위

- AGENTS의 ProductResult 계약, Production CodeMemory, 최신 Task의 F-006 상태·분류별 집계·완료 기록을 갱신했다. Q03/Q11과 2026-10-06 전체 검토에는 최신 정책 링크를 추가하고 원본 평가/과거 집계는 당시 기록으로 보존했다.
- 코드 분석의 기준은 앞선 읽기 전용 확인과 현재 Git `98922a2`다. 이번 문서 편집 직전 해당 생산·생성·중단·프리팹 검증 소스의 차이가 없음을 확인하여 같은 코드 근거를 재사용했다. 새 전체 코드 재검토를 수행한 것은 아니다.
- 제품 C#·테스트·Config·에셋은 수정하지 않았다. 문서 diff와 git diff --check를 확인했고, 새 기록과 갱신한 정책 링크의 상대 대상 18개가 모두 존재했다. 체크된 Task 43개(완료 38개·추적 종료 5개), 미완료 0개와 분류별 현황 집계가 일치했다. 변경 파일은 문서 7개뿐이며 새 기록의 공백·문자 검사를 포함한다.
- 새 컴파일·EditMode·Play Mode·베이킹·Player 빌드·DB 변형/실패 주입은 모두 미실행이다. 과거 컴파일·테스트·Baker 기록을 생산 실패 실행 증거로 합산하지 않는다.
- 추가 완료는 **F-006 한 건**이며 최신 Task는 **43/43 완료/추적 종료, 미완료 0건**이다. 제외된 5개 항목은 다시 합산하지 않으며, 모든 게임 기능이나 미검증 실행·성능이 완료됐다는 뜻이 아니다.
