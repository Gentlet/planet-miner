/// <summary>설정 ID의 카테고리와 비영 일련번호를 판정한다.</summary>
public static class ConfigIdUtility
{
    public const int ItemCategory = 120;
    public const int ResearchCategory = 140;
    public const int FloorBiomeCategory = 150;

    public static bool IsInCategory(int id, int category)
    {
        return id / 100000 == category && id % 100000 != 0;
    }
}
