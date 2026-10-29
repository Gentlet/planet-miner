# 드론 경로 평가/이동/관측 계약

2026-10-29 사용자 확정 1A/2A/3A/4A를 적용한다. 기준 코드는 Task 9.9의 87a0404이며 이번 범위는 실제 경로 평가/이동/거리 소비/관측/도착과 최소 비상 귀환이다. 자동 픽업은 [픽업 계약](DronePickup.md)에 연결했고 자동 납품은 [납품 계약](DroneDelivery.md)에 연결했고 정상 귀환/정박/수납 자동 흐름은 9.13, 실제 전력 기반 충전은 9.14에 유지한다.

## 확정 정책

- 1A: XY 평면의 직선 자유 비행이다. 건물/자원/벨트를 비행 장애물로 취급하지 않는다. 충전 경유가 필요하면 실제 직선 구간의 합을 비교한다. 충전 장소/에너지를 무시한 단순 거리를 성공 결과로 만들지 않는다.
- 2A: 건물/현장/정거장의 목표는 회전된 footprint 중심이다. 월드 아이템과 안전 방출은 원래 격자 위치다. 평가와 실제 이동이 같은 목표점 함수를 사용한다. 이동은 Z를 보존한다.
- 3A: 경로/에너지 계획, 실제 이동/관측, 도착/중단 연결과 배터리 0의 최소 비상 귀환을 제공한다. 충전 정거장에 도착해도 자동 정박/수납/충전 성공을 만들지 않는다. 충전 경유와 실제 중단은 구분한다.
- 4A: 현재 위치/출발/도착의 기존 네트워크 후보 자격과 부분 footprint 허용은 유지한다. 이미 시작한 비행이 중간의 미포함 셀을 통과했다는 이유로 취소하지 않는다. 안전 방출 목표는 여전히 현재 망 안의 활성 현장 외부다.
- 현재 Resources/Config/DroneConfig.json의 speed=4, emergency speed=0.5, maximum battery=100, distance consumption=1을 사용한다. 이동 세 수치는 명시적 finite 양수로 검증한다. 설정 누락/중복/무효 값은 기존 공통 Fatal/원본 보존/자동 재시도 없음 계약을 따른다.

## 소유자와 실행/공개 순서

| 시점 | 소유자/출력 |
| --- | --- |
| Initialization | DroneConfigInitializationSystem이 기존 적재량/최대 배터리와 DroneMovementConfig를 모두 검증한 뒤 한 번 게시한다. 유효한 기존 singleton을 덮어쓰지 않는다. |
| Preparation | DroneWorkerObservationPreparationSystem이 마지막 확정 LocalTransform/배터리/수행자 연결/화물/충전 경유 상태를 관측으로 갱신한다. 값이 달라질 때만 Revision을 증가시킨다. |
| DroneDecision | 정거장 NetworkDecision 뒤 DroneFlightDecisionSystem이 임시 정거장/현장 자료를 만들고 경로 판단과 진행 중 이동 의도를 병렬 Burst Job으로 작성한다. Task Decision은 이번 판단을 내부 입력으로 읽는다. |
| DroneReservation | 기존 Task Reservation이 이전 무효 예약과 새 후보를 정산한다. 이전 이동 실패 결과는 같은 배정/revision에만 무효화 근거다. |
| DroneExecution | Task Execution은 Create/Retain/Remove 의도를 따라 유지되는 경로 결과만 EndSimulation에 기록한다. Movement Execution은 이번 이동 의도와 Task Decision의 무효화 의도를 소비해 실제 위치/격자/LocalToWorld/배터리/활동/결과를 쓴다. |
| DroneStateApply | 기존 Lifecycle이 예약/배정 정리를 소유한다. Movement Apply는 그 뒤 도착 결과를 같은 배정/revision/action에 한정해 AwaitingCollection/Delivery로 ECB 기록한다. 기존 Publish가 새 배정/재배정을 마지막에 기록한다. |
| EndSimulation | 경로 요청/공개 결과와 도착 상태/배정을 확정한다. 새 배정은 다음 틱부터 이동하며, 관측은 다음 Preparation에서 실제 위치와 일치시킨다. |

이동 중 연속 좌표는 LocalTransform에 있고 GridPosition은 그 좌표의 floor 격자로 같은 Execution Job에서 함께 갱신한다. 렌더 LocalToWorld도 같은 원본으로 갱신한다. Observation은 작업 관리의 입력이며 위치나 배터리 원본을 중복 정산하지 않는다.

## 경로 계획과 에너지 대기

DroneRouteEvaluationDecision은 이번 틱의 내부 판단이다. ReadCurrentRouteResult는 준비된 이번 판단을 읽고, 외부 공개 DroneRouteEvaluationResult는 기존 EndSimulation까지 보존한다. 명시적 외부 결과를 제공하는 기존 격리 fixture도 그대로 사용할 수 있다.

Direct는 현재 위치에서 목표까지, ViaSource는 현재 위치→수집점→납품점 전체를 평가한다. 정거장 후보는 이번 틱의 활성 NetworkDecision과 현재 저장 슬롯/정박 점유를 사용하며 철거 승인/비활성 정거장은 제외한다. 충전 경유에는 활성 정박을 받을 빈 슬롯이 필요하지만 미리 슬롯을 영속 예약하지 않는다. 배터리 0의 귀환은 기존 같은 망 우선/실제 거리/PlacementStamp 동률, 없으면 다른 사용 가능한 정거장 규칙을 따른다.

그래프는 현재 충전량으로 첫 구간을 갈 수 있는지, 경유 충전 이후에는 최대 충전량 안에서 다음 구간을 갈 수 있는지 확인한다. 수집점까지 직접 갈 수 있어도 이후 납품 구간을 위해 수집 전에 충전해야 할 수 있으므로 그 경로도 비교한다. 수집점 자체에서 다음 구간을 위해 즉시 충전해야 한다면 첫 도착에도 그 충전 경유 의미를 전달한다. 경유 정거장까지의 실제 거리와 우회 거리를 TotalDistance에 포함한다. 이 최대 충전량은 계획용이며 실제 CurrentCharge를 변경하지 않는다.

에너지 때문에 지금 수행할 수 없는 경로는 WaitingForEnergy다. 신규 후보는 다른 수행 가능한 경로/작업을 선택할 수 있다. 적재품의 목적지가 모두 에너지 대기이면 안전 방출로 바꾸지 않고 적재품을 보존한다. 실제 경로 미평가는 기존 대기 규칙을 유지한다.

안전 방출 검색은 현재 망 영역의 합집합과 활성/비취소 회전 현장의 외부를 대상으로 한다. 원점의 가까운 격자값, 망 경계와 현장 외부 경계 좌표를 조합해 가장 가까운 유효 셀을 고른다. 거리 동률은 x/y 순서이며 전체 거대 격자나 임의 반경을 순회하지 않는다. 현재 셀에서 방출 가능한 기존 빠른 경로는 유지한다.

## 이동/도착/실패

이동량은 현재 구간의 남은 거리, speed×DeltaTime, 현재 배터리로 이동할 수 있는 거리 안에서 정한다. 실제로 이동한 거리만 소비하며 배터리를 음수로 만들지 않는다. 큰 DeltaTime에서도 목표를 지나치지 않는다. 수집점/납품점 도착은 결과와 대기 상태까지만 기록하며 같은 틱에 픽업/납품/다음 이동을 연쇄 실행하지 않는다.

이미 공개된 도착과 행동 시작은 구분한다. 수집 도착은 [픽업 계약](DronePickup.md)에 따라 현재 목표점/양수 배터리를 확인해 다음 틱 픽업한다. 수집 전 배터리 0/목표점 변경은 기존 중단/귀환 경로로 정산한다. 공개된 납품 도착은 [납품 계약](DroneDelivery.md)의 2A대로 실제 목적지에서 추가 이동 없이 0 배터리 인계를 허용한다. 잔여 화물은 기존 중단/보존/비상 귀환을 따른다. 도착이 확정되지 않은 배정은 목표와 같은 좌표라는 이유만으로 0 배터리의 새 도착을 만들지 않는다.

실제 에너지 부족/목표 소실 등 이동 중단 결과는 같은 배정/revision의 무효화 근거로 연결한다. Execution에서 새로 생긴 중단은 다음 Task Decision→Reservation→Lifecycle에서 기존 6A대로 정산한다. 그 전에 정리 근거를 지우지 않는다. 빈 수행자는 종료, 적재 수행자는 실물/연결을 보존한 Retargeting이 되며 최소 비상 귀환/대기 중에는 새 작업/재배정/자동 화물 방출을 차단한다.

양수 배터리의 중단에서 정상 귀환 선택을 앞당기지 않는다. 기존 Lifecycle이 정산한 뒤 현재 위치에서 다음 작업/적재 재배정의 우선권을 유지한다. 배터리 0이면 비상 속도로 수용 가능한 정거장에만 귀환하며 소비/충전은 없다. 없으면 활성 상태로 대기한다. 계획된 충전 정거장에서는 다음 구간을 실제로 시작할 충전량을 기다리고 정상 대기 때문에 예약을 해제하지 않는다. 실제 접속 전 슬롯이 일시적으로 차더라도 활성 정거장의 대기와 정거장 소실을 구분한다.

## Native 자료/Job 경계

공유 인덱스나 Fence를 새로 만들지 않는다. Flight Decision의 단일 Build Job이 임시 정거장/현장 목록을 쓰고 Route/Movement 두 Reader Job이 그 핸들을 기다린다. 소유자 배열은 Build 뒤, 목록은 두 Reader의 마지막 접근 뒤 Dispose하고 최종 핸들을 state.Dependency에 등록한다. 각 Job은 직접 사용하는 Lookup만 전달받는다.

Flight OnUpdate는 Job 레이아웃을 내장한 Burst 스케줄링 트램펄린을 피하도록 managed 경계로 둔다. 자료 준비/경로 평가/이동 판단/실제 이동/도착 반영 Job은 Burst다. 그룹 순서와 Job 완료를 구분하며 ECS 의존성과 기존 EndSimulation Producer를 사용한다. World 전체 동기화나 새 임의 Fence는 추가하지 않는다.

## 검증과 한계

아래는 이동 계약 완료 시점의 검증 기록이다. 자동 픽업/납품의 최신 검증은 각 명세를 따른다.

2026-10-29 최종 C# 컴파일은 completed/failed=false/errors=[]/compilationFailed=false다. runtime/Editor DLL이 최신 소스보다 새롭고 Editor ready/비컴파일/비리로드/Play stopped임을 확인했다. 관련 선택 EditMode는 **215/215**, 실패/생략/불확정 0이다.

| fixture | 통과 |
| --- | ---: |
| DroneMovementTests | 15 |
| DroneConfigurationTests | 33 |
| DronePrefabAndSpawnTests | 6 |
| DroneTaskSchedulingTests | 52 |
| DroneTaskPlanningTests | 14 |
| DroneStationLifecycleTests | 20 |
| DroneTaskRequestTests | 18 |
| DroneRetargetingTests | 18 |
| DroneItemTransferTests | 33 |
| DroneLifecycleIntegrationTests | 6 |

테스트는 정거장 망/수용 공간을 명시적으로 준비하며 실제 경로/이동 Producer와 기존 페이즈/EndSimulation을 실행한다. 회전 footprint 중심, 큰 DeltaTime의 초과 이동 방지, 실제 거리 소비, 정지 관측, 수집 전 충전 경유/우회 거리, 활동 영역 밖 비행, 에너지 대기와 다른 실행 가능 후보, 중단 정산/화물 보존, 비상 귀환/정거장 없음, 현장 외부 방출 셀을 확인했다.

독립 검수에서 수집점 자체가 충전 경유지일 때 첫 도착 표식이 누락되는 결함을 수정했다. 충전 대기 중 일시적 슬롯 점유와 정거장 소실도 구분했다. 집중 회귀는 수정 전 0/2에서 수정 후 2/2다. 실제 도착이 공개된 배정은 배터리 0에서도 행동 대기를 유지하고, 미확정 목표의 일치 위치만으로 새 도착을 만들지 않는 회귀는 수정 전 1/2에서 수정 후 2/2다. 네 사례는 위 Movement 15건에 포함하며 별도로 합산하지 않는다. 관련 Decision/Execution/Apply/구성/관측/자료 수명/경계/guard/가독성/주석의 독립 정적 검수를 수행했고 수용한 지적을 수정해 영향 재검수했다.

검증 중 Burst OnUpdate의 Job 스케줄링 지점에서 Editor Native 크래시가 1회 발생했다. 정확한 원인은 확정하지 못했다. Flight의 스케줄링 OnUpdate만 managed로 두고 계산 Job의 Burst는 유지했다. 같은 Editor의 재컴파일과 관련 테스트 재실행에서는 재발하지 않았다. 이를 원인 규명이나 전체 Burst AOT/성능 검증으로 해석하지 않는다.

최종 콘솔의 오류 42개는 잘못된 설정 거부를 LogAssert.Expect로 확인한 테스트 로그이며 관련 테스트에서 예기치 않은 오류 실패는 없었다. 경고도 격리 fixture의 그룹 정렬 경고와 구분해 보존했다. 원본 실행 결과/컴파일/회귀/해시/검수 자료는 로컬 Logs/Task910에 둔다. Play Mode/Player 빌드/실제 전력 충전/픽업/납품/제품 장면 전체와 성능은 검증하지 않았다. Unity Editor는 종료하지 않았다.
