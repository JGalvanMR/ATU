using System.Security.Cryptography;

namespace ATU.Shared;

/// <summary>
/// Representa un dispositivo móvil enrolado. 
/// Solo dispositivos enrolados pueden generar OTPs válidos.
/// </summary>
public class EnrolledDevice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string OperatorId { get; init; }       // Encargado de cámara
    public required string DeviceFingerprint { get; init; } // Hash de hw_id + user_agent + platform
    public required string EncryptedSecret { get; init; }   // Secret cifrado en reposo
    public required string PushToken { get; init; }         // Para notificaciones push
    public DateTimeOffset EnrolledAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string ColdStorageZoneId { get; init; } = string.Empty; // Zona de geofencing asignada
}

/// <summary>
/// Servicio de enrolamiento de dispositivos.
/// </summary>
public class DeviceEnrollmentService(IDeviceRepository repo, IEncryptionService encryption)
{
    /// <summary>
    /// Enrola un nuevo dispositivo. Genera el secret único del dispositivo.
    /// </summary>
    public async Task<EnrollmentResult> EnrollDeviceAsync(EnrollDeviceRequest request)
    {
        // Verificar que no haya otro dispositivo activo para este operador
        var existing = await repo.GetActiveByOperatorAsync(request.OperatorId);
        if (existing is not null)
        {
            // Revocar el dispositivo anterior automáticamente
            existing.IsActive = false;
            await repo.UpdateAsync(existing);
        }

        // Generar un secret criptográficamente seguro (32 bytes = 256 bits)
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(secretBytes);

        // Calcular fingerprint del dispositivo para vincular
        var fingerprint = ComputeFingerprint(
            request.HardwareId,
            request.UserAgent,
            request.Platform);

        var device = new EnrolledDevice
        {
            OperatorId = request.OperatorId,
            DeviceFingerprint = fingerprint,
            EncryptedSecret = encryption.Encrypt(secret),
            PushToken = request.PushToken,
            ColdStorageZoneId = request.AssignedZoneId
        };

        await repo.AddAsync(device);

        // El secret se envía UNA SOLA VEZ al momento de enrolamiento (como QR cifrado)
        return new EnrollmentResult(device.Id, secret, fingerprint);
    }

    /// <summary>
    /// Verifica que la solicitud de OTP viene del dispositivo enrolado correcto.
    /// </summary>
    public async Task<DeviceValidationResult> ValidateDeviceAsync(
        string operatorId,
        string hardwareId,
        string userAgent,
        string platform)
    {
        var device = await repo.GetActiveByOperatorAsync(operatorId);
        if (device is null)
            return new DeviceValidationResult(false, null, "Dispositivo no enrolado.");

        var incomingFingerprint = ComputeFingerprint(hardwareId, userAgent, platform);

        if (!string.Equals(device.DeviceFingerprint, incomingFingerprint, StringComparison.Ordinal))
            return new DeviceValidationResult(false, null, "Dispositivo no reconocido. Posible suplantación.");

        var secret = encryption.Decrypt(device.EncryptedSecret);
        return new DeviceValidationResult(true, secret, "OK") { Device = device };
    }

    private static string ComputeFingerprint(string hwId, string userAgent, string platform)
    {
        var raw = $"{hwId}|{userAgent}|{platform}";
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public record EnrollDeviceRequest(
    string OperatorId,
    string HardwareId,
    string UserAgent,
    string Platform,
    string PushToken,
    string AssignedZoneId);

public record EnrollmentResult(Guid DeviceId, string Secret, string Fingerprint);

public record DeviceValidationResult(bool IsValid, string? Secret, string Message)
{
    public EnrolledDevice? Device { get; init; }
}

// Abstracciones (implementadas con EF Core + Azure Key Vault en producción)
public interface IDeviceRepository
{
    Task<EnrolledDevice?> GetActiveByOperatorAsync(string operatorId);
    Task AddAsync(EnrolledDevice device);
    Task UpdateAsync(EnrolledDevice device);
}

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
