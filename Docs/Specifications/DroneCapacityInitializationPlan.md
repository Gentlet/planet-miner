# 드론 공통 적재량 초기화 연결

초기 문서는 제안이었으며 2026-10-22 사용자가 6A를 확정했다. Task 9.3의 DroneConfigInitializationSystem이 이 초기화 Writer를 구현했다. Task 9.3.1의 생성 기반과 이후 [정거장/Identity 연결](DroneIdentityAndStationStorage.md)을 구분한다.

## 현재 상태

[DroneConfig.json](../../Assets/Resources/Config/DroneConfig.json)의 `carryingCapacity: 3`과 `maximumBattery: 100`을 [초기화 시스템](../../Assets/Scripts/Systems/Initialization/DroneConfigInitializationSystem.cs)이 Resources에서 읽는다. 적재량 원본은 [DroneCapacityState](../../Assets/Scripts/Components/DroneLogistics/DroneCapacityState.cs), 최대 배터리는 DroneBatteryConfig다. 기존 [읽기 처리](../../Assets/Scripts/Common/DroneSchedulingUtility.cs)는 제품 초기화가 없는 격리 World의 부재/0 입력을 새 배정 대기로 유지한다.

모든 드론의 현재 공통 적재량은 World에 하나인 DroneCapacityState.CarryingCapacity가 소유한다. 실제 적재품은 StoredItemElement/ItemOwnership이 소유한다. 초기화 연결은 기존 관리 시스템에 정상적인 시작 값을 제공하는 작업이다. 명시적 수행자 등록은 Task 9.3이 연결하며 실제 이동/관측/경로 계산은 후속이다.

## 확정한 초기화 흐름

Initialization에서 `Resources/Config/DroneConfig`를 읽고 `carryingCapacity`를 검증한 뒤 World에 공통 상태 하나를 게시한다. 초기 게시 이후에는 초기화 처리를 종료한다. 설정 파일의 현재 값 3을 사용하며 읽기 함수 안에 새로운 기본 숫자를 넣지 않는다.

설정 JSON은 시작 시 사용하는 입력이다. 파싱한 임시 데이터는 게시 이후 별도로 보관할 필요가 없다. 현재 적재량과 별도 DroneConfig ECS 상태에 같은 값을 계속 복제하는 구조는 추가하지 않는 방향을 권한다.

향후 연구는 같은 DroneCapacityState의 값을 갱신한다. 이미 확정한 규칙대로 변경된 값은 빈 수행자의 새 배정부터 적용하고 기존 AssignedQuantity·현장 예약·보유 실물을 소급 수정하지 않는다. 기존 적재품 재배정도 현재 보유 실물을 기준으로 유지한다.

## 사용자 확정 정책 (Task 9.3 구현)

| 항목 | 현재 자료와 제안 |
| --- | --- |
| 설정 출처와 초기값 | 기존 DroneConfig.json의 carryingCapacity를 재사용하고 현재 값 3을 유지한다. 새 설정 파일이나 하드코딩 기본값을 만들지 않는다. |
| 누락·파싱 실패·유효하지 않은 값 | 기존 필수 설정과 같은 공통 Fatal로 처리하고 미게시·자동 재시도 없음·기본값 대체 없음을 적용한다. 제품 초기화가 없는 격리 World에서 기존 Reader의 부재/0 대기는 유지한다. |
| 이미 게시된 상태 | 유효한 상태 하나가 있으면 초기값으로 덮어쓰지 않고 보존한다. 여러 개이거나 무효하면 원본을 보존하고 공통 Fatal로 차단하며 새 게시를 거부한다. 재초기화로 연구 결과나 사전 등록 값을 3으로 되돌리지 않는다. |

표의 정책을 구현했다. 기존 값이 일부만 준비됐을 때에도 모든 기존 상태와 필요한 JSON 입력을 검증한 뒤 없는 상태만 게시한다. 둘 다 유효하게 준비됐으면 JSON을 다시 읽지 않는다. 자동 시스템은 첫 Update 뒤 비활성화한다.

## 이번 후속 작업의 최소 범위

- 공통 적재량 설정 읽기·검증과 한 번의 게시.
- 기존 상태와 중복 게시 처리.
- 정상 값, 누락/오류, 기존 상태 보존에 대한 선별 검증.
- 초기화 생산자와 기존 배정 Reader의 계약 문서 갱신.

Task 9.3은 최대 배터리 설정을 함께 게시해 최초 완충/기존 배터리 보존과 엄격한 수납 검사에 사용한다. 이동 속도/거리 소비/충전 수치는 읽지 않는다. 명시적 출고/수납 요청은 같은 Task에서 연결했으며 지속 관측/경로 평가/비행/행동 신호/연구 전체 시스템은 후속이다. 초기화와 역할 전환만으로 실제 운송을 시작하지 않는다.

## 완료 기준

확정한 정책에 따라 새 World에 적재량 상태가 정확히 하나 게시되고, 기존 배정 판단이 그 값을 읽어 수량을 제한해야 한다. 반복 초기화는 기존 상태를 변경하지 않아야 하며 실패 처리는 임의 기본값으로 감추지 않는다. 설정 부재/실패와 초기 상태가 이미 있는 경우도 선별 검증으로 확인한다.

현재 완료 범위와 처리 계약은 [드론 컴포넌트 문서](../CodeMemory/Components/DroneLogistics.md)를 따른다. 건물·드론 분리 이후 컴파일·선별 EditMode 및 일회 Play Mode를 수행했다. 그 Play Mode는 공통 적재량·수행자·관측·경로 결과·행동 신호를 검증 입력으로 제공했으므로 제품 초기화 Writer의 실행 증거가 아니다. 앞선 142개 회귀와 신규 통합 4개는 이전 구조의 서로 다른 실행 기록이다.
