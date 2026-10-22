# 레거시 설정 보관

현재 제품 로더가 사용하지 않는 대체 완료 설정을 보관한다. JSON 내용과 Unity GUID는 이동 전 그대로 유지한다. 이 경로는 Resources 폴더가 아니며 현재 제품 설정 경로로 사용하지 않는다.

| 보관 파일 | 현재 설정 원본 |
| --- | --- |
| StorageLimitConfig.json | [Resources/Config/ItemConfig.json](../../Resources/Config/ItemConfig.json)의 품목별 MaxStack |
| ResourceGenerationConfig.json | [Resources/Config/WorldGenerationConfig.json](../../Resources/Config/WorldGenerationConfig.json)의 자원·바닥 통합 설정 |

DroneConfig.json, ResearchConfig.json, StartingItemConfig.json은 후속 기능의 설정 입력이므로 Resources/Config에 유지한다. 제품 Reader가 아직 없다는 이유만으로 레거시 설정으로 분류하지 않는다.
