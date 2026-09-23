using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Phase 1 CommandGroup에서 실행되는 제작기(Crafter) 레시피 변경 요청 처리 시스템.
/// 
/// [책임]
/// - ChangeCrafterRecipeRequest 요청 엔티티를 감지하여 레시피 변경을 원자적으로 수행합니다.
/// - 1. 기존 진행도 리셋 (Progress = 0, IsCraftingActive = false)
/// - 2. 잔여 StoredItemElement 재료를 ProductItemElement(출력 버퍼)로 부산물 배출(Byproduct, Slot 1+)
/// - 3. StorageFilter를 새 레시피의 재료 Whitelist로 즉시 갱신 (0 이하면 Blacklist)
/// - 4. SelectedRecipeId와 ActiveRecipeId를 새 레시피 ID로 갱신
/// - 5. 상태 전환: ProductItemElement에 아이템이 남아있으면 WaitingForByproductOutput, 없으면 Idle (또는 NoRecipe)
/// - 6. 처리 완료된 ChangeCrafterRecipeRequest 엔티티 파괴 (Consume-on-Apply)
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct CrafterRecipeCommandSystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<CrafterState> _crafterStateLookup;
    private ComponentLookup<StorageFilter> _filterLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<ProductItemElement> _productBufferLookup;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = SystemAPI.QueryBuilder()
            .WithAll<ChangeCrafterRecipeRequest>()
            .Build();

        _crafterStateLookup = state.GetComponentLookup<CrafterState>(false);
        _filterLookup = state.GetComponentLookup<StorageFilter>(false);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(false);
        _productBufferLookup = state.GetBufferLookup<ProductItemElement>(false);
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_requestQuery.IsEmpty)
        {
            return;
        }

        if (!SystemAPI.HasSingleton<RecipeRegistry>())
        {
            return;
        }

        var recipeRegistry = SystemAPI.GetSingleton<RecipeRegistry>();
        if (!recipeRegistry.Value.IsCreated)
        {
            return;
        }

        ref var registry = ref recipeRegistry.Value.Value;

        var ecbSystem = state.World.GetExistingSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        EntityCommandBuffer ecb;
        bool fallbackPlayback = false;
        if (ecbSystem != null)
        {
            ecb = ecbSystem.CreateCommandBuffer();
        }
        else
        {
            ecb = new EntityCommandBuffer(Allocator.Temp);
            fallbackPlayback = true;
        }

        _crafterStateLookup.Update(ref state);
        _filterLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);
        _productBufferLookup.Update(ref state);

        var requestEntities = _requestQuery.ToEntityArray(Allocator.Temp);
        var requests = _requestQuery.ToComponentDataArray<ChangeCrafterRecipeRequest>(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
        {
            var req = requests[i];
            var reqEntity = requestEntities[i];
            Entity crafter = req.TargetCrafter;
            int newRecipeId = req.NewRecipeId;

            if (state.EntityManager.Exists(crafter) && _crafterStateLookup.HasComponent(crafter))
            {
                var crafterState = _crafterStateLookup[crafter];

                // 1. 제작 진행도 리셋
                crafterState.Progress = 0.0f;
                crafterState.IsCraftingActive = false;

                // 2. StoredItemElement -> ProductItemElement 잔여 재료 Byproduct 배출 (Slot 1+)
                if (_storedBufferLookup.HasBuffer(crafter) && _productBufferLookup.HasBuffer(crafter))
                {
                    var storedItems = _storedBufferLookup[crafter];
                    var productItems = _productBufferLookup[crafter];

                    if (storedItems.Length > 0)
                    {
                        int maxExistingSlot = 0;
                        for (int p = 0; p < productItems.Length; p++)
                        {
                            if (productItems[p].SlotIndex > maxExistingSlot)
                            {
                                maxExistingSlot = productItems[p].SlotIndex;
                            }
                        }
                        int baseSlot = math.max(1, maxExistingSlot + 1);

                        var uniqueTypes = new FixedList32Bytes<byte>();
                        for (int s = 0; s < storedItems.Length; s++)
                        {
                            var item = storedItems[s];
                            byte typeByte = (byte)item.ItemType;
                            int typeIndex = -1;
                            for (int u = 0; u < uniqueTypes.Length; u++)
                            {
                                if (uniqueTypes[u] == typeByte)
                                {
                                    typeIndex = u;
                                    break;
                                }
                            }

                            if (typeIndex < 0 && uniqueTypes.Length < uniqueTypes.Capacity)
                            {
                                typeIndex = uniqueTypes.Length;
                                uniqueTypes.Add(typeByte);
                            }

                            int byproductSlot = baseSlot + math.max(0, typeIndex);
                            productItems.Add(new ProductItemElement(item.ItemEntity, item.ItemType, byproductSlot));
                        }

                        storedItems.Clear();
                    }

                    // 5. 상태 전환: ProductItemElement에 아이템이 남아있으면 WaitingForByproductOutput
                    if (productItems.Length > 0)
                    {
                        crafterState.Status = CrafterStatusEnum.WaitingForByproductOutput;
                    }
                    else
                    {
                        crafterState.Status = newRecipeId > 0 ? CrafterStatusEnum.Idle : CrafterStatusEnum.NoRecipe;
                    }
                }
                else
                {
                    crafterState.Status = newRecipeId > 0 ? CrafterStatusEnum.Idle : CrafterStatusEnum.NoRecipe;
                }

                // 3. StorageFilter 즉시 갱신
                if (_filterLookup.HasComponent(crafter))
                {
                    var filter = _filterLookup[crafter];
                    if (newRecipeId > 0 && registry.TryGetRecipeIndex(newRecipeId, out int newRecipeIdx))
                    {
                        filter.Mode = StorageFilterMode.Whitelist;
                        filter.Mask.Clear();
                        ref var newRecipe = ref registry.Recipes[newRecipeIdx];
                        for (int ing = 0; ing < newRecipe.Ingredients.Length; ing++)
                        {
                            filter.Mask.Set((byte)newRecipe.Ingredients[ing].ItemType, true);
                        }
                    }
                    else
                    {
                        filter.Mask.Clear();
                        filter.Mode = StorageFilterMode.Blacklist;
                    }
                    _filterLookup[crafter] = filter;
                }

                // 4. 레시피 ID 갱신
                crafterState.SelectedRecipeId = newRecipeId;
                crafterState.ActiveRecipeId = newRecipeId;
                _crafterStateLookup[crafter] = crafterState;
            }

            // 6. 요청 엔티티 파괴
            ecb.DestroyEntity(reqEntity);
        }

        requestEntities.Dispose();
        requests.Dispose();

        if (fallbackPlayback)
        {
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
