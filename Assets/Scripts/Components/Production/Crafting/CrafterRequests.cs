using Unity.Entities;

/// <summary>
/// 제작기(Crafter)의 레시피 변경을 요청하는 1회성 명령 엔티티 컴포넌트.
/// [수명주기]: CommandGroup의 CrafterRecipeCommandSystem에서 처리 후 파괴 (Consume-on-Apply).
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
