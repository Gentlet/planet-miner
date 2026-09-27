using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// V2 Test Scene 전용 진단 표시. ECS 바닥 선택 결과를 한 장의 색상 텍스처로 표시한다.
/// 실제 바닥 Sprite 메쉬 렌더링은 Task 10B.6에서 구현한다.
/// </summary>
public sealed class V2FloorBiomePreview : MonoBehaviour
{
    [SerializeField, Range(0, 4)] private int chunkRadius = 1;
    [SerializeField] private Color grassColor = new Color(0.33f, 0.65f, 0.29f);
    [SerializeField] private Color dirtColor = new Color(0.55f, 0.38f, 0.22f);
    [SerializeField] private Color transitionColor = new Color(0.75f, 0.67f, 0.36f);

    private Texture2D texture;
    private Sprite sprite;
    private GameObject previewObject;

    private void Update()
    {
        if (previewObject != null) return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager entityManager = world.EntityManager;
        EntityQuery query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ResourceGenerationSettings>(),
            ComponentType.ReadOnly<FloorGenerationSettings>(),
            ComponentType.ReadOnly<FloorBiomeElement>(),
            ComponentType.ReadOnly<FloorVariantElement>());

        try
        {
            if (!query.HasSingleton<ResourceGenerationSettings>()) return;

            Entity entity = query.GetSingletonEntity();
            uint worldSeed = entityManager.GetComponentData<ResourceGenerationSettings>(entity).WorldSeed;
            FloorGenerationSettings settings = entityManager.GetComponentData<FloorGenerationSettings>(entity);
            DynamicBuffer<FloorBiomeElement> biomes = entityManager.GetBuffer<FloorBiomeElement>(entity);
            DynamicBuffer<FloorVariantElement> variants = entityManager.GetBuffer<FloorVariantElement>(entity);
            BuildPreview(worldSeed, settings, biomes, variants);
        }
        finally
        {
            query.Dispose();
        }
    }

    private void BuildPreview(
        uint worldSeed,
        in FloorGenerationSettings settings,
        in DynamicBuffer<FloorBiomeElement> biomes,
        in DynamicBuffer<FloorVariantElement> variants)
    {
        int radius = Mathf.Clamp(chunkRadius, 0, 4);
        int size = (radius * 2 + 1) * ChunkUtility.ChunkSize;
        int minCell = -radius * ChunkUtility.ChunkSize;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "V2 Floor Biome Preview",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var cell = new int2(minCell + x, minCell + y);
                FloorTileSelection selection = FloorBiomeSampler.SelectFloor(worldSeed, settings, biomes, variants, cell);
                Color color = selection.UsesTransitionVariant
                    ? transitionColor
                    : selection.BiomeIndex == 0 ? grassColor : dirtColor;
                float brightness = 0.88f + selection.VariantIndex * 0.06f;
                Color displayColor = color * brightness;
                displayColor.a = 1f;
                texture.SetPixel(x, y, displayColor);
            }
        }

        texture.Apply(false, true);
        sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 1f);
        sprite.name = "V2 Floor Biome Preview";
        sprite.hideFlags = HideFlags.DontSave;

        previewObject = new GameObject("Floor Biome Preview (Runtime)") { hideFlags = HideFlags.DontSave };
        previewObject.transform.SetParent(transform, false);
        previewObject.transform.position = new Vector3(minCell + size * 0.5f, minCell + size * 0.5f, 1f);
        SpriteRenderer renderer = previewObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = -1000;
    }

    private void OnDestroy()
    {
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
        if (previewObject != null) Destroy(previewObject);
    }
}
