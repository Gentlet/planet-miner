using Unity.Entities;

/// <summary>설정 엔티티가 소유하는 재료 목록. 게시 후 읽기 전용이다.</summary>
[InternalBufferCapacity(0)]
public struct RecipeIngredientElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Amount;

    public RecipeIngredientElement(ItemTypeEnum itemType, int amount)
    {
        ItemType = itemType;
        Amount = amount;
    }
}

/// <summary>레시피별 첫 출력은 주생산품이며, 이후 출력은 부산물이다.</summary>
[InternalBufferCapacity(0)]
public struct RecipeOutputElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Amount;
    public bool IsByproduct;

    public RecipeOutputElement(ItemTypeEnum itemType, int amount, bool isByproduct = false)
    {
        ItemType = itemType;
        Amount = amount;
        IsByproduct = isByproduct;
    }
}

/// <summary>
/// 같은 설정 엔티티의 재료/출력 버퍼 안에서 이 레시피가 사용하는 연속 범위.
/// 시작 위치와 개수는 게시할 때 확정하며 World 종료까지 변경하지 않는다.
/// </summary>
[InternalBufferCapacity(0)]
public struct RecipeConfigElement : IBufferElementData
{
    public int Id;
    public float CraftTime;
    public byte ConditionFlags;
    public int IngredientStart;
    public int IngredientCount;
    public int OutputStart;
    public int OutputCount;

    public bool TryGetPrimaryOutput(DynamicBuffer<RecipeOutputElement> outputs, out RecipeOutputElement primaryOutput)
    {
        for (int i = 0; i < OutputCount; i++)
        {
            var output = outputs[OutputStart + i];
            if (!output.IsByproduct)
            {
                primaryOutput = output;
                return true;
            }
        }

        if (OutputCount > 0)
        {
            primaryOutput = outputs[OutputStart];
            return true;
        }

        primaryOutput = default;
        return false;
    }

    public bool TryFindIngredient(DynamicBuffer<RecipeIngredientElement> ingredients, ItemTypeEnum itemType, out int requiredAmount)
    {
        for (int i = 0; i < IngredientCount; i++)
        {
            var ingredient = ingredients[IngredientStart + i];
            if (ingredient.ItemType == itemType)
            {
                requiredAmount = ingredient.Amount;
                return true;
            }
        }

        requiredAmount = 0;
        return false;
    }
}

/// <summary>
/// RecipeConfigElement/RecipeIngredientElement/RecipeOutputElement 버퍼를 소유하는 설정 엔티티.
/// 게임 시작 시 한 번 게시하며, 버퍼의 할당과 해제는 ECS World가 관리한다.
/// </summary>
public struct RecipeRegistry : IComponentData
{
}