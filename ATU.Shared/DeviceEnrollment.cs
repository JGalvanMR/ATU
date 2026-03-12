using System.Security.Cryptography;
using System.Text;

namespace ATU.Shared;

public sealed class EnrolledDevice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string OperatorId { get; init; }
    public required string Fingerprint { get; init; }
    public required string EncryptedSecret { get; init; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset EnrolledAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class DeviceEnrollmentService(IDeviceRepository repository, IEncryptionService encryptionService)
{
    public async Task<EnrollDeviceResult> EnrollDevice(EnrollDeviceRequest request)
    {
        var existing = await repository.GetActiveByOperatorAsync(request.OperatorId);
        if (existing is not null)
        {
            existing.IsActive = false;
            existing.RevokedAt = DateTimeOffset.UtcNow;
            await repository.UpdateAsync(existing);
        }

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var fingerprint = ComputeFingerprint(request.HardwareId, request.UserAgent, request.Platform);

        var device = new EnrolledDevice
        {
            OperatorId = request.OperatorId,
            Fingerprint = fingerprint,
            EncryptedSecret = encryptionService.Encrypt(secret)
        };

        await repository.AddAsync(device);
        return new EnrollDeviceResult(device.Id, secret, fingerprint);
    }

    public async Task<DeviceValidationResult> ValidateDevice(string operatorId, string hardwareId, string userAgent, string platform)
    {
        var active = await repository.GetActiveByOperatorAsync(operatorId);
        if (active is null)
        {
            return new DeviceValidationResult(false, null, "Dispositivo no enrolado.");
        }

        var incomingFingerprint = ComputeFingerprint(hardwareId, userAgent, platform);
        if (!ATUCore.FixedTimeEquals(active.Fingerprint, incomingFingerprint))
        {
            return new DeviceValidationResult(false, null, "Fingerprint inválido.");
        }

        return new DeviceValidationResult(true, encryptionService.Decrypt(active.EncryptedSecret), "OK");
    }

    public async Task<bool> RevokeDevice(string operatorId)
    {
        var active = await repository.GetActiveByOperatorAsync(operatorId);
        if (active is null)
        {
            return false;
        }

        active.IsActive = false;
        active.RevokedAt = DateTimeOffset.UtcNow;
        await repository.UpdateAsync(active);
        return true;
    }

    public static string ComputeFingerprint(string hardwareId, string userAgent, string platform)
    {
        var payload = $"{hardwareId}|{userAgent}|{platform}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record EnrollDeviceRequest(string OperatorId, string HardwareId, string UserAgent, string Platform);
public sealed record EnrollDeviceResult(Guid DeviceId, string Secret, string Fingerprint);
public sealed record DeviceValidationResult(bool IsValid, string? Secret, string Message);

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
