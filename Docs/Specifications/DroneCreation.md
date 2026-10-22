# 드론 Authoring·프리팹 DB·생성 초기화

2026-10-22 Task 9.3.1의 범위다. Task 9.3의 아이템↔드론 전환·정거장 수용량·출고/수납 요청·공통 적재량 초기화는 구현하지 않는다. [Lifecycle 계약](DroneLifecycle.md)과 [정거장 네트워크](DroneStationNetwork.md)의 기존 소유자/공개 경계를 유지한다.

## Authoring과 준비 검증

DronePrefabDatabaseAuthoring은 단일 GameObject 프리팹을 명시적으로 참조한다. Baker는 별도 DB Entity에 DronePrefabDatabase와 DronePrefab을 게시하고 원형을 Dynamic으로 등록한다. Baker는 수행자·배터리·관측·화물·배정의 런타임 상태를 부착하지 않는다.

V2 Test Scene/sub.unity에는 `Drone Prefab Database` Authoring을 추가하고 기존 `Assets/Resources/Prefabs/Drone.prefab`을 연결했다. 원형은 MeshFilter/MeshRenderer만 가진 루트다. 드론 아이템 프리팹의 ItemAuthoring과 구분하며 원형 에셋 자체는 수정하지 않았다.

PrefabDatabaseInitializationSystem은 요청한 SubScene 로딩을 기다린 뒤 건물·아이템·자원·드론 4종 DB를 모두 검증한다. 드론 태그와 참조는 각각 정확히 하나이며 같은 DB Entity에 있어야 한다. 원형은 생존·Prefab·LocalTransform을 갖고 Disabled가 없어야 한다. 원형에 수행자·관측·배정·화물·배터리·아이템 역할/소유권을 미리 붙이면 거부한다. 실패는 기존 공통 초기화 Fatal/미게시/재시도 없음 계약을 따른다. 성공한 뒤 DB·원형은 불변이다.

## 생성 API와 소유자

| 데이터/변경 | Writer와 경계 | 후속 소비 |
| --- | --- | --- |
| DronePrefabDatabase / DronePrefab | DB Baker, 이후 읽기 전용 | 시작 검증, DroneSpawnUtility |
| PrefabDatabaseReady | 공통 초기화 검증 소유자 | GameSimulationGroup과 생성 API의 준비 전제 |
| 새 드론 Entity | DroneSpawnUtility.TrySpawnActiveDrone의 기록; 호출자가 제공한 EndSimulation ECB | Playback 뒤 후속 틱 |
| 기존 Entity의 수행자 초기 구성 | TryInitializeWorkerRole의 기록; 같은 EndSimulation 경계 | Task 9.3이 논리 Entity 전환에 재사용 가능 |
| 위치·기본 관측·빈 배정/화물 | 생성 초기화 1회 | 실제 관측/이동 Producer는 후속 |
| DroneBatteryState.CurrentCharge | 새 개체는 호출자가 명시한 최대 배터리로 초기화; 기존 값은 보존 | 소비/충전은 9.10·9.14 후속 |

TrySpawnActiveDrone은 World의 준비 태그를 확인하고 검증된 유일 드론 DB에서 원형을 읽어 복제한다. 이미 초기 검증한 원형의 역할/구성을 매번 다시 검사하지 않는다. 표시 원형의 회전·scale을 보존하고 위치만 입력 위치로 바꾼다. GridPosition은 해당 위치의 floor 셀이며 LocalToWorld는 초기 pose와 원형 PostTransformMatrix를 함께 반영한다.

TryInitializeWorkerRole은 기존 Entity를 교체하지 않는 초기 구성 API다. 생존·활성 루트와 LocalTransform이 필요하며 기존 수행자/화물 구성을 덮어쓰지 않는다. 기존 배터리는 유효 범위 안에서 그대로 유지한다. 동일 Entity에 같은 미재생 ECB 안에서 중복 초기화를 기록하지 않는 것은 호출자의 중재 계약이다. 이미 공개된 중복 초기화는 실패하며 기존 위치·배터리·화물을 보존한다.

모든 호출 입력을 확인한 뒤 명령을 기록한다. 준비 전·비유한 위치/배터리·int 격자 범위 밖 좌표·0 이하 최대 배터리는 실패와 오류 문자열을 반환하며 원본이나 ECB를 일부 변경하지 않는다. 실패 결과의 요청 소비/외부 결과/Fatal 선택은 호출자가 소유한다. Shared ECB를 rollback으로 간주하지 않는다.

생성 후 DroneWorkerObservation은 입력 위치·Revision 1·CanAcceptTask=false다. 배정은 Null, 화물 버퍼는 비어 있고 출처는 None이다. 실제 이동·경로/행동 Producer 없이 배차 가능 상태나 성공 결과를 만들지 않는다.

## 이번 범위와 후속 연결

- 이번 배터리 상태는 현재량 하나만 가진다. 최대치를 개체마다 복제하지 않는다. 최대 배터리 값은 호출자가 명시하는 필수 입력이다. 현재 DroneConfig의 `maximumBattery`를 읽어 제품 입력으로 전달하는 소유자는 Task 9.3에서 연결한다. 공통 carryingCapacity 게시도 이번 범위에 포함하지 않는다.
- 이번 생성 유틸리티는 아이템 역할/Owner/소유 버퍼를 제거하거나 정거장 슬롯·예약을 정산하지 않는다. 기존 Entity의 표시를 아이템↔드론 원형으로 바꾸는 책임도 Task 9.3에 남긴다. 기존 Entity 재사용 API의 존재를 전체 Identity 전환 완료로 해석하지 않는다.
- 독립적인 자동 생성/배차 Producer를 추가하지 않았다. 후속 9.3·9.22의 소유자가 EndSimulation ECB를 제공해 호출한다. 새 작업·배정·경로의 다음 틱 사용 계약은 유지한다.
- 수용량·벨트 입출고·귀환·수납·충전·이동·연구 정책은 해당 후속 Task의 범위다. 기존 ItemConfig 이동과 MaxStack 50, 레거시 설정 보관 변경은 보존한다.

## 검증

새 DronePrefabAndSpawnTests는 기존 테스트에 없던 드론 원형 생성→EndSimulation 공개·초기 상태, 기존 루트/배터리 보존과 중복 초기화 거부, 실패 시 원본 아이템 보존을 검증한다. 공통 PrefabDatabaseInitializationTests에는 드론 DB 누락·중복·무효 원형의 시작 차단을 추가했다. 그 외 기존 테스트의 의미나 assertion을 완화하지 않았다.

현재 코드의 Unity 컴파일 오류 없음과 선택 EditMode 56/56(실패·생략·불확정 0)을 확인했다: DronePrefabAndSpawnTests 6, PrefabDatabaseInitializationTests 26, Phase1ItemAuthoringPrefabTests 4, DroneLifecycleIntegrationTests 6, DroneStationNetworkTests 14.

저장된 V2 SubScene 루트들을 현재 Entities의 실제 Baker로 격리 World에 베이킹했다. 현재 제품 설정 초기화와 4종 DB 검증 뒤 Ready 1/Fatal 0, 드론 DB 1을 확인했다. 실제 베이킹 원형의 MaterialMeshInfo와 생성 드론의 렌더 데이터, scale 0.5, 입력 위치, 배터리 100, CanAcceptTask=false도 확인했다. 배터리 100은 이 probe의 명시적 입력이며 제품 DroneConfig 로더 실행 증거가 아니다.

베이킹 probe는 설치된 Entities 패키지의 내부 BakingUtility를 reflection으로 호출한 검증 도구이며 제품 코드에 추가하지 않았다. 초기 두 eval은 검증 스크립트의 시스템 생성 API 해석 오류로 실행되지 않았고, public World.GetOrCreateSystem(Type)로 바꾼 최종 probe가 성공했다. 프로젝트 C# 컴파일 오류와 구분한다.

초기화/생성 반영/컴포넌트 계약/guard/공개 경계의 독립 읽기 전용 검수 5개 역할에서 확인한 범위의 지적은 없었다. 최종 주석과 문서도 생성 초기 Writer를 반영했다. Play Mode/실제 비행/표시/Player 빌드/성능은 이번 검증에 포함하지 않는다.
