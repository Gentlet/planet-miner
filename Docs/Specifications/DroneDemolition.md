# 드론 철거와 건물 실행 결과 계약

## 확정 정책

2026-10-31 확정한 1A/2A/3A/4A/5A를 적용한다.

| 정책 | 현재 범위 |
| --- | --- |
| 1A 대상 | 일반 완공 건물만 지원한다. None/ConstructionSite/MainFacility/DroneStation, Prefab, Indestructible은 거부한다. 정거장 재배치는 후속이다. |
| 2A 중단 | Command 승인과 EndCommand부터 입출고/생산/벨트/전력 참여를 중단하고 드론을 기다린다. 수행자가 없거나 경로 평가가 대기해도 건물은 보존한다. |
| 3A 행동 | 공개된 실제 도착에서 일회 확인하며 추가 시간/행동 에너지 비용은 없다. 도착하며 마지막 이동 에너지를 소모한 0 배터리도 확인한다. 새 빈 수행자의 배차/이동 자격은 기존 양수 배터리 규칙을 유지한다. |
| 4A 취소 | 철거 확인이 EndSimulation에 공개되기 전까지 취소한다. 같은 틱 미수행 확인보다 취소가 우선한다. 취소 상태 공개 뒤 다음 Command/EndCommand가 건물 중단을 해제한다. 확인 공개 뒤에는 취소를 거부한다. 이미 소비한 에너지/정산된 운송/생산을 되돌리지 않는다. |
| 5A 회수 | 이번 철거의 기존 보관/생산품, 비용 환급품과 철거 벨트에서 정지한 실물만 표시 기반 한정 회수로 연결한다. 공간이 없으면 보존하고 기다린다. 일반 바닥 전체 자동 회수 범위는 넓히지 않는다. |

## 입력과 승인 소유자

기존 DemolishBuildingRequest 이름과 TargetBuilding을 유지한다. BuildingDemolitionCommandSystem은 승인/중복/거부 요청을 EndCommand에 삭제하고 승인한 레거시 입력만 기존 DroneTaskRequestUtility.SubmitCreate로 변환한다. 이 내부 어댑터는 메인 스레드의 기존 World 접수 순번 발급자를 사용한다. Command는 발급 가능량을 승인 전에 읽어 단일 Job의 임시 레거시 수용량으로 제한한다. 순번 0/소진 입력은 Transfer/ProductResult/Pending을 변경하기 전에 거부한다. 이미 접수번호를 받은 generic 요청은 이 제한으로 거부하지 않는다. 실제 순번 변경은 계속 Submit만 소유한다. 생성한 요청에는 BuildingDemolitionAutomaticTaskRequest를 붙이고 다음 Command에서 자기 결과만 소비한다. 외부 Task 요청 결과는 외부 TryConsumeResult에 남긴다.

직접 Task 요청은 Kind=BuildingDemolition, ItemType=None, Quantity=0이다. 같은 Command가 대상/보호/이동에 필요한 건물 형태를 검증해 PendingBuildingDemolition을 승인하며 잘못된 payload/대상은 건물을 중단하지 않는다. 기존 전송 요청 비활성화와 이전 ProductResult 폐기도 이 소유자의 책임이다.

PendingBuildingDemolition.Task는 연결 root를 뜻한다. Command의 최초 승인에서는 Null이고 Task Execution은 root 생성과 같은 EndSimulation ECB에서 deferred root를 연결한다. 별도 요청의 deferred Entity를 다른 ECB나 임시 ECS 원본에 넘기지 않는다. 중단 상태는 요청 삭제 후에도 유지된다.

## 작업/경로/배정

BuildingDemolition root는 ItemType=None이며 작업의 Quantity=1은 아이템 수량 대신 철거 행동 한 회다. 공급/회수/일반 운반과 같은 양수 CreationSequence를 사용한다. 적재품 재배정 우선과 공급 선두 비교 규칙을 유지하고, 새 철거는 비공급 선두의 생성 순번 비교에 참여한다. 수동 우선순위/일시 중단/재개는 추가하지 않는다.

Task Decision은 현재 수행자 위치의 정거장 망과 대상의 회전 footprint 일부 겹침을 확인한다. DirectToBuilding 경로는 빈 수행자에서 승인 건물 중심으로 가는 직접 경로다. 기존 Direct 적재품 재배정과 ViaSource 운반 경로의 자격을 완화하지 않는다. 경로 요청/평가 결과는 기존 EndSimulation에 공개되어 다음 틱부터 후보가 된다.

Reservation은 현재 수행 중인 철거 대상과 이번 틱 선택 대상을 같은 임시 확보 집합으로 중재한다. 한 건물은 한 수행자만 선택한다. 재고/보관 공간/공사 자재 예약을 만들지 않는다. 기존 pending PublicationQueued는 ECB 기록 완료이며 Playback 공개와 다르다. Publish가 새 배정/수행자 연결을 EndSimulation에 기록하고 MovingToDestination/DemolishBuilding으로 시작한다.

보관 드론 자동 출고는 현재 실행 가능한 철거 잔여 수요에도 연결한다. 이미 배정/선택된 대상이나 확인을 마친 root는 새 출고 수요로 세지 않는다. 기존 정거장 슬롯/Identity 전환 소유자는 그대로다.

## 확인과 실제 건물 결과

| 시점 | Writer / 소비 |
| --- | --- |
| 이동 | 기존 Flight/Movement가 실제 위치/격자/거리 에너지/도착을 소유한다. Movement Apply는 같은 배정/revision/action의 AwaitingDelivery를 EndSimulation에 공개한다. |
| 다음 Task Decision | 공개된 철거 도착/빈 화물/현재 관측과 대상 연결을 DroneDemolitionDecisionElement에 작성한다. 취소/무효화 배정은 제외한다. |
| Drone Lifecycle | 최신 배정/revision/관측/건물 연결/실제 footprint 중심을 확인하고, 추가 에너지 소비 없이 root의 ExecutionConfirmed와 root 부착 BuildingDemolitionExecutionRequest를 EndSimulation에 기록한다. 해당 수행자 배정은 Completed/연결 해제한다. |
| 다음 Command | 공개된 확인 요청이 있는 root는 중단 상태를 유지한다. 확인 전 Cancelled/Closed/소실 root의 중단만 EndCommand에 해제한다. 재생성으로 다른 root가 연결되면 이전 취소를 소급 적용하지 않는다. |
| 다음 Building StateApply / EndBuilding | 건물 소유자만 확인 요청이 있는 Pending 대상을 반환/환급/삭제하고 root의 BuildingDemolitionResult를 공개한다. 드론이 건물을 직접 삭제하지 않는다. |
| 같은 틱 Drone Decision/Lifecycle | 실제 건물 결과를 FinishDemolitionTask 의도로 전달하고 root를 Closed로 반영한다. 요청/결과/배정 참조가 정리된 뒤 기존 EndSimulation에 root를 삭제한다. |

확인 전 Open, 확인 공개 후 ExecutionConfirmed, 실제 건물 결과 수신 후 Closed를 구분한다. ExecutionConfirmed는 취소/신규 배정 대상이 아니며 결과 전 root 삭제를 막는다. 확인을 보냈다는 사실만으로 건물 제거 성공을 보고하지 않는다. 확인 뒤 대상이 사라져 건물 처리 입력을 잃어도 건물 소유자가 Failed 결과를 남긴다.

## 반환/환급/실패

기존 Stored/Product 실물은 같은 Entity의 Owner/위치/표시를 EndBuilding에서 월드로 반환한다. 활성 Destroy 실물은 반환하지 않는다. 건축 비용은 BuildingType.Type/Tier에 맞는 설정 수량 100%만 공통 ItemLifecycleUtility로 새 생성한다. 과거 선소비 재료/광물/연료 추가 보상은 없다.

반환/환급과 철거 벨트 정지 실물의 DroneRecoveryPending.Reason은 BuildingDemolition이다. 기존 무인자 표시는 DroneDrop으로 해석하여 종전 입력을 유지한다. 다음 드론 생성 판단은 현재 EndBuilding 결과를 읽고 한정 WorldItemRecovery를 EndSimulation에 게시한다. 기존 이동/픽업/보관과 같은 키 사용자 취소 차단을 유지한다. 실제 성공 픽업 뒤 표시를 제거한다.

기존 환급 프리팹 누락의 Strict Fail/Fatal 및 전체 틱 rollback 없음 정책을 유지한다. 이 경우 건물 소유자는 Failed 결과를 기록하며, 일부 처리된 반환을 드론이 복제/보상하거나 성공으로 표시하지 않는다.

## 범위와 검증

주 시설 철거는 계속 거부한다. 정거장 철거/보관 드론 전체 재배치는 Task 9.19, 일반 바닥/사용자 범위 회수는 Task 9.18, 공사 취소 통합은 Task 9.20, 제품 입력/UI는 후속이다. 기존 생성/취소/운반/전력 공개 경계는 유지한다.

실제 Command/EndCommand, Building/EndBuilding, 관측/경로/배정/이동/철거 확인/EndSimulation을 실행하는 DroneDemolitionTests가 배터리0 도착/다음 건물 삭제/취소 경계/단일 배차/제외 대상/재승인/대상 소실/반환 환급 회수/보관 드론 자동 출고/환급 실패를 확인한다. 정거장 망 범위는 fixture 입력이다.

기존 건물/전력/물류 코어 테스트의 ConfirmedBuildingDemolitionTestSystem은 DisableAutoCreation인 Editor 전용 입력 제공자다. 승인된 건물에 이미 공개된 확인을 주입해 코어 정산만 검증하며 실제 드론 수행 증거로 사용하지 않는다. 실제 수행은 별도 신규 통합 fixture로 구분한다. Play Mode/Player/성능/전체 Burst AOT는 미수행이다. Unity C# 컴파일 completed/failed false/errors 없음/compilationFailed false와 최신 runtime/Editor DLL을 확인했다. 선택 EditMode 18개 fixture 393/393(실패/생략/불확정 0)이며 신규 실제 철거 통합은26개다. 원본 실행 기록은 로컬 Logs/Task917에 보존한다.

접수번호 검수 보완의 0/최대값 레거시 두 사례는 수정 전 실패를 재현했다. 마지막 번호 한 개의 다중 대상 중재/이미 접수된 generic 요청/legacy와 generic 혼합 중복도 확인한다.
