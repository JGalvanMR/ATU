using ATU.Shared;

namespace ATU.Tests;

public class ATUCoreTests
{
    public ATUCoreTests()
    {
        ATUCore.ResetReplayCache();
    }
    private const string Secret = "secret-device-32bytes-value-123456";
    private const string Batch = "BATCH-100";
    private const string OtherBatch = "BATCH-200";
    private const string Supervisor = "SUP-1";
    private const string OtherSupervisor = "SUP-2";

    [Fact]
    public void ValidOtp_ShouldPass()
    {
        var now = DateTimeOffset.UtcNow;
        var otp = ATUCore.GenerateOTP(Secret, Batch, Supervisor, now);

        var result = ATUCore.ValidateOTP(otp, Secret, Batch, Batch, Supervisor, now);

        Assert.True(result.IsValid);
        Assert.Equal(OtpValidationStatus.Valid, result.Status);
    }

    [Fact]
    public void ExpiredOtp_ShouldFail()
    {
        var oldTime = DateTimeOffset.UtcNow.AddSeconds(-ATUCore.TtlSeconds - 10);
        var otp = ATUCore.GenerateOTP(Secret, Batch, Supervisor, oldTime);

        var result = ATUCore.ValidateOTP(otp, Secret, Batch, Batch, Supervisor, DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
        Assert.Equal(OtpValidationStatus.ExpiredOrInvalid, result.Status);
    }

    [Fact]
    public void ReplayAttack_ShouldBeDetected()
    {
        var now = DateTimeOffset.UtcNow;
        var otp = ATUCore.GenerateOTP(Secret, Batch, Supervisor, now);

        var first = ATUCore.ValidateOTP(otp, Secret, Batch, Batch, Supervisor, now);
        var second = ATUCore.ValidateOTP(otp, Secret, Batch, Batch, Supervisor, now.AddSeconds(1));

        Assert.True(first.IsValid);
        Assert.False(second.IsValid);
        Assert.Equal(OtpValidationStatus.ReplayAttack, second.Status);
    }

    [Fact]
    public void WrongBatch_ShouldFailAsFraud()
    {
        var otp = ATUCore.GenerateOTP(Secret, Batch, Supervisor, DateTimeOffset.UtcNow);

        var result = ATUCore.ValidateOTP(otp, Secret, Batch, OtherBatch, Supervisor, DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
        Assert.Equal(OtpValidationStatus.Fraud, result.Status);
    }

    [Fact]
    public void WrongSupervisor_ShouldFail()
    {
        var otp = ATUCore.GenerateOTP(Secret, Batch, Supervisor, DateTimeOffset.UtcNow);

        var result = ATUCore.ValidateOTP(otp, Secret, Batch, Batch, OtherSupervisor, DateTimeOffset.UtcNow);

        Assert.False(result.IsValid);
        Assert.Equal(OtpValidationStatus.ExpiredOrInvalid, result.Status);
    }

    [Fact]
    public void ConstantTimeComparison_BehavesAsExpected()
    {
        Assert.True(ATUCore.FixedTimeEquals("12345678", "12345678"));
        Assert.False(ATUCore.FixedTimeEquals("12345678", "12345679"));
    }

    [Fact]
    public void TimeWindow_Behavior_IsStableWithinWindow()
    {
        var t1 = DateTimeOffset.FromUnixTimeSeconds(120);
        var t2 = DateTimeOffset.FromUnixTimeSeconds(149);
        var t3 = DateTimeOffset.FromUnixTimeSeconds(150);

        Assert.Equal(ATUCore.GetTimeWindow(t1), ATUCore.GetTimeWindow(t2));
        Assert.NotEqual(ATUCore.GetTimeWindow(t2), ATUCore.GetTimeWindow(t3));
    }

    [Fact]
    public void HmacConsistency_ShouldMatchForSameInput()
    {
        var window = 777L;
        var h1 = ATUCore.ComputeHMAC(Secret, window, Batch, Supervisor);
        var h2 = ATUCore.ComputeHMAC(Secret, window, Batch, Supervisor);

        Assert.True(h1.SequenceEqual(h2));
    }
}
