# 드론 공통 적재량 초기화 연결

드론 작업 관리의 1~5단계는 확정한 범위의 구현과 선별 검증을 마쳤다. 다음 검토 대상은 게임 시작 시 공통 적재량을 게시하는 초기화 연결이다. 이 문서는 후속 설계 제안이며 코드 구현과 아래 정책의 확정을 의미하지 않는다.

## 현재 상태

[DroneConfig.json](../../Assets/Resources/Config/DroneConfig.json)에 `carryingCapacity: 3`이 있다. 현재 제품 코드에는 이 값을 읽어 [DroneCapacityState](../../Assets/Scripts/Components/DroneLogistics/DroneCapacityState.cs)를 생성하는 초기화 Writer가 없다. 기존 [읽기 처리](../../Assets/Scripts/Common/DroneSchedulingUtility.cs)는 상태가 없거나 값이 0 이하이면 빈 수행자의 신규 배정량을 0으로 판단한다. 무효 작업 정리와 예약 해제는 계속 처리한다.

모든 드론의 현재 공통 적재량은 World에 하나인 DroneCapacityState.CarryingCapacity가 소유한다. 실제 적재품은 StoredItemElement/ItemOwnership이 소유한다. 초기화 연결은 기존 관리 시스템에 정상적인 시작 값을 제공하는 작업이다. 수행자 등록·이동·경로 계산은 별도 후속이다.

## 제안하는 초기화 흐름

Initialization에서 `Resources/Config/DroneConfig`를 읽고 `carryingCapacity`를 검증한 뒤 World에 공통 상태 하나를 게시한다. 초기 게시 이후에는 초기화 처리를 종료한다. 설정 파일의 현재 값 3을 사용하며 읽기 함수 안에 새로운 기본 숫자를 넣지 않는다.

설정 JSON은 시작 시 사용하는 입력이다. 파싱한 임시 데이터는 게시 이후 별도로 보관할 필요가 없다. 현재 적재량과 별도 DroneConfig ECS 상태에 같은 값을 계속 복제하는 구조는 추가하지 않는 방향을 권한다.

향후 연구는 같은 DroneCapacityState의 값을 갱신한다. 이미 확정한 규칙대로 변경된 값은 빈 수행자의 새 배정부터 적용하고 기존 AssignedQuantity·현장 예약·보유 실물을 소급 수정하지 않는다. 기존 적재품 재배정도 현재 보유 실물을 기준으로 유지한다.

## 구현 전에 확인할 정책

| 항목 | 현재 자료와 제안 |
| --- | --- |
| 설정 출처와 초기값 | 기존 DroneConfig.json의 carryingCapacity를 재사용하고 현재 값 3을 유지하는 안을 권한다. 새 설정 파일이나 하드코딩 기본값을 만들지 않는다. |
| 누락·파싱 실패·유효하지 않은 값 | 오류를 기록하고 초기 상태를 게시하지 않아 빈 수행자의 신규 배정을 대기시키는 안을 권한다. 전체 시뮬레이션을 중단하는 정책까지 요구할지는 별도로 확인한다. 현재 값 부재/0 처리와 임의 기본값을 만들지 않는 읽기 계약은 유지한다. |
| 이미 게시된 상태 | 하나가 있으면 초기값으로 덮어쓰지 않고 보존하는 안을 권한다. 여러 개면 중복 오류를 기록하고 새 게시를 거부한다. 재초기화로 연구 결과나 사전 등록 값을 3으로 되돌리지 않는다. |

표의 초기화 정책은 제안이다. 이번 문서 정리에서는 확정하거나 구현하지 않았다.

## 이번 후속 작업의 최소 범위

- 공통 적재량 설정 읽기·검증과 한 번의 게시.
- 기존 상태와 중복 게시 처리.
- 정상 값, 누락/오류, 기존 상태 보존에 대한 선별 검증.
- 초기화 생산자와 기존 배정 Reader의 계약 문서 갱신.

DroneConfig의 이동 속도·배터리·충전·스테이션 범위 등 다른 필드는 이 최소 범위에 포함하지 않는다. 실제 드론 생성/등록, 관측 위치 SoT Writer, 실제 경로 평가, 비행/행동 신호, 연구 전체 시스템도 별도 작업이다. 적재량 초기화를 연결해도 수행자와 경로·행동 입력이 없으면 실제 운송은 시작하지 않는다.

## 완료 기준

확정한 정책에 따라 새 World에 적재량 상태가 정확히 하나 게시되고, 기존 배정 판단이 그 값을 읽어 수량을 제한해야 한다. 반복 초기화는 기존 상태를 변경하지 않아야 하며 실패 처리는 임의 기본값으로 감추지 않는다. 설정 부재/실패와 초기 상태가 이미 있는 경우도 선별 검증으로 확인한다.

현재 완료 범위와 처리 계약은 [드론 컴포넌트 문서](../CodeMemory/Components/DroneLogistics.md)를 따른다. 건물·드론 분리 이후 컴파일·선별 EditMode 및 일회 Play Mode의 범위는 [도메인 분리 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDroneDomainSplit-Verification.md)에 기록했다. 그 Play Mode는 공통 적재량·수행자·관측·경로 결과·행동 신호를 검증 입력으로 제공했으므로 제품 초기화 Writer의 실행 증거가 아니다. 앞선 [142개 회귀](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSafeDropAndJournalRemoval-Verification.md)와 [신규 통합 4개](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneLifecycleIntegration-Verification.md)는 이전 구조의 서로 다른 실행 기록이다.
