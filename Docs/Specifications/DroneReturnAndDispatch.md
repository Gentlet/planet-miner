# 드론 귀환/정거장 전환과 수요 기반 출고 계약

## 확정 정책과 범위

2026-10-30 확정한 1A/2A를 적용한다. 1A는 정상 귀환 중에도 현재 위치에서 실행 가능한 작업이 생기면 귀환을 중단하고 재배차하는 규칙이다. 적재품이 있으면 기존 적재품 재배정을 우선한다. 배터리 0의 비상 귀환과 기존 배정의 계획된 충전 경유는 이 신규 배차 대상으로 바꾸지 않는다.

2A는 보관된 드론 아이템을 남은 실행 가능 작업 수요에 맞춰 자동 출고하는 규칙이다. 정거장 입고 자체는 활성화의 근거가 아니다. 출고는 기존 Activate 요청과 동일 Entity 전환을 사용하고 EndSimulation 공개 뒤 다음 틱의 기존 배차 절차에 참여한다. 출고 요청만으로 작업 배정이나 실제 운반 성공을 만들지 않는다.

정상 귀환, 기존 비상 귀환의 정거장 접속, 자동 Dock/Store와 정박 해제, 수요 기반 Activate를 연결한다. 실제 전력 충전/배터리 증가량 정산은 Task 9.14, 정거장 철거의 전체 드론 재배치는 Task 9.19에 남긴다. 자동 화물 방출과 사용자 진행 중 취소는 각각 기존 후속 범위를 유지한다.

## 귀환과 재배차

작업 종료 뒤 현재 위치에서 실행 가능한 다음 작업을 먼저 선택한다. 정상 귀환 중에도 같은 규칙을 적용하며 빈 수행자의 신규 배정과 적재품 재배정을 구분한다. 아직 평가되지 않은 경로를 작업 없음으로 처리하지 않는다. 기존 후보 순서와 실제 수량 중재를 거친 선택만 귀환보다 우선한다.

귀환 목적지는 현재 위치의 같은 드론 네트워크에 있는 수용 가능한 정거장을 우선한다. 실제 비행 구간 거리로 비교하고 동률은 기존 PlacementStamp 순서를 사용한다. 같은 망에 후보가 없으면 다른 사용 가능한 정거장을 찾고, 모두 불가능하면 활성 상태로 대기한다. 현재 위치가 어떤 활동 영역에도 포함되지 않으면 같은 망 우선 없이 사용 가능한 정거장을 비교한다.

정상 비행은 기존 속도와 거리당 에너지 소비를 사용한다. 충전 경유를 포함한 경로 평가는 현재 충전량으로 첫 구간을, 최대 충전량으로 이후 구간을 평가하며 실제 배터리를 채우지 않는다. 배터리 0은 비상 속도의 귀환만 허용하고 화물을 보존한다.

목적 정거장이 만석/소멸/도달 불가이면 대체 정거장을 찾는다. 예정 목적지는 영속 슬롯 예약이 아니다. 도착 전후 다른 수행자나 보관 동작이 공간을 사용하면 기존 정거장 중재/최종 적용에서 거부할 수 있으며 다음 판단에서 다시 선택한다.

## 정박과 수납

정거장 도착과 정박, 충전 성공, 수납은 별개다. 실제 도착한 미완충 수행자는 활성 역할과 배터리/화물/배정을 보존한 채 Dock으로 정박한다. 배터리가 부족하다는 이유만으로 충전 성공이나 Store를 기록하지 않는다.

Store는 기존 완충/활성 배정 없음/진행 중 행동 없음/빈 화물과 None 출처/보관 공간의 모든 조건을 요구한다. 정박 수행자는 한 슬롯 전체를 점유하고 수납 아이템은 ItemConfig의 MaxStack에 따라 스택한다. 수용 가능성은 실제 요청 종류에 맞게 판단하며 빈 활성 정박 슬롯과 수납 스택의 남은 공간을 혼동하지 않는다.

전용 슬롯 우선과 일반 공간 공유, 같은 요청 접수 순서, 수행자당 한 전환, 최종 보관/정박 관계의 소유자는 [Identity/정거장 보관 계약](DroneIdentityAndStationStorage.md)을 따른다. 공간 부족/Busy는 정상 거부이며 실물 삭제나 충전으로 대체하지 않는다. 자동 Producer의 수행자별 공간 사전 확인은 영속 예약이 아니며, 이미 만석이면 요청을 만들지 않아 귀환 재선택을 허용한다. 동시에 가능한 요청끼리의 실제 경합은 기존 Reservation/Apply가 처리한다.

자동 도착 대기의 자격은 [정거장 전환 명세](DroneIdentityAndStationStorage.md#자동-요청과-도착-대기)를 따른다. 실제 정거장 위치/ChargingStation이 일치하는 대기를 Dock/정박 해제 입력으로 사용하며, 완충 후 대기만 남은 Store는 대기 의도 종료와 수납을 같은 기존 공개 경계에서 확정한다.

## 수요 기반 출고와 요청 수명

DroneStationAutomationDecisionSystem은 Task Decision 뒤, Station Lifecycle Decision 앞에서 실행한다. 실제 정거장 위치와 기존 도착/활동/화물/배정을 읽어 Dock/Store를 준비한다. 정박한 수행자의 기존 배정 또는 적재품 재배정 경로가 실제로 실행 가능하면 Activate로 정박을 해제한다. 보관 아이템과 무배정 정박 수행자는 양수의 실행 가능한 작업 수요가 있을 때만 출고 후보가 된다.

HasTaskDecisionPriority는 기존 작업 후보/일반 운반 계획/이번 경로 생성/미평가 유지 결과를 읽는 순수 조회다. 현재 위치에서 작업 또는 경로 평가의 우선권이 있으면 자동 Dock/Store 요청을 만들지 않는다. 자동 출고 직후 경로가 평가되는 동안 재수납하지 않고 기존 배차 절차를 기다린다.

정박 중 목적지가 무효화된 첫 틱에는 기존 Reservation이 잔여 예약을 반환하고, 이후 Lifecycle Apply가 화물과 배정 연결을 Retargeting으로 보존한다. 이미 Retargeting인 정박 수행자에게 반복 작성되는 무효화 의도는 새 종료로 취급하지 않는다. 다음 판단은 잔여 배터리로 실행 가능한 대체 경로를 확인해 Activate를 접수하며, EndSimulation 정박 해제 공개 뒤 기존 Task Decision/Reservation이 적재품을 재배정한다. 배터리나 화물 원본을 이 판단에서 변경하지 않는다.

출고 경로 계산은 현재/최대 배터리와 실제 공급원/목적지/네트워크 계약을 사용한다. 경로 계산 결과만으로 실물을 전환하지 않고 기존 Submit으로 World 접수 순번을 부여한다. 미처리 정거장/행동 요청은 중복 자동 요청을 막는다.

DroneStationActivationReservationSystem은 Task Reservation 뒤 단일 Burst Job으로 실제 승인 및 진행 중 확보를 제외한 잔여 수요를 계산한다. 초과 출고 후보는 기존 정거장 결정의 거부 결과로 남기고 실제 작업 목표량을 미리 확보하지 않는다. 활성 수행자가 해당 작업의 새 경로 평가를 요청한 경우 그 작업의 출고를 보류해 미평가를 수요 공백으로 취급하지 않는다. 정상 작업 배정/확보는 출고 공개 뒤 기존 Task Reservation이 담당한다.

DroneAutomaticStationRequest는 자동 요청 Entity에만 붙는다. DemandTask/Quantity는 해당 호출의 출고 수요 중재 입력이고 배정/영속 확보 기록이 아니다. EndSimulation 뒤 자동 Producer는 자신의 결과만 TryConsumeResult로 소비한다. 외부 결과는 외부 호출자에게 남기며 결과 소비가 역할/실물 전환을 반복하지 않는다.

정박 Activate는 같은 Entity의 정박 관계를 해제하고 이동 상태 초기값/Idle을 같은 EndSimulation에 공개한다. 배터리와 기존 배정/화물은 유지하며 충전 성공으로 처리하지 않는다. 보관 아이템의 Activate는 기존 공통 생성 초기화와 Identity 전환 계약을 재사용한다.

## 공개와 후속 연결

이동/위치/거리 소비는 기존 Movement Execution, 작업/배정/예약은 기존 관리 소유자, 정박/수납/출고와 Identity 전환은 기존 Station Lifecycle이 소유한다. 자동 요청 Producer가 이 원본을 직접 중복 변경하지 않는다.

| 경계 | 소유자와 출력 |
| --- | --- |
| Preparation | 기존 Observation Preparation은 정상 귀환 중 양수 배터리의 신규 작업 자격을 준비한다. 비상 귀환/기존 작업 충전 경유는 기존 배차 차단을 유지한다. |
| DroneDecision | Flight Decision이 기존 경로와 정상 귀환 fallback 의도를 준비한다. Task Decision이 후보를 작성한 뒤 DroneStationAutomationDecisionSystem은 자동 정거장 요청을 준비한다. 출고 경로 평가는 병렬 Burst Job을 사용하고 접수는 기존 Submit이 소유한다. |
| DroneReservation | Task Reservation의 실제 선택 뒤 DroneReturnReservationSystem이 정상 귀환과 작업/미평가 경로/이미 접수된 요청을 중재하여 DroneReturnReservationElement에 차단된 귀환만 기록한다. DroneStationActivationReservationSystem은 실제 작업 승인/진행 확보를 제외한 잔여 수요만 자동 출고 대상으로 남긴다. Station Lifecycle Reservation은 뒤에서 기존 요청/슬롯 경합을 처리한다. |
| DroneExecution | Movement Execution만 정상 귀환/비상 귀환의 실제 위치/거리 소비/활동/이동 상태를 작성한다. |
| DroneStateApply / EndSimulation | 기존 Station Lifecycle이 동일 Entity의 정박/수납/출고와 결과를 기록하고 EndSimulation이 공개한다. |

DroneReturnReservationElement는 기존 task candidates singleton의 이번 틱 scratch 버퍼다. GetOrCreateCandidates가 최초 부착하고 Return Reservation의 단일 Burst Job이 매 틱 Clear/작성한다. Worker와 PreserveArrival만 전달하며 Flight Decision의 출력을 덮어쓰지 않는다. Movement Execution은 접수된 정거장/행동 요청 때문에 차단된 경우 현재 이동 상태와 위치를 보존하고 요청의 최종 적용까지 멈춘다. 배정 또는 미평가 경로 대기로 차단되면 이동 목적지를 정리하고 Idle로 위치를 유지한다. 새 공유 Persistent 캐시나 영속 슬롯 예약은 없다.

DroneMovementState.IsNormalReturn은 기존 ReturnStation/ChargingStation 목적지의 정상 귀환 여부만 구분한다. 위치/배터리/작업 상태의 사본이 아니며 Movement Execution이 유지한다. 정상 귀환의 목표 선택은 DroneFlightPlanningUtility.PlanReturn이 기존 충전 경유 계산과 실제 구간 거리 비교를 재사용한다.

정거장 역할과 양방향 보관 관계, 요청 제거와 결과는 기존 EndSimulation에서 공개한다. 자동 요청의 결과 소비는 재정산이 아니며 외부 요청 결과를 대신 소비하지 않는다. 새 출고 수행자의 최초 관측은 기존 초기화 계약을 유지하고 다음 Preparation 이후 배차에 참여한다.

이번 틱 새 정박은 다음 틱 충전 수요의 입력이다. [충전 계약](DroneCharging.md)의 Preparation과 Execution이 실제 전력 배정 안에서 배터리를 증가시키며 다음 틱 재배차/수납이 갱신된 원본을 읽는다.

## 초기 구현 검증 범위와 결과

2026-10-30 C# 컴파일의 completed/failed false/errors 없음/compilationFailed false를 확인했다. runtime과 Editor DLL은 최종 C# 소스보다 최신이며 Editor는 ready/비컴파일/비리로드/Play stopped 상태다.

관련 선택 EditMode는 9개 fixture **197/197 통과**, 실패/생략/불확정은 모두 0이다.

| 범위 | 통과 수 |
| --- | ---: |
| 귀환/정거장 전환/수요 기반 출고 | 13 |
| 자동 납품 | 8 |
| 이동/도착/거리 소비 | 26 |
| 실물 인계 | 40 |
| 수명주기 통합 | 6 |
| 적재품 재배정 | 18 |
| 작업 선택/중재 | 52 |
| 작업 계획 | 14 |
| 정거장 수명주기 | 20 |

실제 이동/도착 뒤 자동 정거장 요청, 정상 귀환 중 신규 배차/적재 재배정, 정거장 만석/소멸/대체 후보/후보 없음, 슬롯 경합과 중복 요청, 수요 없는 보관과 수요 기반 출고를 확인했다. 자동 출고 직후 다음 배차 전에 재수납되는 경로를 수정하고 회귀 검증에 포함했다. fixture의 드론 MaxStack은 실제 ItemConfig의 50을 사용한다.

독립 검수의 자동 출고 재수납/명명/guard/주석 보완을 반영하고 관련 경계를 재검수했다. 정박 수행자가 이동한다는 지적은 두 이동 Job의 WithNone<DroneDocked> 제외 조건을 근거로 경계/Apply/guard 검수에서 기각했다.

최종 Unity 콘솔의 현재 집계는 오류 0/경고 502다. Pipeline 내부 이력의 과거 오류 43건과 앞선 실패 누적은 이번 최종 콘솔 상태나 최종 테스트 실패로 합산하지 않는다. 경고가 없는 검증으로 보고하지 않는다. Play Mode/Player 빌드/성능/실제 전력 충전은 실행하지 않았으며 이전 Task의 실행 수치를 합산하지 않는다.


## 정박 중 목적지 무효화 보완 검증

정박한 적재 드론의 배정이 Retargeting으로 보존된 뒤 반복 무효화 의도가 자동 Activate를 계속 차단하던 조건을 수정했다. 실제 픽업/비행/자동 정박 이후 목적지를 철거 대기 또는 비활성으로 만들고, 잔여 배터리로 도달 가능한 대체 보관처에 재배정하는 두 사례를 추가했다. 수정 전에는 두 사례 모두 정박 해제 대기에서 실패했고, 수정 후에는 같은 수행자/배정/화물 보존, 다음 틱 revision 유지, 실제 거리 소비와 대체 보관처의 최종 수령을 통과했다.

2026-10-30 보완 버전의 C# 컴파일은 completed/failed false/errors 없음/compilationFailed false이며 runtime/Editor DLL 최신성과 Editor ready/Play stopped를 확인했다. 관련 선택 EditMode 9개 fixture는 **199/199 통과**(귀환 15개, 나머지 184개)했고 실패/생략/불확정은 0이다. 최종 콘솔은 오류 0/경고 518이다. 초기 197개 실행과 합산하지 않는다. Play Mode/Player/성능/전체 Burst AOT/실제 충전은 미수행이다.
