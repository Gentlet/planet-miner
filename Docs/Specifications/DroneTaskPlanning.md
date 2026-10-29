# 드론 공급원/목적지 계획과 네트워크 필터

2026-10-27 사용자 확정 정책을 따른다. 일반 반입/반출의 요청/작업/계획에 이어 2026-10-28에는 [작업 내 목표량 중재/정산 계약](DroneTransportReservations.md)을 연결했다. 작업 간 선택은 1A 순서로 통합했고 배정 공개를 기존 Publish에 연결했고 실제 운반은 9.10~9.12, 실제 경로/이동/거리 소비/관측과 최소 비상 귀환은 9.10에 연결했고 실제 충전은 9.14, 진행 중 취소/수행 불가 정산은 9.15에 남긴다.

## 요청과 일회성 요구

- 기존 ConstructionSupply/WorldItemRecovery에 StorageImport/StorageExport를 추가한다. 일반 반입은 Target이 도착 건물, 반출은 Target이 출발 건물이다. 상대 건물은 계획이 선택한다.
- 두 끝점은 Storage/MainFacility/DroneStation의 StoredItemElement 보관품/보관 공간만 지원한다. 같은 건물을 출발/도착으로 고르지 않는다. 제작기 입력/생산품, 발전기 연료 및 연구 재료 공급 계약은 확장하지 않는다.
- SubmitCreate의 quantity는 일반 운반에 필수인 양수 일회성 수량이다. 목표 재고 유지가 아니다. 기존 공급/회수 호출은 quantity를 생략한다.
- 중복 키는 (종류, 대상 Entity, 품목)이며 수량은 키에 포함하지 않는다. 중복은 기존 작업을 반환하며 목표량을 합산/수정하지 않는다. 수량 변경은 유휴 취소 후 다시 생성한다. 반입/반출과 다른 품목은 서로 다른 작업이다.
- DroneTransportRequirement.Quantity는 최초 요청 목표량이며 Execution이 Progress와 함께 root와 같은 EndSimulation ECB에 최초 게시한다. [목표량 확보/정산 계약](DroneTransportReservations.md)에 따라 완료량/진행 중 확보량/반환 예정 승인을 구분한다. 계획 작성으로 목표량을 줄이지 않는다.
- 요청/결과/생성 순번/deferred Entity와 유휴 취소/Busy/자동 생성 차단은 기존 작업 요청 계약을 재사용한다. 일반 운반은 명시적 생성만 지원하고 자동 재생성 Producer를 추가하지 않는다.

## 이번 틱 네트워크와 부분 footprint

- 정거장 Decision이 대표 Entity와 계산된 ActivityBounds를 함께 작성한다. 작업 Decision은 이 enable된 결과를 일시 NativeArray로 읽는다. ToComponentDataArray가 선행 ECS Writer 완료를 확보하며 별도 공유 캐시/소유자/Fence를 추가하지 않는다.
- 드론은 현재 관측 위치의 격자 셀이 들어 있는 네트워크로 판단한다. 고정 소속 정거장이나 영속 망 번호를 추가하지 않는다. 소속 영역의 합집합만 활동 영역이며 외곽 사각형의 빈 공간은 포함하지 않는다.
- 건물/현장은 회전한 footprint 중 한 셀이라도 해당 네트워크의 정거장 영역과 겹치면 허용한다. 기준점이나 전체 footprint 포함을 요구하지 않는다. 월드 아이템은 해당 셀로 검사한다. 경계/footprint 합산은 long을 사용한다.
- 출발/도착 모두 드론과 같은 네트워크에 있어야 한다. 여러 망에 걸친 큰 건물은 각 망에서 이용할 수 있지만 망을 연결하지 않는다. 망 연결은 정거장 간 기존 겹침 규칙만 소유한다.
- 새 정거장/철거/비활성화/망 분리는 이번 틱 신규 배차와 적재품 재배정 판단에 반영한다. 이전 공개 DroneStationSpatialIndex를 같은 틱 필터에 사용하지 않는다.
- 안전 방출의 외부 평가 좌표도 현재 드론과 같은 망의 셀이어야 한다. 망 밖 좌표는 방출 후보로 승인하지 않으며 유효한 결과를 기다리는 동안 적재품을 보존한다. 현재 위치의 망 자격은 재배정 진입에서 확인한다.
- 범위 밖이거나 현재 위치에 망이 없는 작업은 후보를 만들지 않고 대기한다. 망 변화만으로 root를 닫거나 진행 중 배정을 취소하지 않는다. 현재 망 밖의 적재 수행자는 실물을 보존하며 대기한다. 실제 수행 불가 중단 정산은 9.15의 책임이다.

## 계획과 수량

- 반입은 재고가 있는 출발 보관처, 반출은 공간이 있는 도착 보관처를 평가한다. 전체 ViaSource 경로 거리와 동률 PlacementStamp 기준을 재사용한다. 재배정은 기존 Direct 경로/현장 우선 규칙을 유지한다.
- 경로 입력이 없거나 아직 미평가이면 기다린다. 단순 직선거리로 도달 가능성을 만들지 않는다. 필요한 경로 중 미평가가 있으면 결과를 모두 기다린다.
- 일반 운반은 요청량/공통 적재량/현재 출발 재고/도착 수용 공간의 최솟값으로 이번 판단의 수량 상한을 작성한다. 공간 판정은 기존 품목 필터/입력 슬롯/정박 점유/드론 전용 슬롯/MaxStack 규칙을 공유한다. 전체 요청량을 한 번에 수용해야 한다는 제한은 없다.
- 재고/공간 변화와 대상 소멸을 매 판단에 반영한다. 재고/공간이 부족하면 목표량과 작업을 보존하고 기다린다. 계획만으로 실물/Owner/용량/예약량은 변경하지 않는다.
- DroneTransportPlanDecisionElement는 일반 운반 전용 버퍼다. 기존 Candidate 형식을 재사용하며 공급/회수/적재 재배정의 Decision 후보 버퍼와 분리한다. DroneTaskReservationSystem은 두 버퍼를 함께 읽어 수행자당 하나 선택한다. 일반 운반은 선택분만 전체 목표량 안에서 별도 미공개 확보 기록으로 작성한다. 선택된 승인은 기존 Assignment Publish가 최종 검사 후 EndSimulation에 배정으로 공개한다.
- 배정 공개와 확보 인계는 구현되어 있으며 자동 픽업 신호/실제 수집량과 미수집 확보량 반환은 [픽업 계약](DronePickup.md)에 연결했다. 실제 자동 납품/완료량 정산은 후속이다.  목표 잔량은 확보/정산 계약이 관리한다. 계획을 운반 완료로 표시하지 않는다.

## 소유자와 공개 경계

| 자료 | Writer | 소비/수명 |
| --- | --- | --- |
| DroneStationNetworkDecision.ActivityBounds | 기존 정거장 Decision Job | 대표와 함께 매 틱 초기화/작성, 작업 Decision이 같은 틱 읽음 |
| DroneTaskRequest.Quantity | 시뮬레이션 전 Submit | 불변 요청, EndSimulation에 소비 |
| DroneTransportRequirement | Task Execution | 작업과 같은 EndSimulation에 최초 생성, 작업 수명 |
| DroneTransportProgress | Task Execution 초기화 / Task Reservation 확보 / 후속 납품 정산 | root의 완료량/미공개 및 진행 중 확보량, 작업 수명 |
| DroneTransportPlanDecisionElement | Task Decision | 후보 singleton에 생성, 매 Decision Clear/재작성, 통합 Reservation 선택 입력 |
| DroneTransportPendingReservationElement | Task Reservation의 단일 Burst Job | 선택된 일반 운반만 보관하며 다음 Reservation 반환 또는 후속 소비자 인계까지 보존, Decision은 읽기만 함 |
| 작업/경로/결과 | 기존 Execution/Lifecycle | 기존 EndSimulation 공개, 다음 틱 이용 |

Task Decision/Execution과 기존 작업 후보 선택의 메인 스레드 ISystem 경로를 유지하며 일반 운반 목표량 반환/승인은 선택 뒤 단일 Burst Job으로 처리한다. 기존 정거장 Burst Job 체인과 EndBuilding/EndSimulation 경계를 변경하지 않는다. 실제 이동/배터리/충전 경유 경로 및 그 상태 변화에 따른 평가 무효화는 9.10의 Producer가 연결해야 한다.

## 검증 상태

해당 feature/drone-task-9-5 작업본의 Unity 6000.4.11f1/CLI 1.0.0-beta.14에서 컴파일 오류 없음/최신 runtime 및 Editor DLL, Editor ready/비컴파일/비리로드/Play stopped를 확인했다. 관련 선택 EditMode의 최신 고유 사례는 135/135이며 실패/생략/불확정은 0이다.

선택 범위는 DroneTaskPlanningTests 6, DroneTaskRequestTests 18, DroneTaskSchedulingTests 40, DroneRetargetingTests 18, DroneLifecycleIntegrationTests 6, DroneItemTransferTests 33, DroneStationNetworkTests 14다. 동일 fixture의 이전 실행을 합산하지 않는다. 신규 계획 핵심 흐름과 기존 통합 fixture의 네트워크 입력/진행 중 운송 보존/망 밖 방출 거부를 검사한다.

초기 독립 6역할 검수의 망 밖 외부 방출 좌표 허용 P2는 기존 재배정 테스트 확장에서 1건 실패를 재현한 뒤 수정했다. 컴포넌트 색인 표 P3도 수정했다. 영향 범위의 Decision/guard/경계 v2 재검수에서 두 지적 해소와 추가 지적 없음을 확인했다. 검증 래퍼의 기존 null 필터 판정과 현재 Pipeline 그룹형 명령 차이는 ignored Temp 사본에서 호환시켰고 저장소 운영 도구는 수정하지 않았다.

이전 Task의 성공 건수와 합산하지 않는다. 초기 구현 검증에서는 Play Mode를 수행하지 않았으며 아래 추가 검증과 구분한다. 실제 비행/충전/Player/성능 측정은 후속이다.


### 추가 Play Mode 검증 (2026-10-27)

사용자 요청으로 창이 있는 Unity Editor(batchmode=false)의 실제 V2 Test Scene/Default World에서 검증했다. 프레임 18,972→62,186과 World 시간 199.562→669.311초의 진행을 확인했다. 최초 백그라운드 Editor 확인과 구분하며 실제 UI Editor에서 다시 실행했다.

- 제품 SpawnBuildingRequest/SpawnItemRequest/DroneSpawnUtility로 별도 위치의 정거장/저장고/아이템/드론을 준비하고 공통 SubmitCreate/SubmitCancel 및 실제 게임 그룹/EndSimulation을 통과시켰다.
- 반입/반출 계획, 중복 Existing 반환과 목표량 5 보존, 계획량 3/2, 서로 다른 망의 상대 건물 제외, 배정 없음과 재고/화물 비변경, 정거장 비활성 시 계획 대기/복구 시 재계획, 유휴 취소 결과/작업 제거를 확인했다.
- 지속 관측/경로 Producer는 미구현이므로 CanAcceptTask와 경로 결과/거리는 명시적 검증 입력이다. 자동 비행/실제 거리 계산이나 충전 성공으로 해석하지 않는다.
- 현재 저장고 설정은 1x1이다. 일부 footprint 포함/회전/미겹침/망 밖 셀 거부는 실제 정거장 Decision을 읽는 일시 geometry 프로브로 별도 확인했다. 해당 엔티티는 같은 eval 안에서 제거했으며 공사 생성/운반 전체 흐름으로 확대하지 않는다.
- 실행/종료 구간의 오류와 공통 Fatal은 0건이다. Lookup을 OnUpdate에서 생성한다는 성능 권고 경고 7건(Power 4/Drone 3)을 기록했다. 성능 측정이나 경고 수정은 수행하지 않았다.
- 검증 후 Play를 정상 종료했고 장면 dirty=false/원래 카메라 위치 복원을 확인했다. 게임 코드/장면 에셋은 수정하지 않았다. 결과 JSON과 카메라 렌더 캡처는 ignored Logs/Task95/PlayMode에 보관한다. 이는 NUnit PlayMode 테스트 건수가 아니며 EditMode 135/135와 합산하지 않는다.

### 통합 자동 선택 검증 (2026-10-28)

확정 1A 순서로 공급 선두(PlacementStamp→CreationSequence)와 회수/반입/반출 선두(CreationSequence)를 생성 순번으로 비교한다. 적재 재배정은 이 신규 선택보다 먼저 수행한다. 같은 수행자의 미선택 운반 대안은 목표량을 선점하지 않고, 앞선 수행자의 선택으로 잔량이 소진된 작업은 다음 가능한 후보로 건너뛴다. 일반 운반은 선택된 승인만 보관하며 배정 공개는 아래 후속 검증의 일반 운반 Publish가 담당한다.

Unity 6000.4.11f1/CLI 1.0.0-beta.14의 원본 architecture-v2에서 C# 컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready와 최신 runtime/Editor DLL을 확인했다. 관련 선택 EditMode는 151/151(실패/생략/불확정 0)이다. Scheduling 44/Planning 14/StationLifecycle 18/Request 18/Retargeting 18/ItemTransfer 33/LifecycleIntegration 6이며 이전 실행과 합산하지 않는다. 기존 혼합 사례를 회수/반입/반출/미도달 후보로 확장하고 같은 틱 세 드론의 공급/반입/반출 선택, 미선택 목표량 보존, 목표량 소진 뒤 다음 작업 선택, 유휴 취소 후 갱신을 확인한다.

초기 C# 로컬 NativeHashMap 수명 선언 오류는 수정 후 재컴파일했으며 오류 어셈블리로 테스트하지 않았다. 기존 검증 래퍼의 인자 처리 실패는 컴파일 실패와 구분하고 현재 grouped CLI 명령으로 실행했다. 실행 원본은 ignored Logs/Task98에 보관한다. Play Mode/Player/Burst AOT 전체/성능은 수행하지 않았으며 실제 경로/이동/납품 Producer를 실행한 증거가 아니다.

Reservation/컴포넌트/guard/경계/가독성/주석 6역할의 독립 검수에서 확인한 범위의 필수 수정 지적은 없다. 최종 Editor 콘솔 오류는 0건이며 격리 테스트의 그룹 순서 관련 경고 등을 포함해 221건이 남았다. 누적 Pipeline 로그 개수와 현재 Editor 콘솔 개수는 구분한다. Editor는 ready/비컴파일/비리로드/Play stopped 상태로 열어 두었다.

### 일반 운반 배정 공개 검증 (2026-10-28)

확정 1A/2A에 따라 Assignment Publish가 최신 자격/수량을 확인하고 승인량 안의 양수분만 배정한다. 감량 차액/거부분은 즉시 반환하고 실패한 후보는 다음 Decision부터 재선택한다. Take 후 원본 승인은 0이며 PublicationQueued는 ECB 기록 완료다. 배정/유형별 확보 기록/수행자 연결은 같은 EndSimulation에 공개한다. 정거장 Reservation/Apply는 일반 운반 승인과 기록 완료 표시를 모두 Busy로 읽는다.

원본 architecture-v2/기준 bb876fa의 후속 변경을 Unity 6000.4.11f1/CLI 1.0.0-beta.14에서 검증했다. C# 컴파일 completed/failed false/errors 없음/compilationFailed false, 최신 runtime/Editor 어셈블리와 Editor ready/비컴파일/비리로드를 확인했다. 관련 선택 EditMode는 161/161(실패/생략/불확정 0)이며 Scheduling 52/Planning 14/StationLifecycle 20/Request 18/Retargeting 18/ItemTransfer 33/LifecycleIntegration 6이다. 앞선 151개나 중간 실행과 합산하지 않는다.

새 자동 배정 흐름은 Scheduling fixture의 실제 그룹/ECB로 검증한다. 계획/정산 fixture는 의도적으로 Publish 전까지 진행해 승인/반환 API와 수명 경계를 따로 검사하며, 적재 재배정 회귀는 Publish를 포함한다. 기존 혼합 선택 사례의 일반 운반 기대값을 실제 배정 공개로 갱신했고 반입/반출 3+2 인계와 다음 틱 보존, 감량/0 및 대상 무효 반환, 반복 공개, 다음 틱 재선택, 인계 후 유휴 취소 Busy와 수납 차단을 확인했다.

초기 Scheduling 50개 중 반출 실패 뒤 다음 틱에 반드시 다른 root를 선택해야 한다는 기대값 1건이 실패했다. 기존 정책상 반출 root가 새 목적지를 선택할 수 있으므로 기대값을 바로잡았고, 대상 무효 사례까지 보완한 최신 선택 161개를 재실행했다. 게임 정책을 바꾸거나 실패 테스트를 제외해 통과시키지 않았다. 최종 Editor 콘솔 오류는 0건/경고 243건이며 격리 World의 UpdateAfter 대상 부재 경고 등이 남았다. 누적 Pipeline 로그 개수와 현재 Editor 콘솔 개수는 구분한다. 원본 실행 기록은 ignored Logs/Task99에 보관한다.

Play Mode/Player/Burst AOT 전체/성능은 수행하지 않았다. 실제 경로/배터리/지속 관측은 9.10, 일반 운반 수집/납품은 9.11~9.12 후속이다. 최초 관측 CanAcceptTask=false를 임의로 열거나 실제 이동/도착/실적을 자동 성공으로 만들지 않는다.

배정 반영/컴포넌트/guard/경계/가독성/주석의 6역할 독립 검수에서 코드의 필수 수정 지적은 없었다. 주석 검수에서 일반 반입/반출의 최초 CollectFromStorage 설명 누락 P3를 보완했으며 Source/Destination의 출발/도착 의미와 root Target 방향을 구분했다. 코드는 위 컴파일/161개 테스트 이후 변경하지 않았다.

실제 경로/이동 Producer가 연결된 이후의 계약과 검증은 [이동 명세](DroneMovement.md)를 따른다. 위 9.5~9.9의 경로 직접 입력/이동 미수행 기록은 당시 범위로 보존한다.
