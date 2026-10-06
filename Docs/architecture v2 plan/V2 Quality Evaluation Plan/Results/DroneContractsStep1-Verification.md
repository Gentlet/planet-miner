# 드론 데이터 계약 1단계 변경과 검증

## 범위와 결과

2026-10-04 사용자가 승인한 범위는 당시 구현 계획의 1단계 데이터·수행부 계약 정의와 컴파일 확인이다. 작업 생성·배정·예약 정산·실물 인계·실제 드론 등록이나 실행 시스템은 추가하지 않았다.

5개 C# 파일에 ECS 타입 11개를 추가했다. 기존 시스템·테스트·장면·프리팹 코드는 변경하지 않았다. 기존 작업 트리의 변경과 스테이징 상태를 보존했으며 이번 변경을 커밋하거나 스테이징하지 않았다.

## 추가한 계약

| 정의 파일 | 추가한 ECS 타입 |
| --- | --- |
| [DroneTaskComponents.cs](../../../../Assets/Scripts/Components/DroneLogistics/DroneTaskComponents.cs) | DroneLogisticsTask, DroneTaskAssignment |
| [DroneWorkerComponents.cs](../../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs) | DroneWorker, DroneWorkerObservation, DroneWorkerAssignment, DroneCargoState |
| [DroneRouteContracts.cs](../../../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs) | DroneRouteEvaluationRequest, DroneRouteEvaluationResult |
| [DroneActionContracts.cs](../../../../Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs) | DroneActionReadyRequest, DroneItemTransferResult |
| [ConstructionSupplyReservation.cs](../../../../Assets/Scripts/Components/Construction/ConstructionSupplyReservation.cs) | ConstructionSupplyReservation |

DroneActionIdentity와 enum은 일반 값 형식으로 정의하고 ECS 타입 수에서 제외한다. 타입의 기본 상태는 None 또는 유효하지 않은 0이며 기본값이 도달 가능·성공·유효한 배정을 뜻하지 않는 계약을 기록했다.

현장 공급량만 예약 데이터로 정의했다. 공급원 재고·보관 공간 예약은 없고 벨트 판단·입출고·예약·FIFO를 수정하지 않았다. 실제 적재 품목과 수량은 StoredItemElement를 원본으로 사용한다. 회수품의 보관 전 공급 금지 제약은 DroneCargoState로 작업 수명과 분리했다.

관측·배정·평가 revision과 행동 sequence는 후속 Consumer가 오래된 결과·중복 신호를 구분할 입력이다. 이 필드를 선언한 것만으로 해당 검사가 실행되는 것은 아니다. 요청·결과의 생성/소비·ECB 시점과 내부 실제 인계 결과/외부 행동 신호의 차이를 소스 주석 및 [컴포넌트 문서](../../../CodeMemory/Components/DroneLogistics.md)에 기록했다.

## 컴파일 근거

설치된 Unity CLI로 대상 프로젝트의 연결된 Editor를 확인하고 재컴파일을 제출했다. CLI 호출 출처 플래그를 전달하기 위해 개별 command 경로를 사용했다. 재컴파일 제출 이후 최종 상태를 별도로 확인했다.

- 연결: 대상 프로젝트 Editor 1개, Unity 6000.4.11f1.
- recompile_status: `status=completed`, `failed=false`, `compilationFailed=false`, `errors=[]`.
- editor_status: `status=ready`, `compiling=false`, `domainReloadInProgress=false`, `playMode=stopped`.
- Assembly-CSharp.dll: 가장 최근 Assets C# 수정 시각 이후에 생성된 것을 확인했다.
- 새 파일과 문서의 경로·컴포넌트 색인 및 변경 diff를 정적으로 확인했다.

원본 JSON은 프로젝트 로컬의 `Logs/Codex/DroneContractsStep1-20261004/`에 보관했다. `connection.json`, `recompile.json`, `recompile-status.json`, `editor-status.json`, `assembly-freshness.json`이 각각 연결·제출·최종 상태·Editor 상태·최신성의 근거다. Logs는 로컬 실행 기록이며 Git 추적 문서와 구분한다.

## 확인하지 않은 범위

데이터 정의만 추가한 이번 단계는 컴파일로 확인했다. 단순 필드·컴포넌트 존재 확인 테스트를 새로 만들지 않았고 EditMode·Play Mode 테스트는 실행하지 않았다.

등록, 배정, 현장 예약 집계, 경로 평가, 중복 신호 거부, 적재 한도 검사, 아이템 인계, 취소·완공 연결은 아직 구현·검증되지 않았다. 이 컴파일 성공을 실제 드론의 이동·충전·공급·회수 동작이나 전체 게임 흐름의 증거로 사용하지 않는다.
