namespace TreasureLedger.ReplayProtocol;

public static class ReplayClock
{
    // The parser's UtcTimeStartedMatch has UTC ticks but may have Kind.Unspecified.
    // Calling ToUniversalTime on those ticks applies the player's local timezone
    // a second time (eight hours in Pacific standard time).
    public static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}
