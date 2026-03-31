using Microsoft.AspNetCore.Mvc;
using ATU.Shared;
using ATU.Shared.Models;

namespace ATU.AuthService.Controllers;

[ApiController]
[Route("api/otp")]
public class OTPController : ControllerBase
{
    private readonly IAuditEventPublisher _audit;
    private readonly IDeviceRepository _devices;
    private readonly IEncryptionService _encryption;
    private readonly IOtpRepository _otps;

    public OTPController(
        IAuditEventPublisher audit,
        IDeviceRepository devices,
        IEncryptionService encryption,
        IOtpRepository otps)
    {
        _audit = audit;
        _devices = devices;
        _encryption = encryption;
        _otps = otps;
    }

    public class MobileGenerateRequest
    {
        public string SupervisorId { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public string DeviceFingerprint { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class MobileValidateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public string SupervisorId { get; set; } = string.Empty;
        public string DeviceFingerprint { get; set; } = string.Empty;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] MobileGenerateRequest request)
    {
        var device = await _devices.GetByIdAsync(request.SupervisorId);
        if (device == null)
            return Ok(new { success = false, message = $"Supervisor '{request.SupervisorId}' no enrolado.", data = (object?)null, errors = new[] { "NOT_ENROLLED" } });

        var secret = _encryption.Decrypt(device.EncryptedSecret);
        var otp = ATUCore.GenerateOTP(secret, request.BatchId, "", request.SupervisorId);

        var record = new OtpRecord
        {
            Otp = otp,
            BatchId = request.BatchId,
            SupervisorId = request.SupervisorId,
            OperatorId = request.SupervisorId,
            GeneratedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ATUCore.TtlSeconds),
            IsUsed = false
        };
        await _otps.SaveAsync(record);

        await _audit.PublishAsync(new AuditEvent
        {
            Type = "OTP_GENERATED",
            SupervisorId = request.SupervisorId,
            BatchId = request.BatchId,
            Message = $"OTP generado: {otp}",
            Timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new
        {
            success = true,
            message = "OTP generado correctamente",
            data = new
            {
                code = otp,
                generatedAt = DateTime.UtcNow,
                expiresAt = DateTime.UtcNow.AddSeconds(ATUCore.TtlSeconds),
                secondsRemaining = ATUCore.TimeStepSeconds,
                batchId = request.BatchId,
                transactionId = Guid.NewGuid().ToString()
            },
            errors = new List<string>()
        });
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] MobileValidateRequest request)
    {
        var stored = await _otps.GetByBatchAndSupervisorAsync(request.BatchId, request.SupervisorId);
        if (stored == null)
            return Ok(new { success = false, status = "Red", message = "No hay OTP pendiente para este embarque.", isAuthorized = false });

        if (stored.IsUsed)
            return Ok(new { success = false, status = "Red", message = "OTP ya fue utilizado.", isAuthorized = false });

        if (stored.ExpiresAt < DateTimeOffset.UtcNow)
            return Ok(new { success = false, status = "Red", message = "OTP expirado. Solicite uno nuevo.", isAuthorized = false });

        if (stored.Otp != request.Code)
            return Ok(new { success = false, status = "Red", message = "Código incorrecto.", isAuthorized = false });

        stored.IsUsed = true;
        stored.UsedAt = DateTimeOffset.UtcNow;
        await _otps.UpdateAsync(stored);

        await _audit.PublishAsync(new AuditEvent
        {
            Type = "OTP_VALIDATED",
            SupervisorId = request.SupervisorId,
            BatchId = request.BatchId,
            Message = $"Código {request.Code} validado",
            Timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new
        {
            success = true,
            status = "Green",
            message = "Autorización validada correctamente",
            isAuthorized = true,
            supervisorId = stored.SupervisorId
        });
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] string batchId)
    {
        var pending = await _otps.GetPendingByBatchAsync(batchId);
        if (pending == null)
            return Ok(new { success = false, message = "Sin OTP pendiente.", supervisorId = (string?)null });

        return Ok(new
        {
            success = true,
            message = "OTP pendiente encontrado",
            supervisorId = pending.SupervisorId,
            expiresAt = pending.ExpiresAt.DateTime
        });
    }
}