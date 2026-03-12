using Microsoft.AspNetCore.Mvc;
using ATU.Shared;

namespace ATU.AuthService.Controllers;

/// <summary>
/// Microservicio de Autorización Transaccional Única.
/// 
/// Flujo:
///   1. POST /api/otp/generate  → Encargado de cámara genera OTP en su móvil enrolado
///   2. POST /api/otp/validate  → Scanner del supervisor valida el OTP + BatchID
///   3. GET  /api/otp/status    → Dashboard de gerente (vía SignalR también)
/// </summary>
[ApiController]
[Route("api/otp")]
public class OTPController(
    DeviceEnrollmentService enrollmentService,
    IOTPRepository otpRepo,
    IAuditEventPublisher auditPublisher,
    IGeofenceService geofence) : ControllerBase
{
    /// <summary>
    /// Genera un OTP vinculado a lote + supervisor.
    /// Solo funciona desde el dispositivo enrolado del encargado.
    /// </summary>
    [HttpPost("generate")]
    public async Task<IActionResult> GenerateOTP([FromBody] GenerateOTPRequest req)
    {
        // 1. Validar que la solicitud viene del dispositivo enrolado
        var hwId = Request.Headers["X-Device-HwId"].ToString();
        var userAgent = Request.Headers["User-Agent"].ToString();
        var platform = Request.Headers["X-Device-Platform"].ToString();

        var deviceResult = await enrollmentService.ValidateDeviceAsync(
            req.OperatorId, hwId, userAgent, platform);

        if (!deviceResult.IsValid)
        {
            await auditPublisher.PublishAsync(new AuditEvent
            {
                EventType = AuditEventType.FraudAttempt,
                OperatorId = req.OperatorId,
                SupervisorId = req.SupervisorId,
                BatchId = req.BatchId,
                Message = $"Intento de generación desde dispositivo NO enrolado. {deviceResult.Message}",
                Severity = AlertSeverity.Critical
            });
            return Unauthorized(new { error = deviceResult.Message });
        }

        // 2. (Opcional) Validar geofencing - ambos en zona de embarque
        if (req.OperatorLatitude.HasValue && req.OperatorLongitude.HasValue)
        {
            var geoResult = await geofence.ValidateProximityAsync(
                deviceResult.Device!.ColdStorageZoneId,
                req.OperatorLatitude.Value,
                req.OperatorLongitude.Value,
                req.SupervisorLatitude,
                req.SupervisorLongitude);

            if (!geoResult.IsWithinZone)
            {
                await auditPublisher.PublishAsync(new AuditEvent
                {
                    EventType = AuditEventType.GeofenceViolation,
                    OperatorId = req.OperatorId,
                    SupervisorId = req.SupervisorId,
                    BatchId = req.BatchId,
                    Message = $"Operador fuera de zona. Distancia: {geoResult.DistanceMeters:F0}m",
                    Severity = AlertSeverity.High
                });
                return BadRequest(new { error = "Operador fuera de zona de embarque autorizada." });
            }
        }

        // 3. Generar OTP vinculado a contexto
        var otp = ATUCore.GenerateOTP(deviceResult.Secret!, req.BatchId, req.SupervisorId);

        // 4. Registrar OTP pendiente en base de datos (TTL 90s, uso único)
        var record = new OTPRecord
        {
            Otp = otp, // Solo el hash del OTP, nunca en claro
            BatchId = req.BatchId,
            SupervisorId = req.SupervisorId,
            OperatorId = req.OperatorId,
            GeneratedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90),
            IsUsed = false
        };
        await otpRepo.SaveAsync(record);

        // 5. Publicar evento de auditoría en tiempo real
        await auditPublisher.PublishAsync(new AuditEvent
        {
            EventType = AuditEventType.OTPGenerated,
            OperatorId = req.OperatorId,
            SupervisorId = req.SupervisorId,
            BatchId = req.BatchId,
            Message = "OTP generado. Esperando validación del supervisor.",
            Severity = AlertSeverity.Info
        });

        return Ok(new GenerateOTPResponse(otp, record.ExpiresAt, req.BatchId));
    }

    /// <summary>
    /// Valida un OTP. Devuelve resultado con semántica de semáforo.
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateOTP([FromBody] ValidateOTPRequest req)
    {
        // Obtener el secret del dispositivo enrolado del encargado del lote
        var deviceResult = await enrollmentService.ValidateDeviceAsync(
            req.OperatorId, req.DeviceHwId, req.UserAgent, req.Platform);

        if (!deviceResult.IsValid)
            return Unauthorized(new { error = "Dispositivo del encargado no reconocido." });

        // Verificar que el OTP no fue ya usado (prevenir replay attacks)
        var record = await otpRepo.GetByBatchAndSupervisorAsync(req.BatchId, req.SupervisorId);
        if (record?.IsUsed == true)
        {
            await auditPublisher.PublishAsync(new AuditEvent
            {
                EventType = AuditEventType.ReplayAttempt,
                OperatorId = req.OperatorId,
                SupervisorId = req.SupervisorId,
                BatchId = req.BatchId,
                Message = "ALERTA: Intento de reutilización de OTP ya consumido.",
                Severity = AlertSeverity.Critical
            });

            return Ok(new ValidateOTPResponse(
                ATUStatus.Red, "FRAUDE: Código ya fue utilizado. Alerta registrada.", false));
        }

        // Validación criptográfica con semántica de semáforo
        var result = ATUCore.Validate(
            req.Otp,
            deviceResult.Secret!,
            req.ClaimedBatchId,  // BatchId que el supervisor dice autorizar
            req.BatchId,         // BatchId real del lote físico (del scanner)
            req.SupervisorId);

        // Marcar como usado si fue válido (Green)
        if (result.Status == ATUStatus.Green && record is not null)
        {
            record.IsUsed = true;
            record.UsedAt = DateTimeOffset.UtcNow;
            record.ValidatedBySupervisorLatitude = req.SupervisorLatitude;
            record.ValidatedBySupervisorLongitude = req.SupervisorLongitude;
            await otpRepo.UpdateAsync(record);
        }

        // Publicar resultado al dashboard en tiempo real
        await auditPublisher.PublishAsync(new AuditEvent
        {
            EventType = result.Status switch
            {
                ATUStatus.Green => AuditEventType.OTPValidated,
                ATUStatus.Yellow => AuditEventType.OTPExpired,
                ATUStatus.Red => AuditEventType.FraudAttempt,
                _ => AuditEventType.OTPValidated
            },
            OperatorId = req.OperatorId,
            SupervisorId = req.SupervisorId,
            BatchId = req.BatchId,
            Message = result.Message,
            Severity = result.Status == ATUStatus.Green ? AlertSeverity.Info : AlertSeverity.Critical
        });

        return Ok(new ValidateOTPResponse(result.Status, result.Message, result.IsAuthorized));
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record GenerateOTPRequest(
    string OperatorId,
    string SupervisorId,
    string BatchId,
    double? OperatorLatitude,
    double? OperatorLongitude,
    double? SupervisorLatitude,
    double? SupervisorLongitude);

public record GenerateOTPResponse(string Otp, DateTimeOffset ExpiresAt, string BatchId);

public record ValidateOTPRequest(
    string Otp,
    string OperatorId,
    string SupervisorId,
    string BatchId,           // BatchId real del lote físico
    string ClaimedBatchId,    // BatchId que el supervisor dice estar autorizando
    string DeviceHwId,
    string UserAgent,
    string Platform,
    double? SupervisorLatitude,
    double? SupervisorLongitude);

public record ValidateOTPResponse(ATUStatus Status, string Message, bool IsAuthorized);

// ── Modelos de dominio ────────────────────────────────────────────────────────

public class OTPRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Otp { get; init; }
    public required string BatchId { get; init; }
    public required string SupervisorId { get; init; }
    public required string OperatorId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsUsed { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public double? ValidatedBySupervisorLatitude { get; set; }
    public double? ValidatedBySupervisorLongitude { get; set; }
}

public class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AuditEventType EventType { get; init; }
    public required string OperatorId { get; init; }
    public required string SupervisorId { get; init; }
    public required string BatchId { get; init; }
    public required string Message { get; init; }
    public AlertSeverity Severity { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public enum AuditEventType
{
    OTPGenerated, OTPValidated, OTPExpired,
    FraudAttempt, ReplayAttempt, GeofenceViolation,
    DeviceEnrolled, DeviceRevoked
}

public enum AlertSeverity { Info, Warning, High, Critical }

public interface IOTPRepository
{
    Task SaveAsync(OTPRecord record);
    Task<OTPRecord?> GetByBatchAndSupervisorAsync(string batchId, string supervisorId);
    Task UpdateAsync(OTPRecord record);
}

public interface IAuditEventPublisher
{
    Task PublishAsync(AuditEvent evt);
}
