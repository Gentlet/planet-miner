using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Command에서 제작 레시피 변경/해제 요청을 검증하고 기존 제작을 중단한다.
/// 입력·생성자: 외부 ChangeCrafterRecipeRequest, RecipeRegistry/ItemRegistry와 제작기의 입력 구성·재료/생산 버퍼.
/// 출력·소유권: 상태/진행도·선택 레시피·입력 슬롯/필터/용량을 즉시 갱신하고 잔여 재료를 같은 Owner의 생산 버퍼로 옮긴다. 실물을 재생성하지 않는다.
/// 이용·정리: 이후 Decision/Execution은 변경된 레시피를 읽는다. 설정 미게시의 선택 요청은 대기하고 검증 실패/처리 요청 삭제는 EndCommand에 기록한다.
/// 가시화: 정상 게임의 구조 변경은 EndCommand에 재생한다. ECB 시스템이 없는 호출 환경은 임시 ECB를 즉시 재생 후 해제한다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct CrafterRecipeCommandSystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<CrafterState> _crafterStateLookup;
    private ComponentLookup<Storage> _storageLookup;
    private ComponentLookup<StorageFilter> _filterLookup;
    private BufferLookup<BuildingInputSlotElement> _inputSlotLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<ProductItemElement> _productBufferLookup;
    private BufferLookup<RecipeConfigElement> _recipeConfigLookup;
    private BufferLookup<RecipeIngredientElement> _recipeIngredientLookup;
    private BufferLookup<ItemConfigElement> _itemConfigLookup;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = SystemAPI.QueryBuilder()
            .WithAll<ChangeCrafterRecipeRequest>()
            .Build();

        _crafterStateLookup = state.GetComponentLookup<CrafterState>(false);
        _storageLookup = state.GetComponentLookup<Storage>(false);
        _filterLookup = state.GetComponentLookup<StorageFilter>(false);
        _inputSlotLookup = state.GetBufferLookup<BuildingInputSlotElement>(false);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(false);
        _productBufferLookup = state.GetBufferLookup<ProductItemElement>(false);
        _recipeConfigLookup = state.GetBufferLookup<RecipeConfigElement>(true);
        _recipeIngredientLookup = state.GetBufferLookup<RecipeIngredientElement>(true);
        _itemConfigLookup = state.GetBufferLookup<ItemConfigElement>(true);
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

        state.Dependency.Complete();
        bool hasRecipeRegistry = SystemAPI.TryGetSingletonEntity<RecipeRegistry>(out var recipeRegistryEntity);
        bool hasItemRegistry = SystemAPI.TryGetSingletonEntity<ItemRegistry>(out var itemRegistryEntity);

        var ecbSystem = state.World.GetExistingSystemManaged<EndCommandEntityCommandBufferSystem>();
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
        _storageLookup.Update(ref state);
        _filterLookup.Update(ref state);
        _inputSlotLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);
        _productBufferLookup.Update(ref state);
        _recipeConfigLookup.Update(ref state);
        _recipeIngredientLookup.Update(ref state);
        _itemConfigLookup.Update(ref state);

        bool hasRecipeConfig = hasRecipeRegistry && _recipeConfigLookup.HasBuffer(recipeRegistryEntity) &&
                               _recipeIngredientLookup.HasBuffer(recipeRegistryEntity);
        bool hasItemConfig = hasItemRegistry && _itemConfigLookup.HasBuffer(itemRegistryEntity);

        var requestEntities = _requestQuery.ToEntityArray(Allocator.Temp);
        var requests = _requestQuery.ToComponentDataArray<ChangeCrafterRecipeRequest>(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
        {
            var req = requests[i];
            var reqEntity = requestEntities[i];
            Entity crafter = req.TargetCrafter;
            int newRecipeId = math.max(0, req.NewRecipeId);

            if (state.EntityManager.Exists(crafter) && _crafterStateLookup.HasComponent(crafter))
            {
                if (!HasInputConfiguration(crafter))
                {
                    UnityEngine.Debug.LogError("[CrafterRecipeCommandSystem] Missing Crafter input configuration. Recipe change rejected.");
                    ecb.DestroyEntity(reqEntity);
                    continue;
                }

                // 계산/검증 실패 시 기존 진행도, 슬롯, 필터, 소유 버퍼를 모두 보존한다.
                FixedList512Bytes<BuildingInputSlotElement> slots = default;
                if (newRecipeId > 0)
                {
                    if (!hasRecipeConfig || !hasItemConfig)
                    {
                        // 설정 게시를 기다리는 요청은 소비하지 않는다. 해제는 설정 없이도 처리한다.
                        continue;
                    }

                    var recipes = _recipeConfigLookup[recipeRegistryEntity];
                    if (!RecipeConfigLookupUtility.TryGetRecipeIndex(recipes, newRecipeId, out int recipeIndex))
                    {
                        UnityEngine.Debug.LogError($"[CrafterRecipeCommandSystem] Unknown recipe {newRecipeId}. Recipe change rejected.");
                        ecb.DestroyEntity(reqEntity);
                        continue;
                    }

                    var recipe = recipes[recipeIndex];
                    if (!BuildingInputSlotUtility.TryCalculate(
                            _recipeIngredientLookup[recipeRegistryEntity], recipe.IngredientStart,
                            recipe.IngredientCount, _itemConfigLookup[itemRegistryEntity],
                            out slots, out var error))
                    {
                        UnityEngine.Debug.LogError($"[CrafterRecipeCommandSystem] Invalid input slots for recipe {newRecipeId}: {error}. Recipe change rejected.");
                        ecb.DestroyEntity(reqEntity);
                        continue;
                    }
                }

                var crafterState = _crafterStateLookup[crafter];

                // 1. 제작 진행도 리셋
                crafterState.Progress = 0.0f;
                crafterState.IsCraftingActive = false;

                // 2. StoredItemElement -> ProductItemElement 잔여 재료 Byproduct 배출 (Slot 1+)
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

                    for (int s = 0; s < storedItems.Length; s++)
                    {
                        var item = storedItems[s];
                        // 같은 품목도 여러 입력 스택을 가질 수 있다. 기존 슬롯 구분을
                        // 유지하여 잔여물 배출 슬롯의 MaxStack 초과를 방지한다.
                        int byproductSlot = baseSlot + item.SlotIndex;
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

                // 3. 입력 용량/품목별 슬롯/필터를 동일 Command 경계에서 갱신.
                var inputSlots = _inputSlotLookup[crafter];
                inputSlots.Clear();
                var filter = new StorageFilter(StorageFilterMode.Whitelist);
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    inputSlots.Add(slots[slot]);
                    filter.Mask.Set((byte)slots[slot].ItemType, true);
                }
                _storageLookup[crafter] = new Storage(slots.Length);
                _filterLookup[crafter] = filter;

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

    private bool HasInputConfiguration(Entity crafter)
    {
        return _storageLookup.HasComponent(crafter) && _filterLookup.HasComponent(crafter) &&
               _inputSlotLookup.HasBuffer(crafter) && _storedBufferLookup.HasBuffer(crafter) &&
               _productBufferLookup.HasBuffer(crafter);
    }
}
