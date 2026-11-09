using Unity.Entities;

/// <summary>
/// 역할·목적: 특정 제작기의 레시피 선택·해제를 요청하는 일회성 입력이다.
/// 부착 엔티티: 제작기와 별도인 요청 엔티티이며 TargetCrafter로 대상을 참조한다.
/// 생성: BuildingInfoView의 레시피 적용 입력이 BuildingInfoController.SetRecipe/BuildingSettingsUtility.Submit을 거쳐 요청을 만든다. Command 전에 실체화된 요청을 처리한다.
/// 이용: CrafterRecipeCommandSystem(Command)이 대상·레시피를 검사하여 작업 중단·잔여 입력 이관·필터/전용 슬롯/상태를 갱신한다.
/// 제거: 처리한 요청 엔티티는 EndCommand ECB에 삭제를 기록한다. 실패한 요청을 다음 틱에 자동 재시도하지 않는다.
/// </summary>
public struct ChangeCrafterRecipeRequest : IRequestComponent
{
    public Entity TargetCrafter;
    public int NewRecipeId;

    public ChangeCrafterRecipeRequest(Entity targetCrafter, int newRecipeId)
    {
        TargetCrafter = targetCrafter;
        NewRecipeId = newRecipeId;
    }
}
