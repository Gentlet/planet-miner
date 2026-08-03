using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.UIElements;

public partial class BuildingUI
{
    private void RefreshCrafter(
        Entity configEntity,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (!_entityManager.HasBuffer<StoredItemElement>(_selectedBuilding) ||
            !_entityManager.HasBuffer<ProducedItemElement>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetCrafterLayout();
        Crafter crafter = _entityManager.GetComponentData<Crafter>(_selectedBuilding);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(_selectedBuilding, true);
        DynamicBuffer<ProducedItemElement> producedItems =
            _entityManager.GetBuffer<ProducedItemElement>(_selectedBuilding, true);
        DynamicBuffer<CrafterRecipeElement> recipes =
            _entityManager.GetBuffer<CrafterRecipeElement>(configEntity, true);
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients =
            _entityManager.GetBuffer<CrafterRecipeIngredientElement>(configEntity, true);

        CountItems(storedItems, _storedCounts, static item => item.type);
        CountItems(producedItems, _producedCounts, static item => item.type);

        BuildRecipeButtons(recipes, ingredients);

        bool hasRecipe = recipes.TryFindRecipe(
            crafter.selectedItemType,
            out CrafterRecipeElement selectedRecipe);

        UpdateRecipeSelection(crafter.selectedItemType);
        UpdateStatus(crafter, hasRecipe, selectedRecipe, ingredients);
        UpdateInventory(
            crafter,
            hasRecipe,
            selectedRecipe,
            ingredients,
            storageLimits);
        UpdateProgress(crafter, hasRecipe, selectedRecipe);
    }

    private void BuildRecipeButtons(
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        if (_recipeButtons.Count == recipes.Length)
            return;

        _recipeContainer.Clear();
        _recipeButtons.Clear();

        for (int recipeIndex = 0; recipeIndex < recipes.Length; recipeIndex++)
        {
            CrafterRecipeElement recipe = recipes[recipeIndex];
            ItemTypeEnum outputItemType = recipe.outputItemType;
            string ingredientText = GetIngredientText(ingredients, recipe.id);

            Button recipeButton = new(() => RequestRecipeChange(outputItemType))
            {
                text = $"{GetItemDisplayName(outputItemType)}\n{ingredientText}  ·  {recipe.craftTime:0.##}초"
            };

            recipeButton.AddToClassList("recipe-button");
            _recipeContainer.Add(recipeButton);
            _recipeButtons.Add(outputItemType, recipeButton);
        }
    }

    private void RequestRecipeChange(ItemTypeEnum outputItemType)
    {
        if (!_entityManager.Exists(_selectedBuilding))
            return;

        Crafter crafter = _entityManager.GetComponentData<Crafter>(_selectedBuilding);

        if (crafter.selectedItemType == outputItemType)
            return;

        Entity requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new CrafterRecipeChangeRequest
        {
            crafterEntity = _selectedBuilding,
            selectedItemType = outputItemType
        });
    }

    private void UpdateRecipeSelection(ItemTypeEnum selectedItemType)
    {
        foreach (KeyValuePair<ItemTypeEnum, Button> pair in _recipeButtons)
            pair.Value.EnableInClassList("recipe-button-selected", pair.Key == selectedItemType);
    }

    private void UpdateStatus(
        Crafter crafter,
        bool hasRecipe,
        CrafterRecipeElement recipe,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        string status;
        string statusClass;

        switch (crafter.state)
        {
            case CrafterStateEnum.NoRecipe:
                status = "레시피를 선택하세요";
                statusClass = "status-waiting";
                break;
            case CrafterStateEnum.Crafting:
                status = "제작 중";
                statusClass = "status-normal";
                break;
            case CrafterStateEnum.WaitingForOutput:
                status = "생산 아이템 보관함이 가득 찼습니다";
                statusClass = "status-error";
                break;
            case CrafterStateEnum.WaitingForExceptionItem:
                status = "새 레시피에 사용할 수 없는 재료를 꺼내야 합니다";
                statusClass = "status-error";
                break;
            default:
                if (crafter.speed <= 0f)
                {
                    status = "작동 정지";
                    statusClass = "status-error";
                }
                else if (!hasRecipe || !HasRequiredIngredients(recipe, ingredients))
                {
                    status = "재료 대기 중";
                    statusClass = "status-waiting";
                }
                else
                {
                    status = "생산 준비 중";
                    statusClass = "status-normal";
                }
                break;
        }

        SetStatus(status, statusClass);

        _currentRecipeLabel.text = hasRecipe
            ? $"현재 레시피: {GetItemDisplayName(recipe.outputItemType)}"
            : "현재 레시피: 없음";
    }

    private bool HasRequiredIngredients(
        CrafterRecipeElement recipe,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[i];

            if (ingredient.recipeId != recipe.id)
                continue;
            if (!_storedCounts.TryGetValue(ingredient.itemType, out int count) ||
                count < ingredient.amount)
                return false;
        }

        return true;
    }

    private void UpdateInventory(
        Crafter crafter,
        bool hasRecipe,
        CrafterRecipeElement recipe,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        _inputContainer.Clear();
        _outputContainer.Clear();
        _listedInputTypes.Clear();

        if (hasRecipe)
        {
            for (int i = 0; i < ingredients.Length; i++)
            {
                CrafterRecipeIngredientElement ingredient = ingredients[i];

                if (ingredient.recipeId != recipe.id)
                    continue;

                _storedCounts.TryGetValue(ingredient.itemType, out int count);
                int capacity = storageLimits.GetStorageLimit(ingredient.itemType);
                AddItemRow(
                    _inputContainer,
                    ingredient.itemType,
                    count,
                    capacity,
                    $"필요 {ingredient.amount}개",
                    false);
                _listedInputTypes.Add(ingredient.itemType);
            }
        }

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!_storedCounts.TryGetValue(itemType, out int count) ||
                _listedInputTypes.Contains(itemType))
                continue;

            int capacity = storageLimits.GetStorageLimit(itemType);
            AddItemRow(
                _inputContainer,
                itemType,
                count,
                capacity,
                hasRecipe ? "현재 레시피에서 사용 불가" : "보관 중",
                hasRecipe);
        }

        if (_inputContainer.childCount == 0)
            AddEmptyLabel(_inputContainer);

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!_producedCounts.TryGetValue(itemType, out int count))
                continue;

            int capacity = storageLimits.GetStorageLimit(itemType);
            AddItemRow(_outputContainer, itemType, count, capacity, "배출 대기", false);
        }

        if (_outputContainer.childCount == 0)
            AddEmptyLabel(_outputContainer);
    }

    private void UpdateProgress(
        Crafter crafter,
        bool hasRecipe,
        CrafterRecipeElement recipe)
    {
        float progressRatio = 0f;
        float requiredTime = 0f;

        if (hasRecipe && crafter.speed > 0f)
        {
            requiredTime = recipe.GetCraftTime(crafter.speed);

            if (crafter.state == CrafterStateEnum.WaitingForOutput)
                progressRatio = 1f;
            else if (crafter.state == CrafterStateEnum.Crafting)
                progressRatio = math.saturate(crafter.progress / requiredTime);
        }

        float progressPercent = progressRatio * 100f;
        _progressBar.value = progressPercent;
        _progressBar.title = $"{progressPercent:0}%";

        if (crafter.state == CrafterStateEnum.Crafting)
            _remainingTimeLabel.text =
                $"남은 시간: {math.max(0f, requiredTime - crafter.progress):0.0}초";
        else if (crafter.state == CrafterStateEnum.WaitingForOutput)
            _remainingTimeLabel.text = "제작 완료 · 배출 공간 대기";
        else
            _remainingTimeLabel.text = "생산 대기";

        if (!hasRecipe)
        {
            _speedLabel.text = $"생산 속도: ×{crafter.speed:0.##}";
            return;
        }

        if (crafter.speed <= 0f)
        {
            _speedLabel.text = "생산 속도: 정지";
            return;
        }

        float craftsPerMinute = 60f / requiredTime;
        _speedLabel.text =
            $"생산 속도: ×{crafter.speed:0.##}  ·  1회 {requiredTime:0.##}초  ·  분당 {craftsPerMinute:0.#}개";
    }

    private static string GetIngredientText(
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        string result = string.Empty;

        for (int i = 0; i < ingredients.Length; i++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[i];

            if (ingredient.recipeId != recipeId)
                continue;
            if (result.Length > 0)
                result += ", ";

            result += $"{GetItemDisplayName(ingredient.itemType)} ×{ingredient.amount}";
        }

        return result;
    }
}
