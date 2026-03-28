using System.Security.Cryptography;
using System.Text;

namespace ATU.Shared;

public static class ATUCore
{
    private const int TimeStepSeconds = 30;
    private const int OtpDigits = 6;
    private const int OfflineValidWindows = 2; // 60s total para offline

    /// <summary>
    /// Genera OTP vinculado a: Producto + Recibo + Tarima + FechaCaducidad + Supervisor + Tiempo
    /// </summary>
    public static string GenerateOTP(
        string deviceSecret,
        string productoClave,
        string recibo,
        string tarima,
        string fechaCaducidad,     // DD/MM/YYYY
        string supervisorId,
        DateTimeOffset? at = null)
    {
        var batchId = $"{productoClave}-{recibo}-{tarima}";
        var window = GetTimeWindow(at ?? DateTimeOffset.UtcNow);
        return ComputeOTP(deviceSecret, batchId, fechaCaducidad, supervisorId, window);
    }

    /// <summary>
    /// Validación online completa con anti-fraude de producto
    /// </summary>
    public static ATUValidationResult Validate(
        string candidateOtp,
        string deviceSecret,
        string productoClave,      // Producto que dice el OTP
        string recibo,             // Recibo del OTP
        string tarima,             // Tarima del OTP
        string fechaCaducidad,
        string supervisorId,
        string actualProducto,     // Producto REAL escaneado
        string actualRecibo,       // Recibo REAL escaneado
        string actualTarima)       // Tarima REAL escaneada
    {
        var claimedBatchId = $"{productoClave}-{recibo}-{tarima}";
        var actualBatchId = $"{actualProducto}-{actualRecibo}-{actualTarima}";

        // 🔴 FRAUDE: Producto diferente al autorizado
        if (!string.Equals(claimedBatchId, actualBatchId, StringComparison.OrdinalIgnoreCase))
        {
            return new ATUValidationResult(
                ATUStatus.Red,
                $"FRAUDE: OTP generado para {claimedBatchId} usado en {actualBatchId}",
                false,
                claimedBatchId,
                actualBatchId);
        }

        var now = DateTimeOffset.UtcNow;
        var currentWindow = GetTimeWindow(now);

        // Validar ventana actual (30s estricto para online)
        var expected = ComputeOTP(deviceSecret, claimedBatchId, fechaCaducidad, supervisorId, currentWindow);

        if (CryptographicEquals(candidateOtp, expected))
        {
            return new ATUValidationResult(
                ATUStatus.Green,
                "Autorización válida. FIFO correcto.",
                true,
                claimedBatchId,
                actualBatchId);
        }

        // Verificar si expiró (ventana anterior)
        var previousWindow = currentWindow - 1;
        var previousExpected = ComputeOTP(deviceSecret, claimedBatchId, fechaCaducidad, supervisorId, previousWindow);

        if (CryptographicEquals(candidateOtp, previousExpected))
        {
            return new ATUValidationResult(
                ATUStatus.Yellow,
                "OTP expirado (más de 30s). Solicite nuevo código.",
                false,
                claimedBatchId,
                actualBatchId);
        }

        return new ATUValidationResult(
            ATUStatus.Red,
            "OTP inválido.",
            false,
            null,
            actualBatchId);
    }

    /// <summary>
    /// Validación offline para emergencias (sin conexión al servidor)
    /// </summary>
    public static ATUValidationResult ValidateOffline(
        string candidateOtp,
        string deviceSecret,
        string productoClave,
        string recibo,
        string tarima,
        string fechaCaducidad,
        string supervisorId,
        DateTimeOffset generatedAt)
    {
        var batchId = $"{productoClave}-{recibo}-{tarima}";
        var generationWindow = GetTimeWindow(generatedAt);
        var currentWindow = GetTimeWindow(DateTimeOffset.UtcNow);

        // OTP válido por 2 ventanas desde generación (60s máximo)
        for (int window = (int)generationWindow;
             window <= generationWindow + OfflineValidWindows && window <= currentWindow;
             window++)
        {
            var expected = ComputeOTP(deviceSecret, batchId, fechaCaducidad, supervisorId, window);

            if (CryptographicEquals(candidateOtp, expected))
            {
                bool isCurrent = window == currentWindow;
                return new ATUValidationResult(
                    isCurrent ? ATUStatus.Green : ATUStatus.Yellow,
                    isCurrent ? "Autorización válida (offline)." : "OTP expirado (offline).",
                    isCurrent,
                    batchId,
                    batchId);
            }
        }

        return new ATUValidationResult(
            ATUStatus.Red,
            "OTP inválido o expirado (>60s desde generación).",
            false,
            null,
            batchId);
    }

    public static long GetTimeWindow(DateTimeOffset time)
        => time.ToUnixTimeSeconds() / TimeStepSeconds;

    public static string ComputeOTP(
        string deviceSecret,
        string batchId,
        string fechaCaducidad,
        string supervisorId,
        long timeWindow)
    {
        // Hash de: BATCH|FECHA_CAD|SUPERVISOR|TIMEWINDOW
        var message = $"{batchId.ToUpperInvariant()}|{fechaCaducidad}|{supervisorId.ToUpperInvariant()}|{timeWindow}";
        var keyBytes = Encoding.UTF8.GetBytes(deviceSecret);
        var msgBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(msgBytes);

        // Extraer 6 dígitos numéricos
        int offset = hash[^1] & 0x0F;
        long code = ((hash[offset] & 0x7F) << 24)
                  | ((hash[offset + 1] & 0xFF) << 16)
                  | ((hash[offset + 2] & 0xFF) << 8)
                  | (hash[offset + 3] & 0xFF);

        return (code % 1000000).ToString().PadLeft(OtpDigits, '0');
    }

    public static bool CryptographicEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}

public record ATUValidationResult(
    ATUStatus Status,
    string Message,
    bool IsAuthorized,
    string? ExpectedBatchId,
    string? ActualBatchId);

public enum ATUStatus { Green, Yellow, Red }