# F-032 비활성 원본 아이템 선택 개선

날짜: 2026-10-10. 사용자 확정 정책 구현·코드 확인·Unity 컴파일 완료. F-032 완료이며 게임 실행 재현은 미검증이다.

## 확정한 동작과 범위

건물 입고·Splitter·Merger는 월드 소유이며 실제 `BeltMovementState`가 활성인 종단 실물만 선택한다. 활성은 벨트 운반 참여 상태이며 이번 틱에 전진한 거리와 다르다. 앞이 막혀 전진량이 0인 활성 실물은 정상 후보로 유지한다. 비활성 실물은 잔여 Progress가 끝을 가리켜도 선택하지 않는다.

기존 바닥 회수 후 완공·정상 출고 복귀를 유지한다. 새 자동 복귀 기능·새 시스템/컴포넌트/Utility/공간 캐시·별도 아이템 인계 경로를 추가하지 않았다. F-034의 목적지 공간, F-041 설치 순서/동률/누락 비교, 현재 Command 철거 승인 정책, 제외된 F-033, 일반 T형 직접 합류 제외와 속도 상한은 유지한다.

## 구현과 책임

- [BuildingItemInputDecisionSystem](../../../../Assets/Scripts/Systems/Buildings/Decision/BuildingItemInputDecisionSystem.cs)의 Job은 매 판단 시작에 `new BuildingItemInputDecision(Entity.Null)`과 결정 비활성을 기록한다. 생성자의 CanDeposit=false·슬롯 -1을 사용하며 모든 거절 분기는 이 상태를 유지한다. `EnabledRefRO<BeltMovementState>`로 이동 활성을 별도 검사하고 기존 Owner·벨트·진행도·철거 승인·Storage·제작기 대기·필터 조건을 통과했을 때만 후보를 켠다.
- 비활성 입고 결정을 다음 틱에 다시 판단하도록 `IgnoreComponentEnabledState`는 유지했다. 이동 상태 읽기와 프레임 결정 활성은 별개다. 기존 공간 Map/Fence와 Job 읽기·쓰기 계약을 사용한다.
- [Splitter](../../../../Assets/Scripts/Systems/Buildings/Decision/SplitterDecisionSystem.cs)와 [Merger](../../../../Assets/Scripts/Systems/Buildings/Decision/MergerDecisionSystem.cs)의 `FindItemAtBeltEnd`는 Owner·이동 컴포넌트 존재를 확인한 뒤 lookup의 `IsComponentEnabled`를 검사한다. 기존 종단 임계값·최대 진행도 선택·동률 처리와 설치 표식 비교는 유지한다. 기존 Execute 시작의 전달 결정 초기화·비활성화도 유지한다.
- Decision은 후보만 바꾼다. 슬롯/목적지 승인은 기존 Reservation, 실물 버퍼·Owner·위치·이동 상태·커서 적용과 결정 소비는 기존 Apply 소유자에게 둔다. 다른 시스템 내부 메서드 호출이나 새 UpdateBefore/After를 추가하지 않았다.

## 상태 조합의 코드 확인

아래는 실행 결과가 아니라 변경 후 분기와 기존 소비 경로를 확인한 내용이다.

- 월드·이동 활성·Progress=0은 종단 후보가 아니다. 기존 일반 벨트 이동은 계속 참여한다. 월드·이동 활성·Progress=1은 연결·필터·용량/공간 등 기존 조건을 만족하면 입고 또는 Routing 후보가 된다.
- 월드·이동 비활성·Progress=0/1은 모두 거절한다. 입고/전달 결정은 비활성이며 이 선택 경로가 실물 위치·Owner·이동 상태를 바꾸거나 Router 커서를 진행시키지 않는다.
- 수납·이동 활성/비활성·Progress=0/1의 네 조합은 모두 Owner 조건으로 거절한다. 이동 비활성인 경우에는 더 앞의 이동 자격 검사에서 거절된다.
- 이전 활성 입고 결정이 있더라도 다음 판단에서 대상·CanDeposit·슬롯·활성을 모두 초기화한다. 이후 원본이 이동 비활성이면 Reservation/Apply에 이전 후보를 넘기지 않는다. 다시 유효해지면 비활성 프레임 결정도 재판단하여 새 후보를 만들 수 있다.
- 정상 입고는 기존 저장 버퍼 추가·이동 정지·Transfer·Owner/렌더 적용을 사용하며 입고 Apply 자체는 GridPosition/LocalTransform을 새로 옮기지 않는다. 정상 Routing은 기존 월드 Owner를 유지하고 목적지 벨트 입구의 위치·방향·Progress=0·이동 활성 및 성공 커서 갱신을 사용한다. 정상 출고는 기존 보관품을 벨트 입구로 옮겨 Progress=0·이동 활성·World Transfer를 기록한다.

## 철거·재설치와 레거시 경계

[철거](../../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs)는 해당 셀에 남은 벨트 실물의 이동 비활성화를 EndBuilding에 기록하고 Progress와 위치를 보존한다. Command의 PendingBuildingDemolition을 읽는 기존 후보 차단은 유지한다. Progress를 0으로 지우는 철거 내부 정리에 입고/Routing 자격을 의존시키지 않았다.

[현재 배치](../../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs)는 바닥 실물이 있어도 현장을 만들 수 있지만 [완공](../../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs)은 footprint 내부의 최신 월드 실물이 없어질 때까지 기다린다. 기존 드론 현장 회수와 마지막 회수 다음 틱 완공 계약을 유지한다. 실제 드론 이동·행동 신호 Producer는 후속이다. 같은 타입의 기존 벨트 덮어쓰기는 방향 변경이며 아이템 이동을 켜지 않는다.

직접 Spawn은 공사 바닥 정리 검사를 거치지 않는다. 유효한 Config/DB로 남은 바닥 실물 위에 벨트를 직접 만들 수 있지만 생성은 기존 실물의 이동을 켜지 않으며 F-032 변경 후 입고/Router도 그 비활성 원본을 거절한다. 따라서 Q14의 철거→재설치 예시는 현재 일반 공사 경로에서 실물이 그대로 남은 채 벨트가 완공되는 실행 사례로 단정할 수 없다. 직접 Spawn의 조건부 경로는 코드상 성립하며 실행 재현하지 않았다.

사전 분석에서 현재 소스에 없는 레거시를 삭제 직전 커밋 `f85d2ca`로 확인했다. `BuildingDestroySystem`→`ChunkMapSystem.TryUnregisterBuilding`은 벨트 등록·활성 셀만 제거하고 월드 실물을 남겼다. `TryRegisterBelt`는 아이템이 있는 셀을 활성 이동 셀에 넣었고 `BeltMoveSystem`은 그 셀의 실물을 처리했다. 당시 배치/완공에는 현재 바닥 정리 조건이 없었다. 플레이어 철거 호출자 `DemolitionAreaRequestUtility`는 같은 영역의 실물 회수 요청도 만들었다. 따라서 남은 실물의 재등록 후 자동 이동 편입은 코드로 확인했지만 모든 실물이 항상 재운반됐다는 게임 실행 근거는 아니다. 레거시 입고/라우팅은 위치·월드 등록과 StoredItem/Disabled 제외를 사용했으며 V2의 아이템별 이동 비활성과 같은 계약이 아니었다. 과거 코드를 복원하지 않았고 자동 복귀를 V2에 이식해야 한다는 명시적 의도도 확인하지 못했다.

## 검증 결과와 제한

- Serena `initial_instructions`를 읽고 절대 프로젝트 경로로 활성화했다. 세 대상 메서드와 입고 OnCreate·결정 생성자를 `find_symbol`로, Job/선택 메서드의 직접 호출자를 `find_referencing_symbols`로 확인했다. 변경한 세 파일의 `get_diagnostics_for_file`에서 Error 진단은 없었다. [진단 기록](../../../../Logs/QualityImprovement/F032/serena-errors.json). 온보딩·Serena/사용자 메모리 변경은 하지 않았다.
- `Verify-Unity.ps1`의 현재 CompileOnly 선택 조건에는 null TestFilter 계산 문제가 명시되어 있어 컴파일 전용 검증에 개별 CLI 명령을 사용했다. 처음 Pipeline 연결 부재로 요청을 실행하지 못했으나 사용자 Editor 활성화 후 연결을 확인하고 재컴파일 요청/상태를 검증했다. [연결](../../../../Logs/QualityImprovement/F032/connection-status.json), [요청](../../../../Logs/QualityImprovement/F032/recompile-request.json).
- [최종 컴파일](../../../../Logs/QualityImprovement/F032/recompile-status.json)은 외부 success=true/errors:[]이며 내부 문자열 결과를 파싱하면 up_to_date/failed:false/errors:[]/compilationFailed:false다. [최종 Editor](../../../../Logs/QualityImprovement/F032/editor-status.json)는 ready/compiling:false/domainReloadInProgress:false/playMode:stopped다.
- [실행 어셈블리 최신성](../../../../Logs/QualityImprovement/F032/assembly-freshness.json)은 `Assembly-CSharp.dll`이 최신 Assets/Scripts C# 소스보다 새것임을 확인했다. 컴파일 확인 뒤 C#을 추가 변경하지 않았다.
- 새 테스트 작성/assertion 보강·EditMode·Play Mode·실제 철거/재설치·드론 이동·Player 빌드·성능은 실행하지 않았다. 상태 조합의 코드 확인과 실제 실행 근거를 구분한다. 기존 F-034/F-041 테스트 통과 수는 F-032 검증 수로 합산하지 않는다.

최신 Task에 F-032 한 건만 완료로 반영하여 39/43·미완료 4건으로 갱신했다. Q14/Q16·F041-Verification·2026-10-06 재검토에는 후속 안내만 추가하고 원본 평가·당시 실행 수는 보존한다.

## 2026-10-10 관련 이슈 후속 정리

구현 완료 뒤 별도의 사용자 요청으로 직접 연결된 현재 코드·최신 Task·원본 평가와 기존 F-032 컴파일 기록을 대조했다. 이번 후속은 문서만 변경한다. 추가 완료/종료 대상은 확인되지 않았고 39/43·미완료 4건을 유지한다.

### 기존 완료 범위에 연결한 영향

- **F-007/F-031/F-037:** 입고 시작 초기화와 활성 원본 검사 뒤 유효 후보만 기존 슬롯 예약에 전달한다. BuildingStorageInputReservation의 활성 결정 쿼리, 원본 MaxStack 조회와 0 이하 한도 거절, PendingSlot 품목/수량, 제작기 전용 슬롯은 유지한다. 후보 자격과 슬롯 승인·Crafter 생성 구성은 별개다. 이번 변경/컴파일을 새로운 용량·Owner·제작 통합 실행으로 해석하지 않는다.
- **F-029/F-034:** BeltDestinationReservation의 후보는 계속 활성 BuildingOutput/RoutingTransfer이며 일반 BeltMovementDecision은 포함하지 않는다. CheckTargetSpace는 기존 BeltEntryUtility.HasEntrySpace를 호출한다. 비활성 원본은 F-032에 따라 선택하지 않고 비활성 목적지 실물은 F-034에 따라 활성 벨트 점유로 세지 않는다. 목적지당 외부 진입 한 개/틱과 일반 T형 직접 합류 제외를 유지한다. Task의 오래된 F-032 미완료 안내를 갱신했으며 추가 합류 해결/새 공정성 정책/새 점유 실행 수는 없다.
- **F-030:** 공통 건물 생성의 MaxBeltSpeed 상한을 재확인했다. 원본 자격 검사에 PlannedProgress나 IsBlocked를 사용하지 않으므로 이번 틱 전진량이 0인 활성 실물은 유지한다. 속도·간격·다다음 셀 조회·일반 이동의 진행도 적용을 바꾸거나 새 고속 이동 실행을 확인한 작업이 아니다.
- **F-004:** 현재 철거는 Command가 이전 Transfer/ProductResult를 정리하고 PendingBuildingDemolition을 게시하여 같은 틱 후보를 앞단에서 차단한다. 입고도 원본/수신자의 승인 상태를 거절한다. F-032는 여기에 비활성 원본 자격만 추가했으며 Owner/렌더/버퍼 Writer나 ECB 순서를 다시 조정하지 않았다. 2026-10-02의 입고 후 반환·2/2는 당시 기록이고 이후 앞단 차단 정책의 이번 실행 근거가 아니다. 현재 정책은 [철거 승인·동작 중단 기록](BuildingDemolitionStop-Verification.md)을 함께 따른다.
- **F-020/F-024:** SpawnPrefabItem의 비활성 이동/입고 초기값과 일반 Spawn·생산품·신규 환급의 공통 초기화는 유지한다. 기존 실물 반환과 신규 환급을 구분하고 정상 출고의 명시적 복귀를 사용한다. Construction 완료는 최신 월드 실물의 회수 대기를 검사하고 Spawn non-Null 이후에만 자재/현장을 삭제한다. F-032를 자동 수거/재설치 운반 추가나 새 완공 실패 보존 실행으로 확대하지 않는다.
- **F-041:** 원본 아이템의 활성 자격과 기준 벨트/발신 건물의 설치 우선순위는 서로 다른 선택이다. 목적지 예약의 PlacementStamp.Compare, Router의 기존 기준 연결·포트 순환·성공 커서 갱신은 유지한다. 기존 92/92는 F-041 실행이며 비활성 원본·철거/재설치의 F-032 실행으로 합산하지 않는다.

기존 Map/Fence의 읽기 대기·Reader 등록·Job 의존성과 상태/결정 소유자도 유지된다. 원본 선택이 비활성 Progress의 내부 정리 방식에 기대던 결합은 제거했지만 맵 재구축·새 캐시·Reader 병렬화나 성능 개선을 검증한 것은 아니다.

### 유지하는 미완료와 제외 경계

- **F-006 미완료:** ProductResultApplyJob은 프리팹 누락/실패 기록 뒤 마지막 productResults.Clear를 수행한다. 원본 선택과 입고 결정 정리는 미실체화 생산 결과의 보존/소비 정책이나 현재 틱 rollback을 정하지 않는다.
- **F-017 미완료:** 월드 설정 반경/초기 크기/region/transition의 지원 상한과 소비자 산술은 변경하지 않았다. F-032의 진행도 비교·활성 검사로 정수 overflow 문제가 해결된 것은 아니다. 이번 후속에서 전체 산술을 새로 재감사하지 않았다.
- **F-022 미완료:** 현재 SpawnBuilding은 요청→Config→타입 기본 크기를 선택하며 조회한 dbFootprint를 사용하지 않는다. 직접 Spawn 아래의 비활성 원본 거절은 크기 출처의 권위/DB 편집 의미를 정하지 않는다.
- **F-025 미완료:** 활성 EditorBuildSettings 진입은 여전히 SampleScene이다. 원본 자격 검사와 기존 명시적 ECS DB 컴파일은 해당 장면의 DB 이관·실제 Bake·Player 빌드 근거가 아니다.
- **F-033 제외 유지:** 입고/Router 원본의 이동 활성 검사와 일반 출고 승인 이후의 실물/버퍼 유효성은 다른 문제다. 일반 출고 Apply를 수정하거나 새 실행 재현을 찾은 작업이 아니므로 제외 상태를 유지하며 활성 목록/완료 수에 넣지 않는다.

### 문서 반영과 검증 경계

Task의 직접 관련 항목과 남은 네 항목에 영향/한계를 연결하고 Q13/Q14/Q15/Q16·F004-Verification·F041-Verification·2026-10-06 재검토의 후속 안내를 정리했다. 원본 평가 본문·당시 실행 수·F-032 구현 변경과 기존 작업 트리는 보존한다.

기존 F-032 컴파일 원본의 up_to_date/failed:false/errors:[]/compilationFailed:false, 당시 ready/비컴파일/비리로드 및 어셈블리 최신성 기록을 다시 읽었다. 이는 앞선 구현 시점의 같은 기록이며 새 컴파일/테스트/성능 결과가 아니다.

후속 시작 시 보호 대상으로 수집한 C# 원본/테스트·설정 JSON·패키지 manifest/lock·빌드 장면 설정 213개 파일은 종료 시 SHA-256과 모두 일치한다. [보존 확인 원본](../../../../Logs/QualityImprovement/F032/followup-preservation.json)을 남겼다. 새 테스트/assertion·컴파일·EditMode·Play Mode·실제 재설치·Bake·빌드·성능 측정은 수행하지 않았다.
