// Assets/Systems/RewardType.cs
namespace Pulseforge.Systems
{
    /// <summary>
    /// 게임에서 사용하는 자원 타입. TopBar 및 보상 테이블과 호환.
    /// 필요한 타입이 생기면 여기에 추가하면 됨.
    /// </summary>
    public enum RewardType
    {
        Crystal = 0,
        Gold = 1,
        Shard = 2,   // ✅ ResourceHUD가 참조하는 항목
        // (원한다면 아래처럼 확장)
        // Iron = 3,
        // Gem  = 4,
        // Energy = 5,
    }

    /// <summary>표시용 유틸 (라벨 등)</summary>
    public static class RewardTypeUtil
    {
        public static string Short(this RewardType t)
        {
            switch (t)
            {
                case RewardType.Crystal: return "Crystal";
                case RewardType.Gold: return "Gold";
                case RewardType.Shard: return "Shard";
                default: return t.ToString();
            }
        }
    }
}
