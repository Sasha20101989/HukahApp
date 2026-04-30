namespace HookahPlatform.BuildingBlocks;

public static class DomainRules
{
    public static bool Intersects(DateTimeOffset newStart, DateTimeOffset newEnd, DateTimeOffset existingStart, DateTimeOffset existingEnd)
    {
        return newStart < existingEnd && newEnd > existingStart;
    }

    public static decimal CalculateGrams(decimal capacityGrams, decimal percent)
    {
        return Math.Round(capacityGrams * percent / 100m, 2, MidpointRounding.AwayFromZero);
    }

    public static bool PercentSumIsValid(IEnumerable<decimal> percents)
    {
        return percents.Sum() == 100m;
    }
}

public static class Clock
{
    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
