using System.Security.Cryptography;
using System.Text;

namespace ATU.Shared;

/// <summary>
/// Autorización Transaccional Única (ATU) Core
/// 
/// El código OTP se genera como:
///   HMAC-SHA256( DeviceSecret | TimeWindow | BatchID | SupervisorID )
/// Esto hace que un código generado para el Lote A sea INVÁLIDO para el Lote B.
/// </summary>
public static class ATUCore
{
    // Ventana de tiempo: 90 segundos (3 ventanas de 30s para tolerancia de reloj)
    private const int TimeStepSeconds = 30;
    private const int OtpDigits = 8;
    private const int ValidWindowCount = 3; // ±1.5 min

    /// <summary>
    /// Genera el OTP vinculado a lote + supervisor + dispositivo + tiempo.
    /// </summary>
    public static string GenerateOTP(
        string deviceSecret,
        string batchId,
        string supervisorId,
        DateTimeOffset? at = null)
    {
        var window = GetTimeWindow(at ?? DateTimeOffset.UtcNow);
        return ComputeOTP(deviceSecret, batchId, supervisorId, window);
    }

    /// <summary>
    /// Valida el OTP. Devuelve el resultado con semántica de semáforo.
    /// </summary>
    public static ATUValidationResult Validate(
        string candidateOtp,
        string deviceSecret,
        string claimedBatchId,
        string actualBatchId,
        string supervisorId)
    {
        // ROJO: El código fue generado para un lote distinto → FRAUDE
        if (!string.Equals(claimedBatchId, actualBatchId, StringComparison.OrdinalIgnoreCase))
        {
            return new ATUValidationResult(
                ATUStatus.Red,
                "FRAUDE: Código vinculado a otro lote. Alerta registrada.",
                false);
        }

        var now = DateTimeOffset.UtcNow;
        var currentWindow = GetTimeWindow(now);

        // Validar en ventana actual y adyacentes (tolerancia de reloj)
        for (int delta = -ValidWindowCount; delta <= ValidWindowCount; delta++)
        {
            var window = currentWindow + delta;
            var expected = ComputeOTP(deviceSecret, claimedBatchId, supervisorId, window);

            if (CryptographicEquals(candidateOtp, expected))
            {
                bool isFresh = delta == 0;
                return new ATUValidationResult(
                    isFresh ? ATUStatus.Green : ATUStatus.Yellow,
                    isFresh ? "Autorización válida." : "Código expirado. Solicite uno nuevo.",
                    isFresh);
            }
        }

        return new ATUValidationResult(ATUStatus.Red, "Código inválido o no existe.", false);
    }

    // ── Internals ──────────────────────────────────────────────────────────

    private static long GetTimeWindow(DateTimeOffset time)
        => time.ToUnixTimeSeconds() / TimeStepSeconds;

    private static string ComputeOTP(
        string deviceSecret,
        string batchId,
        string supervisorId,
        long timeWindow)
    {
        // El mensaje incluye TODOS los contextos: si cambia cualquiera, el hash cambia
        var message = $"{batchId.ToUpperInvariant()}|{supervisorId.ToUpperInvariant()}|{timeWindow}";
        var keyBytes = Encoding.UTF8.GetBytes(deviceSecret);
        var msgBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(msgBytes);

        // Extraer 8 dígitos con offset dinámico (similar a RFC 6238 TOTP)
        int offset = hash[^1] & 0x0F;
        long code = ((hash[offset] & 0x7F) << 24)
                  | ((hash[offset + 1] & 0xFF) << 16)
                  | ((hash[offset + 2] & 0xFF) << 8)
                  | (hash[offset + 3] & 0xFF);

        return (code % (long)Math.Pow(10, OtpDigits)).ToString().PadLeft(OtpDigits, '0');
    }

    /// <summary>
    /// Comparación en tiempo constante para evitar timing attacks.
    /// </summary>
    private static bool CryptographicEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}

public record ATUValidationResult(ATUStatus Status, string Message, bool IsAuthorized);

public enum ATUStatus { Green, Yellow, Red }
