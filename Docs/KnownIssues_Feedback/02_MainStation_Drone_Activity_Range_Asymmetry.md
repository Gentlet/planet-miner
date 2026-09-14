# 메인 스테이션 드론 작업 범위 좌우 비대칭 문제

## 1. 개요

- **구분**: 버그 / 알려진 문제 (Bug / Known Issue)
- **상태**: 구현 완료 (Resolved)
- **관련 시스템**: `DroneStationNetworkSystem`, `DroneStationRangeUtility`, `MainFacilityBootstrapSystem`

---

## 2. 현상 및 문제점

- 메인 스테이션의 드론 작업 범위가 우측은 길고 좌측은 짧게 적용되었습니다.
- 3×3 시설의 실제 중심이 아니라 앵커가 속한 청크 전체를 기준으로 범위를 만들었기 때문에 청크 안의 앵커 위치만큼 범위가 편향되었습니다.

---

## 3. 요구사항

- 메인 스테이션의 실제 점유 영역을 기준으로 좌우·상하 대칭인 작업 범위를 적용합니다.
- 일반 드론 정거장과 회전 가능한 footprint에도 같은 계산 규칙을 적용합니다.
- 음수 활동 범위는 기존과 같이 0으로 정규화합니다.
- 범위 변경 후에도 정거장 네트워크 병합·분리와 드론 작업 탐색이 정상 동작해야 합니다.

---

## 4. 구현 결과

- `DroneStation`에 생성 당시의 정규화된 `footprintSize`를 저장합니다.
- 활동 범위는 회전된 footprint의 최소·최대 점유 셀을 계산한 뒤, 각 방향으로 `activityRangeInChunks * GameConstants.chunkSize`만큼 확장합니다.
- 3×3 메인 스테이션이 `(0,0)`에 있고 범위가 1청크일 때 활동 범위는 기존 `(-16,-16) ~ (31,31)`에서 `(-16,-16) ~ (18,18)`로 변경됩니다.
- 새 범위는 시설 중심 `(1,1)`을 기준으로 좌우·상하가 대칭이며, 일반 정거장도 동일하게 실제 footprint를 기준으로 계산합니다.

---

## 5. 완료 기준 및 검증

- [x] 메인 스테이션 3×3 footprint의 양쪽 경계가 중심 기준으로 대칭입니다.
- [x] 경계 바로 바깥 셀은 작업 범위에서 제외됩니다.
- [x] 회전된 비정사각형 footprint의 점유 경계를 올바르게 반영합니다.
- [x] 정거장 네트워크 병합·분리 동작을 유지합니다.
- [x] Unity 스크립트 재컴파일 성공
- [x] `DroneStationNetworkTests` EditMode 테스트 8개 통과
- [x] `DroneBuildingItemTaskTests` EditMode 테스트 7개 통과
- [x] `DroneDemolitionRecoveryTests` EditMode 테스트 7개 통과
- [x] `ActiveDroneTransportTests` EditMode 테스트 16개 통과

Play Mode에서의 실제 작업 가능 범위, 입력, UI 및 시각적 범위 표시는 별도로 확인하지 않았습니다.
