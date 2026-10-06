using Unity.Collections;

/// <summary>
/// 역할·목적: 품목 종류를 Burst 경로의 오류 메시지에서 쓸 FixedString32Bytes 이름으로 변환한다.
/// 입력·출력: 정의된 enum 이름을 반환하며 그 밖의 값은 None으로 표기한다. 품목 설정의 존재/유효성을 검증하는 함수는 아니다.
/// 이용·수명: 아이템 생성·환급 등의 진단 호출자가 사용하며 관리 문자열 할당이나 ECS 상태 변경을 하지 않는다.
/// </summary>
public static class ItemTypeExtensions
{
    public static FixedString32Bytes ToFixedString(this ItemTypeEnum type)
    {
        switch (type)
        {
            case ItemTypeEnum.Iron_Ore: return (FixedString32Bytes)"Iron_Ore";
            case ItemTypeEnum.Copper_Ore: return (FixedString32Bytes)"Copper_Ore";
            case ItemTypeEnum.Coal: return (FixedString32Bytes)"Coal";
            case ItemTypeEnum.Stone: return (FixedString32Bytes)"Stone";
            case ItemTypeEnum.Iron: return (FixedString32Bytes)"Iron";
            case ItemTypeEnum.Copper: return (FixedString32Bytes)"Copper";
            case ItemTypeEnum.Iron_Stick: return (FixedString32Bytes)"Iron_Stick";
            case ItemTypeEnum.Copper_Stick: return (FixedString32Bytes)"Copper_Stick";
            case ItemTypeEnum.Drone: return (FixedString32Bytes)"Drone";
            default: return (FixedString32Bytes)"None";
        }
    }
}
