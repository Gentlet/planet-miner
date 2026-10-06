# Planet Miner 문서 안내

기준: 2026-10-06. 현재 구현 설명, 합의된 설계, 과거 구조와 실행 기록을 구분해서 읽는다. 실제 코드와 사용자 결정이 문서보다 우선하며 공통 규칙은 저장소 루트의 AGENTS.md 한 곳에 둔다.

| 확인할 내용 | 시작 문서 | 역할과 한계 |
| --- | --- | --- |
| 현재 실행 순서·소유권·변경 및 검증 규칙 | [AGENTS.md](../AGENTS.md) | 프로젝트의 기준 문서. 도메인 그룹과 세 ECB 경계, 구현/후속 범위를 설명한다. |
| ECS 타입의 필드·Writer/Reader·수명 | [컴포넌트 색인](CodeMemory/Components/README.md) | 현재 정의 103개와 해당 소유 시스템을 추적한다. 타입이나 enum의 존재를 기능 완료로 판단하지 않는다. |
| 코드 흐름과 주요 파일 위치 | [V2 코드 지도](architecture%20v2%20plan/CodeMemory/README.md), [C# 파일 색인](architecture%20v2%20plan/CodeMemory/CSharpFileIndex.md) | 현재 주요 경로의 탐색 보조. 전체 파일의 개별 내용 재감사나 실행 성공 증거는 아니다. |
| 건설·공급·회수의 확정 규칙 | [건설·드론 명세](Specifications/ConstructionAndDroneSupply.md) | 관리 계층 구현과 후속 수행부를 구분한다. 자동 비행·실제 도착 신호 Producer는 후속이다. |
| 아직 결정이 필요한 초기화 | [공통 적재량 초기화 검토안](Specifications/DroneCapacityInitializationPlan.md) | 설정 출처·오류·기존 singleton 처리 정책이 미확정인 제안이다. 구현 승인으로 해석하지 않는다. |
| 연구의 합의된 게임 규칙 | [연구건물 명세](Specifications/ResearchBuilding.md) | 설계 규칙과 임시 데이터를 구분한다. 현재 V2 연구 진행 Writer의 구현 증거는 아니다. |
| 개발 마일스톤·설계 배경 | [V2 작업 목록](architecture%20v2%20plan/Architecture%20V2%20Tasks.md), [V2 설계](architecture%20v2%20plan/Architecture%20V2%20Plan_0.2.md) | Phase 번호와 체크는 해당 개발 작업의 범위다. 현재 런타임 그룹 수/순서나 전체 기능 완료를 뜻하지 않는다. |
| 실제 변경 검증 | [도메인 분리 검증](architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDroneDomainSplit-Verification.md) | 컴파일·선택 EditMode·제어 입력 Play Mode·유휴 성능 표본과 한계를 따로 기록한다. 이전 결과를 합산하거나 새 구조로 소급 해석하지 않는다. |
| 이전 구조의 도메인 상세·피드백 | CodeMemory 바로 아래 문서, [과거 피드백 목록](KnownIssues_Feedback/README.md) | 각 문서 첫머리의 참고 기록 표시를 따른다. 옛 심볼·기능을 현재 V2 동작으로 읽지 않는다. |

문서가 충돌하면 현재 코드의 타입·쿼리·Writer/Reader·그룹 속성·ECB 소비 시점을 확인한다. 코드 정의, 테스트 구성, 실제 실행 결과는 서로 다른 근거다. 미확정 게임 규칙은 문서 보완 과정에서 새로 정하지 않는다.
