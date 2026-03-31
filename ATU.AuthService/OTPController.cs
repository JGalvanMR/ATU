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

    public OTPController(
        IAuditEventPublisher audit,
        IDeviceRepository devices,
        IEncryptionService encryption)
    {
        _audit = audit;
        _devices = devices;
        _encryption = encryption;
    }

    /// <summary>
    /// DTO que espera la App Android
    /// </summary>
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
        // 1. Validar que el supervisor existe (usamos el OperatorId de prueba "12345")
        var device = await _devices.GetByIdAsync(request.SupervisorId);
        if (device == null)
        {
            return Ok(new
            {
                success = false,
                message = $"Supervisor '{request.SupervisorId}' no enrolado. Use '12345' para prueba.",
                data = (object?)null,
                errors = new List<string> { "NOT_ENROLLED" }
            });
        }

        // 2. GENERAR OTP MOCK (Conectividad pura)
        var random = new Random();
        var otp = random.Next(100000, 999999).ToString();

        await _audit.PublishAsync(new AuditEvent
        {
            Type = "OTP_GENERATED",
            SupervisorId = request.SupervisorId,
            BatchId = request.BatchId,
            Message = $"OTP Mock generado: {otp}",
            Timestamp = DateTimeOffset.UtcNow
        });

        // 3. Regresar EXACTAMENTE el formato que espera el Android (OTPResponse)
        return Ok(new
        {
            success = true,
            message = "OTP generado correctamente",
            data = new
            {
                code = otp,
                generatedAt = DateTime.UtcNow,
                expiresAt = DateTime.UtcNow.AddSeconds(30),
                secondsRemaining = 30,
                batchId = request.BatchId,
                transactionId = Guid.NewGuid().ToString()
            },
            errors = new List<string>()
        });
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] MobileValidateRequest request)
    {
        // Validación mock - siempre aprueba por ahora para probar flujo
        await _audit.PublishAsync(new AuditEvent
        {
            Type = "OTP_VALIDATED",
            SupervisorId = request.SupervisorId,
            BatchId = request.BatchId,
            Message = $"Código {request.Code} validado (Mock)",
            Timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new
        {
            success = true,
            status = "Green",
            message = "Autorización validada correctamente",
            isAuthorized = true
        });
    }
}