using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 역할·목적: V2 Test Scene에서 ECS 바닥 선택 결과를 하나의 색상 텍스처로 확인하는 진단 표시다.
/// 부착 대상: 진단용 GameObject의 MonoBehaviour. Update는 기본 World의 월드/바닥 설정 singleton을 기다렸다가 한 번 생성한다.
/// 입력·출력: FloorBiomeSampler의 결과를 Texture2D/Sprite/SpriteRenderer로 표시하며 ECS 셀 선택·월드 상태는 변경하지 않는다.
/// 수명·제거: 임시 표시 오브젝트·Sprite·Texture는 OnDestroy에서 회수한다. 설정 변경을 매 프레임 재표시하거나 실제 바닥 청크를 렌더링하는 시스템은 아니다.
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
        // 초기 설정 게시 전에는 대기하고, 표시를 만든 뒤에는 임시 텍스처를 반복 생성하지 않는다.
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

        // CPU 픽셀 쓰기는 여기서 끝낸다. 이후 표시는 정적 Sprite로 유지하고 ECS 원본을 대신하는 캐시로 사용하지 않는다.
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
