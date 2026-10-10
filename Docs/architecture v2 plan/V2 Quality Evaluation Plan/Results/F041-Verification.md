# F-041 설치 순서 승계와 공통 동률 비교

> 2026-10-10 F-032 후속 안내: 아래 F-032 미완료와 38/43 집계는 F-041 완료 당시 기록이다. 이후 F-032 활성 원본 검사·입고 결정 정리를 구현·컴파일 완료하여 최신 Task는 39/43·미완료 4건이다. 직접 영향 후속에서도 추가 완료/종료는 0건이다. F-041 설치 비교·기존 92/92는 유지하며 F-032 비활성 원본/재설치 실행으로 합산하지 않는다. [F-032 현재 범위·관련 영향과 한계](F032-Verification.md#2026-10-10-관련-이슈-후속-정리)를 따른다.

날짜: 2026-10-10. 사용자 확정 정책 구현, 현재 코드 확인, Unity 컴파일 확인, 선별 EditMode 92/92 통과. F-041 완료.

## 확정한 게임 동작

명시 설치 Tick이 먼저이며 실제 접수 순서와 구분한다. 같은 Tick에서는 먼저 접수한 묶음 전체, 이어 원래 후보 순서를 우선한다. 끝까지 같으면 좌표 x, 이어 y가 작은 건물이다. 표식을 생략한 직접 생성도 새 설치로 발급하며 명시 표식과 현장→완공의 기존 전체 표식은 보존한다.

F-027의 승인 순서·최종 승인 셀 공유·Strict/Partial, 기존 벨트 방향 변경의 표식 보존, F-029의 외부 진입 한 개/틱과 일반 T형 직접 합류 제외를 유지했다. 라우터의 유효한 기준 연결과 성공 시 커서 갱신, 드론의 거리 우선·공급/회수 대표 비교·작업 생성 순번은 별도 기존 계약으로 유지한다. F-023/F-026과 제외된 F-033은 재작업하지 않았다.

## 책임과 공개 데이터

- [PlacementStamp](../../../../Assets/Scripts/Components/Buildings/PlacementStamp.cs)는 Tick/ReceiptSequence/Order를 보존한다. 공통 Compare는 표식 유무→Tick→양수 접수번호 유무/번호→Order→좌표 x/y 순서다. 접수번호 0인 기존 명시 표식도 허용하며 같은 Tick의 양수 번호 뒤에 둔다. 같은 좌표까지 같으면 동순위이고 Entity/방향/Query 순서로 보완하지 않는다.
- [기존 접수 Utility](../../../../Assets/Scripts/Common/BuildingPlacementRequestUtility.cs)와 BuildingPlacementReceiptSequence를 확장했다. 원본의 NextValue/CurrentTick은 World 수명이며 Command가 기존 정상 처리 종료에서 Tick을 전진한다. 새 시스템·컴포넌트·공간 캐시·별도 순번 원본은 추가하지 않았다.
- [Command](../../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs)는 후보 Tick→헤더 Tick→공통 Tick을 선택하고 요청의 번호와 원래 후보 인덱스를 현장에 복사한다. 요청/후보 삭제와 현장 생성은 EndCommand다. 승인 순서는 명시 Tick으로 바꾸지 않는다.
- [완공](../../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs)은 기존 값 전달로 확장한 표식 전체를 EndBuilding 건물에 승계한다. 완공 순서나 새 Entity 값으로 재발급하지 않는다. 현장에 표식 자체가 없는 비정상/격리 입력은 기존 default 전달을 유지한다.
- [직접 생성 요청](../../../../Assets/Scripts/Components/Buildings/BuildingRequests.cs)은 HasPlacementStamp로 명시 0/0과 생략값을 구별한다. SubmitSpawn은 접수 시 생략 표식만 발급하고 명시 기록은 보존한다. 원시 요청은 기존 지원을 유지하되 실제 접수 순서를 복원할 수 없으므로 Apply 준비 묶음마다 같은 표식을 발급해 좌표 비교를 사용한다. 이 값은 실제 접수 순번의 증거가 아니다. 준비는 Job 완료 후 Lookup 갱신 전에 수행하며 생성/소비는 EndBuilding이다. 실제 런타임 직접 생성 Producer/UI는 후속이다.
- [목적지 예약](../../../../Assets/Scripts/Systems/Buildings/Reservation/BeltDestinationReservationSystem.cs), Splitter/Merger 기준 벨트 탐색, [드론 비교](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs)는 같은 공개 비교를 사용한다. 후속 소비자는 배치 시스템의 내부 필드나 임시 요청 엔티티를 읽지 않는다. 드론 작업/경로/배정/결과 공개는 기존 EndSimulation, 공간 맵 갱신은 Synchronization이다.

## 검증

Serena initial_instructions/프로젝트 활성화는 이전 분석에서 수행했고 이번 구현에서도 프로젝트를 재활성화했다. get_symbols_overview, find_symbol, find_referencing_symbols로 기존 발급 상태·Command·직접 Spawn·라우터 탐색·연관 테스트를 확인했다. 온보딩이나 메모리 변경은 수행하지 않았다.

초기 제한된 shell에서는 Editor가 조회되지 않았고 권한 확장 조회에서는 열린 Editor/Pipeline 미연결을 확인했다. 사용자가 Window → Pipeline → Start Server를 실행한 뒤 연결됐다. Editor 교체·패키지 업데이트·Play Mode·대체 headless 실행은 하지 않았다. 기존 Verify-Unity의 CompileOnly 모드 검출 결함과 호출 라벨 부재를 수정 범위에 넣지 않고 공식 CLI의 순차 명령을 직접 사용했다.

[최종 재컴파일 원본](../../../../Logs/QualityImprovement/F041/final-recompile-status.json)은 completed/failed:false/errors:[]/compilationFailed:false다. [최종 Editor 원본](../../../../Logs/QualityImprovement/F041/final-editor-status.json)은 ready/compiling:false/domainReloadInProgress:false/playMode:stopped다. [최종 어셈블리 최신성](../../../../Logs/QualityImprovement/F041/final-assembly-freshness.json)은 실행 어셈블리를 해당 비Editor 소스, 테스트 어셈블리를 Editor 소스의 최신 수정 시각과 각각 비교한 기록이다. 두 어셈블리가 각 소스보다 최신이다. 테스트 통과 뒤 남은 C# 설명 주석만 정리하고 컴파일을 다시 확인했으며 행동 코드를 바꾸지 않아 테스트를 반복하지 않았다.

기존 사례만으로 별도 비중첩 요청의 Stamp 중복과 후속 동률 차이를 판별할 수 없어 관련 기존 테스트를 좁혀 보강했다. 새 테스트/assertion은 일괄 필수 조건으로 삼지 않았다.

- Phase7PlacementCommandTests 23/23: 기존 18사례와 후보 수 1/4·자동/같은/다른 명시 Tick의 5사례. 요청 archetype 변경과 재사용 가능한 Entity 슬롯을 준비한 상태에서 현장→완공 표식과 실제 출고 예약 승자를 검사한다. [원본](../../../../Logs/QualityImprovement/F041/placement-status.json).
- Phase7BuildingLifecycleTests 11/11: 명시 접수번호 전체 보존, SubmitSpawn의 생략 발급, 원시 생략 요청의 묶음/좌표 비교, 명시 0/0 보존을 포함한다. [원본](../../../../Logs/QualityImprovement/F041/lifecycle-status.json).
- Phase6BeltDestinationReservationTests 49/49: 같은 Tick에서 앞 요청의 뒤 후보가 뒤 요청의 첫 후보보다 앞섬, 표식 동률/누락의 아래쪽 좌표 승자와 기존 외부 진입 정책을 검사한다. [원본](../../../../Logs/QualityImprovement/F041/reservation-status.json).
- Splitter/Merger의 기존 Test05 각 2/2: 다른 Tick과 같은 표식의 좌표 선택을 실제 아이템 전달/기준 연결/방향으로 검사한다. [Splitter 원본](../../../../Logs/QualityImprovement/F041/splitter-status.json), [Merger 원본](../../../../Logs/QualityImprovement/F041/merger-status.json).
- 드론 최초 현장 배정 2/2, 공급원 선택 3/3: 같은 Tick의 접수 묶음 우선, 실제 경로 거리 우선, 거리/표식 동률의 좌표 선택을 실제 배정으로 검사한다. [현장 원본](../../../../Logs/QualityImprovement/F041/drone-site-status.json), [공급원 원본](../../../../Logs/QualityImprovement/F041/drone-source-status.json).

합계 92/92이며 실패/Skipped/Inconclusive는 0이다. Play Mode·실제 입력/UI·Baker/SubScene·저장/불러오기·성능과 모든 Entity 재사용 패턴을 실행한 것은 아니다. 같은 좌표의 중복 건물 원본이나 표식 없는 현장의 기존 default 승계 의미를 새로 해결했다고 주장하지 않는다. 번호/Tick 소진·Submit 준비 예외·부분 기록의 모든 조합은 코드 확인이며 별도 실행 반례를 추가하지 않았다.

작업 시작 시 변경 사항이 없는 checkout을 사용했다. 기존 원본 평가/Q23/F-027 실행 기록은 보존하고 현재 AGENTS·직접 관련 컴포넌트 지도·명세·Task만 갱신했다. 작업 중 새로 나타난 Assets/Settings/Pipeline 설정 에셋/메타는 내용 수정·삭제 없이 보존했다. git diff --check는 통과했고 Task 체크 상태는 총 43·완료/추적 종료 38·미완료 5다. 커밋/스테이징은 하지 않았다.

## 2026-10-10 관련 이슈 후속 정리

구현 완료 뒤 별도의 사용자 요청으로 최신 Task·직접 관련 원본 평가·현재 소스와 위 원본 실행 기록을 대조했다. 이 후속은 문서만 변경하며 컴파일·테스트·성능 측정을 새로 실행하지 않았다. 위 92/92의 해당 사례를 관련 이슈의 회귀 근거로 연결한 것이며 별도의 통과 수로 합산하지 않는다. 추가 완료/종료 대상은 확인되지 않았고 Task 38/43·미완료 5건을 유지한다.

### 기존 완료에서 정리한 영향과 근거

- **F-027:** 요청 간 승인 순서·Strict/Partial·최종 승인 셀 공유와 후속 설치 순서를 구분하던 경계가 연결됐다. 같은 Tick의 접수번호를 현장과 완공 건물에 보존하므로 앞 요청의 마지막 후보도 뒤 요청의 첫 후보보다 우선한다. 배치 23/23 안의 기존 18사례는 기존 승인 계약의 회귀 근거이고 추가 5사례는 비중첩 요청→현장→완공→출고 경쟁을 확인한다. 과거 F-027의 24/24와 합산하거나 UI의 실제 입력 접수까지 검증했다고 주장하지 않는다.
- **F-029/F-034:** 설치 우선순위만 표식의 공통 비교로 바뀌었고 [예약](../../../../Assets/Scripts/Systems/Buildings/Reservation/BeltDestinationReservationSystem.cs)은 여전히 활성 BuildingOutput/RoutingTransfer 후보만 집계한다. 목적지당 외부 진입 하나와 [BeltEntryUtility](../../../../Assets/Scripts/Common/BeltEntryUtility.cs)의 공간 판정은 유지한다. 원본 예약 49/49의 FullName을 확인하면 기존 Test03 양쪽 승패 2사례와 Test07의 후보→예약→Apply/Ownership→ECB→Sync 40사례가 포함된다. 일반 T형 벨트 직접 합류·새 종류 우선권·새 공정성 규칙을 추가한 것이 아니다.
- **F-023:** 기존 Right (2,3) 배치→현장→Sync의 8사례가 배치 23/23에 포함된다. 직접 생성 11/11의 기존 Test09도 Right Crafter Spawn 뒤 기본 Size=(2,3) 보존을 검사한다. Test09는 컴포넌트 값만 확인하며 실제 Sync 셀 집합을 검사하지 않는다. 새 표식 승계 5사례의 건물은 1×1이므로 비정사각형 Completion·4방향 전체 셀 검증 제한은 유지한다.
- **F-026:** [배치 Command](../../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs)의 세 Map Writer 완료는 공통 Tick 도입 후에도 유지한다. 직접 Spawn 입력 준비의 CompleteDependency/Lookup 갱신은 공간 Writer 대기의 대체가 아니다. 배치 23/23 통과는 일반 경로의 회귀 근거이며 지연 Writer 주입·Validator 없는 실행을 새로 검증한 것은 아니다. F-028의 Fence 메타데이터 유지 결정도 변경하지 않는다.
- **F-024:** [완공 호출자](../../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs)는 확장한 표식 전체를 승계하면서 공통 Spawn의 Null이면 현장/자재를 보존하는 기존 경계를 유지한다. 새 5사례는 정상 Storage 완공과 출고 경쟁이다. 직접 생성 11/11의 기존 Test08 프리팹 누락 거부도 통과했으나 이것은 완공 현장의 도착 자재 보존 반례가 아니다. 과거 실패 검증과 현재 틱 전체 rollback 미보장은 그대로 보존한다.
- **F-037:** 표식 생략 준비가 직접 Spawn 앞에 추가됐지만 [공통 생성](../../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs)의 Crafter 필수 구성·레시피 미선택 0슬롯/빈 Whitelist를 변경하지 않았다. 직접 생성 11/11의 기존 Test03 초기 구성 통과는 해당 경로의 회귀 근거다. 과거 제작 연결 19/19, 실제 Baker 또는 전체 공사 자재 배송을 이번 실행으로 다시 확인했다고 기록하지 않는다.

### 남은 문제와 재작업하지 않는 경계

- **F-032 미완료:** 이번 변경은 기준 벨트/건물 선택의 설치 우선순위다. 두 Router의 FindItemAtBeltEnd는 여전히 원본 아이템의 World Owner·movement 존재·Progress만 확인하며 이동 enable을 검사하지 않는다. [일반 입고](../../../../Assets/Scripts/Systems/Buildings/Decision/BuildingItemInputDecisionSystem.cs)도 IgnoreComponentEnabledState로 입력을 읽는다. 목적지의 비활성 점유를 세지 않는 F-034나 설치 표식 동률 테스트는 원본의 이동 자격을 해결하지 않는다. 바닥 아이템의 복귀/수거 정책 결정은 유지한다.
- **F-022 미완료:** 공통 Spawn은 요청→Config→타입 기본 크기를 선택하며 조회한 dbFootprint를 사용하지 않는다. 새 표식과 직접 생성의 순서 발급은 DB Size 권위/편집 의미를 정하지 않는다. F-023 기본 크기 보존 테스트로 닫지 않는다.
- **F-025 미완료, F-021 종료 범위 유지:** 현재 EditorBuildSettings의 활성 장면은 여전히 SampleScene이다. 명시적 ECS 테스트 프리팹 DB로 실행한 Spawn은 해당 장면의 DB 이관/실제 Bake/Player 빌드 근거가 아니다. 작업 중 나타난 Pipeline 설정 에셋도 장면 DB 수리와 무관하다. 장면·Baker 검증의 기존 선택/제한은 바꾸지 않는다.
- **F-006 판단 보류:** 건물의 설치 표식 보존은 [ItemLifecycleApplySystem](../../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs)의 실패 후 마지막 productResults.Clear를 변경하지 않는다. 미실체화 생산 결과의 보존/소비 정책과 현재 틱 rollback을 새로 확정하거나 구현한 것으로 보지 않는다.
- **F-033 제외 유지:** 설치 순서의 공통화는 승인 이후 일반 출고의 실물/버퍼 유효성 문제를 다시 평가하거나 고친 작업이 아니다. 사용자가 제외한 이슈를 활성 목록이나 완료 구현 수에 넣지 않았다.

드론에서는 ComparePlacement를 사용하던 최초 공급 현장 정렬·동일 거리 공급원/목적지 선택·공급 대표 비교에 같은 계약이 적용된다. [EarlierTask](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs)는 공급끼리 설치 기록을 비교한 뒤 작업 CreationSequence를 사용하며 공급/회수의 두 대표 비교는 별도 기존 규칙이다. 거리 우선·적재품 최초 순서·작업 생성 순번을 설치 순서에 합치지 않았다. 실제 이동 Producer·저장/불러오기 결정성은 새 완료 이슈로 만들지 않았다.

이번에 검증한 Serena 호출은 프로젝트 재활성화, 비교/예약/Router 아이템 선택/드론 작업 비교의 find_symbol과 관련 파일 get_symbols_overview다. 신규 Compare의 find_referencing_symbols는 빈 결과였으므로 참조가 없다고 판단하지 않았다. 좁힌 rg 검색으로 예약·Splitter·Merger·DroneSchedulingUtility의 실제 네 호출을 확인했다. 입고 Execute의 body 요청은 출력 제한으로 전문이 반환되지 않아 활성 상태 관련 구간을 일반 검색으로 보완했다. 온보딩·메모리 변경은 수행하지 않았다.

Q15/Q16/Q23·F027-Verification·2026-10-06 전체 재검토에는 후속 상태 안내만 덧붙였다. 원본 평가 본문·당시 실행 수·위험/추정의 근거 수준은 보존한다. 코드·테스트·Pipeline 설정 203개 파일은 후속 시작 시 SHA-256과 모두 일치했고 기존 F-041 소스 변경과 미추적 파일을 되돌리거나 삭제하지 않았다. [보존 확인 원본](../../../../Logs/QualityImprovement/F041/followup-source-preservation.json)을 남겼으며 git diff --check도 통과했다.
