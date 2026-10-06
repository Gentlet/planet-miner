using Unity.Entities;

/// <summary>
/// 건물·벨트·생산·아이템 수명주기를 한 번 실행하고 EndBuilding에서 결과를 확정한다.
/// 드론은 확정된 ECS 상태를 읽는다. 공사 완공도 이 그룹에서 판단하며 이번 틱 드론 결과는 다음 틱에 읽는다.
/// </summary>
[UpdateInGroup(typeof(GameSimulationGroup))]
[UpdateAfter(typeof(CommandGroup))]
public partial class BuildingSimulationGroup : ComponentSystemGroup
{
}

