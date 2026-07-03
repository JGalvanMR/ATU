using System.Security.Cryptography;
using System.Text;

namespace ATU.Shared;

public static class ATUCore
{
    // ── Constantes públicas que usan el Controller y los Tests ───────────────
    public const int TtlSeconds = 30;   // Vida del OTP
    public const int TimeStepSeconds = 30;   // Ventana TOTP

    private const int OtpDigits = 6;

    // ── Overload simplificado (batchId ya construido externamente) ───────────
    // Firma: (secret, batchId, supervisorId, at?)
    // Usada por: OTPController y ATUCoreTests
    public static string GenerateOTP(
        string deviceSecret,
        string batchId,
        string supervisorId,
        DateTimeOffset? at = null)
    {
        var window = GetTimeWindow(at ?? DateTimeOffset.UtcNow);
        return ComputeOTP(deviceSecret, batchId, supervisorId, window);
    }

    // ── Overload completo (construye batchId internamente) ───────────────────
    // Firma: (secret, prodClave, recibo, tarima, fechaCad, supervisorId, at?)
    // Usada por: ATUCore.Validate interno
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
        var window = GetTimeWindow(at ?? DateTimeOffset.UtcNow);
        return ComputeOTP(deviceSecret, batchId, supervisorId, window);
    }

    // ── Validación FIFO-aware ────────────────────────────────────────────────
    // Verifica: OTP correcto + lote reclamado == lote real (anti-sustitución)
    public static ATUValidationResult ValidateFIFO(
        string candidateOtp,
        string deviceSecret,
        string claimedBatchId,   // batchId del OTP generado
        string actualBatchId,    // batchId real escaneado por embarques
        string supervisorId)
    {
        // 🔴 Fraude por sustitución de lote
        if (!string.Equals(claimedBatchId.Trim(), actualBatchId.Trim(),StringComparison.OrdinalIgnoreCase))
        {
            return new ATUValidationResult(ATUStatus.Red,$"FRAUDE: OTP de '{claimedBatchId}' usado en '{actualBatchId}'.",false,claimedBatchId,actualBatchId);
        }

        var now = DateTimeOffset.UtcNow;
        var currentWindow = GetTimeWindow(now);

        // Ventana actual (0–30 s)
        var expected = ComputeOTP(deviceSecret, claimedBatchId, supervisorId, currentWindow);
        if (CryptographicEquals(candidateOtp, expected))
            return new ATUValidationResult(ATUStatus.Green, "Autorización FIFO válida.", true, claimedBatchId, actualBatchId);

        // Ventana anterior (30–60 s) → amarillo
        var prevExpected = ComputeOTP(deviceSecret, claimedBatchId, supervisorId, currentWindow - 1);
        if (CryptographicEquals(candidateOtp, prevExpected))
            return new ATUValidationResult(ATUStatus.Yellow,"OTP expirado (>30 s). Solicite un nuevo código.",false, claimedBatchId, actualBatchId);

        return new ATUValidationResult(ATUStatus.Red,"OTP inválido.",false, null, actualBatchId);
    }

    // ── Validación simple (sin check FIFO, compatible con tests) ────────────
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
        => $"{prodClave.Trim()}-{recibo.Trim()}-{tarima.Trim()}";

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

    // Alias para los tests existentes
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

    // Alias para los tests que llaman FixedTimeEquals directamente
    public static bool FixedTimeEquals(string a, string b)
        => CryptographicEquals(a, b);

    // Para el test de ReplayAttack que llama ResetReplayCache
    // (el replay se controla en DB, no en memoria; este método es no-op)
    public static void ResetReplayCache() { }
}

// ── Tipos de resultado ───────────────────────────────────────────────────────

public record ATUValidationResult(
    ATUStatus Status,
    string Message,
    bool IsAuthorized,
    string? ExpectedBatchId,
    string? ActualBatchId);

public enum ATUStatus { Green, Yellow, Red }

// Alias de resultado para compatibilidad con tests
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