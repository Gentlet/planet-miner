using Unity.Mathematics;

/// <summary>배터리에 이미 저장된 미세 충전량을 보존해 표시 단위 미만의 충전이 사라지지 않게 한다.</summary>
public static class DroneBatteryUtility
{
    public static bool IsValid(in DroneBatteryState battery, float maximumCharge)
    {
        return math.isfinite(battery.CurrentCharge) && battery.CurrentCharge >= 0f && battery.CurrentCharge <= maximumCharge &&
            math.isfinite(battery.FractionalCharge) && battery.FractionalCharge >= 0d &&
            battery.FractionalCharge <= (double)maximumCharge - battery.CurrentCharge;
    }

    public static void AddFundedCharge(ref DroneBatteryState battery, double newCharge, float maximumCharge)
    {
        float currentChargeBefore = battery.CurrentCharge;
        double fundedCharge = battery.FractionalCharge + newCharge;
        double missingCharge = (double)maximumCharge - currentChargeBefore;
        if (fundedCharge >= missingCharge)
        {
            battery.CurrentCharge = maximumCharge;
            battery.FractionalCharge = 0d;
            return;
        }

        float nextCharge = PowerFrameUtility.RoundEnergyDown((double)currentChargeBefore + fundedCharge, maximumCharge);
        double postedCharge = (double)nextCharge - currentChargeBefore;
        // double 덧셈의 경계 반올림으로도 아직 확보하지 않은 다음 float 단위를 게시하지 않는다.
        if (postedCharge > fundedCharge)
        {
            nextCharge = math.asfloat(math.asuint(nextCharge) - 1u);
            postedCharge = (double)nextCharge - currentChargeBefore;
        }
        battery.CurrentCharge = nextCharge;
        battery.FractionalCharge = fundedCharge - postedCharge;
    }
}
