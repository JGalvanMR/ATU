using System.Security.Cryptography;
using System.Text;

namespace ATU.Shared;

public static class ATUCore
{
    // ── Constantes públicas ─────────────────────────────────────────────────
    // ✅ CAMBIO 1: Aumentamos el tiempo de vida real a 90 segundos (1.5 min)
    // Esto da tiempo de que el supervisor lo lea y el operador lo teclee
    public const int TtlSeconds = 90;
    public const int TimeStepSeconds = 30;

    private const int OtpDigits = 6;

    // ── Overload simplificado ───────────────────────────────────────────────
    public static string GenerateOTP(
        string deviceSecret,
        string batchId,
        string supervisorId,
        DateTimeOffset? at = null)
    {
        var window = GetTimeWindow(at ?? DateTimeOffset.Now);
        return ComputeOTP(deviceSecret, batchId, supervisorId, window);
    }

    // ── Overload completo ───────────────────────────────────────────────────
    public static string GenerateOTP(
        string deviceSecret,
        string productoClave,
        string recibo,
        string tarima,
        string fechaCaducidad,
        string supervisorId,
        DateTimeOffset? at = null)
    {
        var batchId = BuildBatchId(productoClave, recibo, tarima);
        var window = GetTimeWindow(at ?? DateTimeOffset.Now);
        return ComputeOTP(deviceSecret, batchId, supervisorId, window);
    }

    // ── Validación FIFO-aware ───────────────────────────────────────────────
    public static ATUValidationResult ValidateFIFO(
        string candidateOtp,
        string deviceSecret,
        string claimedBatchId,
        string actualBatchId,
        string supervisorId)
    {
        // Fraude por sustitución de lote
        if (!string.Equals(claimedBatchId.Trim(), actualBatchId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new ATUValidationResult(ATUStatus.Red, $"FRAUDE: OTP de '{claimedBatchId}' usado en '{actualBatchId}'.", false, claimedBatchId, actualBatchId);
        }

        var now = DateTimeOffset.Now;
        var currentWindow = GetTimeWindow(now);

        // ✅ CAMBIO 2: Nueva lógica de ventanas "Warehouse-Friendly"

        // 1. Ventana actual (0–30 s) -> VÁLIDO
        var expected = ComputeOTP(deviceSecret, claimedBatchId, supervisorId, currentWindow);
        if (CryptographicEquals(candidateOtp, expected))
            return new ATUValidationResult(ATUStatus.Green, "Autorización FIFO válida.", true, claimedBatchId, actualBatchId);

        // 2. Ventana anterior (30–60 s) -> VÁLIDO (Tolerancia para cruce de segundo)
        var prevExpected = ComputeOTP(deviceSecret, claimedBatchId, supervisorId, currentWindow - 1);
        if (CryptographicEquals(candidateOtp, prevExpected))
            return new ATUValidationResult(ATUStatus.Green, "Autorización FIFO válida.", true, claimedBatchId, actualBatchId);

        // 3. Ventana futura (+30s) -> VÁLIDO (Por si el celular del supervisor va 30s adelantado)
        var nextExpected = ComputeOTP(deviceSecret, claimedBatchId, supervisorId, currentWindow + 1);
        if (CryptographicEquals(candidateOtp, nextExpected))
            return new ATUValidationResult(ATUStatus.Green, "Autorización FIFO válida.", true, claimedBatchId, actualBatchId);

        // 4. Ventana lejana (-60 a -90 s) -> AMARILLO (Expirado pero reciente)
        var oldExpected = ComputeOTP(deviceSecret, claimedBatchId, supervisorId, currentWindow - 2);
        if (CryptographicEquals(candidateOtp, oldExpected))
            return new ATUValidationResult(ATUStatus.Yellow, "OTP expirado (>60 s). Solicite un nuevo código.", false, claimedBatchId, actualBatchId);

        return new ATUValidationResult(ATUStatus.Red, "OTP inválido.", false, null, actualBatchId);
    }

    // ── Validación simple ───────────────────────────────────────────────────
    public static ATUValidationResult ValidateOTP(
        string candidateOtp,
        string deviceSecret,
        string claimedBatchId,
        string actualBatchId,
        string supervisorId,
        DateTimeOffset? at = null)
        => ValidateFIFO(candidateOtp, deviceSecret, claimedBatchId, actualBatchId, supervisorId);

    // ── Utilidades ───────────────────────────────────────────────────────────

    public static string BuildBatchId(string prodClave, string recibo, string tarima)
        => $"{recibo.Trim()}-{prodClave.Trim()}-{tarima.Trim()}";

    public static long GetTimeWindow(DateTimeOffset time)
        => time.ToUnixTimeSeconds() / TimeStepSeconds;

    public static string ComputeOTP(string deviceSecret, string batchId, string supervisorId, long timeWindow)
    {
        var message = $"{batchId.ToUpperInvariant()}|{supervisorId.ToUpperInvariant()}|{timeWindow}";
        var keyBytes = Encoding.UTF8.GetBytes(deviceSecret);
        var msgBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(msgBytes);

        int offset = hash[^1] & 0x0F;
        long code = ((hash[offset] & 0x7F) << 24)
                    | ((hash[offset + 1] & 0xFF) << 16)
                    | ((hash[offset + 2] & 0xFF) << 8)
                    | (hash[offset + 3] & 0xFF);

        return (code % 1_000_000).ToString().PadLeft(OtpDigits, '0');
    }

    public static byte[] ComputeHMAC(string secret, long window, string batchId, string supervisorId)
    {
        var message = $"{batchId.ToUpperInvariant()}|{supervisorId.ToUpperInvariant()}|{window}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    }

    public static bool CryptographicEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a.PadRight(OtpDigits, '0'));
        var bBytes = Encoding.UTF8.GetBytes(b.PadRight(OtpDigits, '0'));
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    public static bool FixedTimeEquals(string a, string b)
        => CryptographicEquals(a, b);

    public static void ResetReplayCache() { }
}

public record ATUValidationResult(
    ATUStatus Status,
    string Message,
    bool IsAuthorized,
    string? ExpectedBatchId,
    string? ActualBatchId);

public enum ATUStatus { Green, Yellow, Red }

public class OtpValidationResult
{
    public bool IsValid { get; init; }
    public OtpValidationStatus Status { get; init; }
}

public enum OtpValidationStatus
{
    Valid,
    ExpiredOrInvalid,
    ReplayAttack,
    Fraud
}