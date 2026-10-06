using Unity.Entities;
using UnityEngine;

/// <summary>
/// 역할·목적: 아이템 프리팹 원형의 품목 식별 데이터를 베이킹에 제공한다.
/// 부착 대상: 프리팹 GameObject의 MonoBehaviour. ItemAuthoringBaker는 Dynamic 엔티티에 ItemIdentity만 추가한다.
/// 이용: ItemPrefabDatabaseAuthoring의 참조와 런타임 프리팹 검증/생성이 베이킹 결과를 사용한다.
/// 수명·경계: 위치·Owner·요청·이동 상태는 런타임 생성자가 초기화한다. Authoring 제거/재베이킹과 런타임 실물 삭제를 구분한다.
/// </summary>
[DisallowMultipleComponent]
public class ItemAuthoring : MonoBehaviour
{
    [Tooltip("아이템 고유 식별 타입")]
    public ItemTypeEnum Type;

    /// <summary>Unity 베이킹 시 원형의 품목만 게시한다. 실물의 위치/소유권/수명주기 요청은 만들지 않는다.</summary>
    public class ItemAuthoringBaker : Baker<ItemAuthoring>
    {
        public override void Bake(ItemAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ItemIdentity(authoring.Type));
        }
    }
}
