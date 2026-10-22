# Planet Miner 문서 안내

기준: 2026-10-06. 현재 구현 설명, 합의된 설계, 과거 구조와 실행 기록을 구분해서 읽는다. 실제 코드와 사용자 결정이 문서보다 우선하며 공통 규칙은 저장소 루트의 AGENTS.md 한 곳에 둔다.

| 확인할 내용 | 시작 문서 | 역할과 한계 |
| --- | --- | --- |
| 현재 실행 순서·소유권·변경 및 검증 규칙 | [AGENTS.md](../AGENTS.md) | 프로젝트의 기준 문서. Preparation/Power를 포함한 도메인 그룹과 네 ECB 경계, 구현/후속 범위를 설명한다. |
| ECS 타입의 필드·Writer/Reader·수명 | [컴포넌트 색인](CodeMemory/Components/README.md) | 도메인별 타입과 해당 소유 시스템을 추적한다. 과거 전체 조사 수치는 현재 전체 수와 구분한다. 타입이나 enum의 존재를 기능 완료로 판단하지 않는다. |
| 코드 흐름과 주요 파일 위치 | [V2 코드 지도](architecture%20v2%20plan/CodeMemory/README.md), [C# 파일 색인](architecture%20v2%20plan/CodeMemory/CSharpFileIndex.md) | 현재 주요 경로의 탐색 보조. 전체 파일의 개별 내용 재감사나 실행 성공 증거는 아니다. |
| 건설·공급·회수의 확정 규칙 | [건설·드론 명세](Specifications/ConstructionAndDroneSupply.md) | 관리 계층 구현과 후속 수행부를 구분한다. 자동 비행·실제 도착 신호 Producer는 후속이다. |
| 드론 상태 전이·귀환·충전·수납 정책 | [드론 Lifecycle 계약](Specifications/DroneLifecycle.md) | Task 9.1에서 확정한 1A~6A와 상태/이벤트/Owner·검증 기준. 정거장 기반은 Task 9.2, 실제 수행자·이동·충전은 해당 후속 Task에서 다룬다. |
| 드론 정거장 등록·활동 범위·네트워크 | [정거장 네트워크 명세](Specifications/DroneStationNetwork.md) | Task 9.2의 단일 Range·정사각형·무전력 참가·대표·같은 틱 연결/공개 조회 계약. 작업 필터·수용량·충전은 후속이다. |
| 드론 Authoring·DB·생성 초기화 | [드론 생성 명세](Specifications/DroneCreation.md) | Task 9.3.1의 실제 Baker·4종 DB 준비·EndSimulation 생성 초기화. Identity 전환·수용량·제품 배차/이동/충전은 후속이다. |
| 공통 적재량 초기화 후속 | [공통 적재량 초기화 계약](Specifications/DroneCapacityInitializationPlan.md) | 6A 정책은 사용자 확정이며 구현은 Task 9.3에 남는다. Task 9.3.1은 생성 초기 구성만 다룬다. |
| 연구의 합의된 게임 규칙 | [연구건물 명세](Specifications/ResearchBuilding.md) | 설계 규칙과 임시 데이터를 구분한다. 현재 V2 연구 진행 Writer의 구현 증거는 아니다. |
| 개발 마일스톤·설계 배경 | [V2 작업 목록](architecture%20v2%20plan/Architecture%20V2%20Tasks.md), [V2 설계](architecture%20v2%20plan/Architecture%20V2%20Plan_0.2.md) | Phase 번호와 체크는 해당 개발 작업의 범위다. 현재 런타임 그룹 수/순서나 전체 기능 완료를 뜻하지 않는다. |
| 아직 이식하지 않은 게임 동작·정책 차이 | [V2 작업 목록](architecture%20v2%20plan/Architecture%20V2%20Tasks.md)의 도메인별 레거시 검토 항목 | 이전 CodeMemory에서 필요한 동작만 보존했다. 현재 확정 계약과 충돌하는 내용은 후속 결정 사항이며 구현 승인이나 완료를 뜻하지 않는다. |

문서가 충돌하면 현재 코드의 타입·쿼리·Writer/Reader·그룹 속성·ECB 소비 시점을 확인한다. 코드 정의, 테스트 구성, 실제 실행 결과는 서로 다른 근거다. 미확정 게임 규칙은 문서 보완 과정에서 새로 정하지 않는다.
