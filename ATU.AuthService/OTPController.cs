using Microsoft.AspNetCore.Mvc;
using ATU.Shared;
using ATU.Shared.Models;

namespace ATU.AuthService.Controllers;

[ApiController]
[Route("api/otp")]
public class OTPController : ControllerBase
{
    private readonly IAuditEventPublisher _audit;
    private readonly IGeofenceService _geofence;
    private readonly IDeviceRepository _devices;
    private readonly IEncryptionService _encryption;

    public OTPController(
        IAuditEventPublisher audit,
        IGeofenceService geofence,
        IDeviceRepository devices,
        IEncryptionService encryption)
    {
        _audit = audit;
        _geofence = geofence;
        _devices = devices;
        _encryption = encryption;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateOTPRequest request)
    {
        var device = await _devices.GetByIdAsync(request.OperatorId);
        if (device == null)
            return Unauthorized(new { error = "Dispositivo no enrolado" });

        // Validar geofencing
        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            var geo = await _geofence.ValidateAsync(
                device.ColdStorageZoneId,
                request.Latitude.Value,
                request.Longitude.Value);

            if (!geo.IsValid)
                return BadRequest(new { error = geo.Message });
        }

        // CORREGIDO: Desencriptar el secret antes de usarlo
        var secret = _encryption.Decrypt(device.EncryptedSecret);

        var otp = ATUCore.GenerateOTP(
            secret,
            request.ProductoClave,
            request.Recibo,
            request.Tarima,
            request.FechaCaducidad,
            request.SupervisorId);

        await _audit.PublishAsync(new AuditEvent
        {
            Type = "OTP_GENERATED",
            SupervisorId = request.SupervisorId,
            BatchId = $"{request.ProductoClave}-{request.Recibo}-{request.Tarima}",
            Timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new GenerateOTPResponse(
            otp,
            DateTimeOffset.UtcNow.AddSeconds(30),
            request.ProductoClave,
            request.Recibo,
            request.Tarima,
            request.FechaCaducidad));
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidateOTPRequest request)
    {
        var device = await _devices.GetByIdAsync(request.OperatorId);
        if (device == null)
            return Unauthorized(new { error = "Dispositivo no encontrado" });

        // CORREGIDO: Desencriptar el secret
        var secret = _encryption.Decrypt(device.EncryptedSecret);

        var result = ATUCore.Validate(
            request.Otp,
            secret,
            request.ProductoClave,
            request.Recibo,
            request.Tarima,
            request.FechaCaducidad,
            request.SupervisorId,
            request.ProductoClave,
            request.Recibo,
            request.Tarima);

        await _audit.PublishAsync(new AuditEvent
        {
            Type = result.Status.ToString(),
            SupervisorId = request.SupervisorId,
            IsAuthorized = result.IsAuthorized,
            Message = result.Message,
            Timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new ValidateOTPResponse(
            result.Status.ToString(),
            result.Message,
            result.IsAuthorized,
            result.ExpectedBatchId?.Split('-')[0],
            result.ExpectedBatchId?.Split('-')[1],
            result.ExpectedBatchId?.Split('-')[2]));
    }
}