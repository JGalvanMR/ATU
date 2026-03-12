using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ATU.Shared;

public static class ATUCore
{
    public const int TimeStepSeconds = 30;
    public const int TtlSeconds = 90;
    private const int OtpDigits = 8;

    private static readonly ConcurrentDictionary<string, DateTimeOffset> UsedOtps = new();

    public static string GenerateOTP(
        string secret,
        string batchId,
        string supervisorId,
        DateTimeOffset? timestamp = null)
    {
        var timeWindow = GetTimeWindow(timestamp ?? DateTimeOffset.UtcNow);
        var hmac = ComputeHMAC(secret, timeWindow, batchId, supervisorId);

        var offset = hmac[^1] & 0x0F;
        var binaryCode = ((hmac[offset] & 0x7F) << 24)
                         | ((hmac[offset + 1] & 0xFF) << 16)
                         | ((hmac[offset + 2] & 0xFF) << 8)
                         | (hmac[offset + 3] & 0xFF);

        var otp = (binaryCode % (int)Math.Pow(10, OtpDigits)).ToString();
        return otp.PadLeft(OtpDigits, '0');
    }

    public static OtpValidationResult ValidateOTP(
        string candidateOtp,
        string secret,
        string claimedBatchId,
        string actualBatchId,
        string supervisorId,
        DateTimeOffset? now = null)
    {
        if (!string.Equals(claimedBatchId, actualBatchId, StringComparison.OrdinalIgnoreCase))
        {
            return new OtpValidationResult(false, OtpValidationStatus.Fraud, "Batch inválido: posible fraude.");
        }

        var utcNow = now ?? DateTimeOffset.UtcNow;
        PurgeExpiredReplayLocks(utcNow);

        var currentWindow = GetTimeWindow(utcNow);
        var oldestAllowedWindow = GetTimeWindow(utcNow.AddSeconds(-TtlSeconds));

        for (var window = currentWindow; window >= oldestAllowedWindow; window--)
        {
            var expectedOtp = GenerateOTP(secret, claimedBatchId, supervisorId, FromTimeWindow(window));
            if (!FixedTimeEquals(candidateOtp, expectedOtp))
            {
                continue;
            }

            var replayKey = BuildReplayKey(candidateOtp, claimedBatchId, supervisorId, window);
            if (!UsedOtps.TryAdd(replayKey, utcNow.AddSeconds(TtlSeconds)))
            {
                return new OtpValidationResult(false, OtpValidationStatus.ReplayAttack, "OTP ya utilizado.");
            }

            var status = window == currentWindow
                ? OtpValidationStatus.Valid
                : OtpValidationStatus.PreviousWindow;

            return new OtpValidationResult(true, status, status == OtpValidationStatus.Valid
                ? "OTP válido."
                : "OTP válido en ventana previa; solicitar nuevo OTP.");
        }

        return new OtpValidationResult(false, OtpValidationStatus.ExpiredOrInvalid, "OTP inválido o expirado.");
    }

    public static byte[] ComputeHMAC(string secret, long timeWindow, string batchId, string supervisorId)
    {
        var payload = $"{timeWindow}|{batchId.ToUpperInvariant()}|{supervisorId.ToUpperInvariant()}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    public static long GetTimeWindow(DateTimeOffset timestamp) => timestamp.ToUnixTimeSeconds() / TimeStepSeconds;

    private static DateTimeOffset FromTimeWindow(long timeWindow) => DateTimeOffset.FromUnixTimeSeconds(timeWindow * TimeStepSeconds);

    public static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string BuildReplayKey(string otp, string batchId, string supervisorId, long timeWindow)
        => $"{otp}:{batchId}:{supervisorId}:{timeWindow}";

    private static void PurgeExpiredReplayLocks(DateTimeOffset now)
    {
        foreach (var entry in UsedOtps)
        {
            if (entry.Value <= now)
            {
                UsedOtps.TryRemove(entry.Key, out _);
            }
        }
    }
}

public enum OtpValidationStatus
{
    Valid,
    PreviousWindow,
    ExpiredOrInvalid,
    Fraud,
    ReplayAttack
}

public sealed record OtpValidationResult(bool IsValid, OtpValidationStatus Status, string Message);
