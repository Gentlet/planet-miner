using Unity.Collections;

/// <summary>
/// 역할·목적: 건물 종류를 Burst 경로의 오류 메시지에서 쓸 FixedString32Bytes 이름으로 변환한다.
/// 입력·출력: 정의된 종류의 고정 이름을 반환하며 그 밖의 값은 None으로 표기한다. 설정의 식별값이나 enum 순서는 바꾸지 않는다.
/// 이용·수명: 건물 생성/철거의 진단 호출자가 사용하며 관리 문자열 할당이나 ECS 상태 변경을 하지 않는다.
/// </summary>
public static class BuildingTypeExtensions
{
    public static FixedString32Bytes ToFixedString(this BuildingTypeEnum type)
    {
        switch (type)
        {
            case BuildingTypeEnum.Storage: return (FixedString32Bytes)"Storage";
            case BuildingTypeEnum.Miner: return (FixedString32Bytes)"Miner";
            case BuildingTypeEnum.Crafter: return (FixedString32Bytes)"Crafter";
            case BuildingTypeEnum.PowerPole: return (FixedString32Bytes)"PowerPole";
            case BuildingTypeEnum.CoalGenerator: return (FixedString32Bytes)"CoalGenerator";
            case BuildingTypeEnum.ResearchBuilding: return (FixedString32Bytes)"ResearchBuilding";
            case BuildingTypeEnum.MainFacility: return (FixedString32Bytes)"MainFacility";
            case BuildingTypeEnum.DroneStation: return (FixedString32Bytes)"DroneStation";
            case BuildingTypeEnum.Splitter: return (FixedString32Bytes)"Splitter";
            case BuildingTypeEnum.Merger: return (FixedString32Bytes)"Merger";
            case BuildingTypeEnum.Belt: return (FixedString32Bytes)"Belt";
            case BuildingTypeEnum.ConstructionSite: return (FixedString32Bytes)"ConstructionSite";
            default: return (FixedString32Bytes)"None";
        }
    }
}
