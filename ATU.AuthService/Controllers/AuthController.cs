using ATU.Shared.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ATU.AuthService.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IDeviceRepository _devices;
    private readonly IEncryptionService _encryption;
    private readonly string _connStr;

    public AuthController(
        IDeviceRepository devices,
        IEncryptionService encryption,
        IConfiguration configuration)
    {
        _devices = devices;
        _encryption = encryption;
        _connStr = configuration.GetConnectionString("SqlServer") ?? string.Empty;
    }

    public class LoginMobileRequest
    {
        public string EmployeeNumber { get; set; } = string.Empty;
        public string DeviceFingerprint { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginMobileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeNumber))
            return Ok(Fail("Número de empleado requerido."));

        // ── 1. Validar contra Tb_Autoriza_OdeP ───────────────────────────────
        string supervisorNombre;
        try
        {
            if (string.IsNullOrWhiteSpace(_connStr))
                return Ok(Fail("Cadena de conexión SQL Server no configurada."));

            await using var conn = new SqlConnection(_connStr);

            // Busca por número de empleado (campo 'usuario') con permiso 'EM'
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT usuario, obs
                FROM   Tb_Autoriza_OdeP
                WHERE  LTRIM(RTRIM(usuario)) = @emp
                  AND  LTRIM(RTRIM(clave))   = 'EM'",
                new { emp = request.EmployeeNumber.Trim() });

            if (row == null)
                return Ok(Fail("Empleado no encontrado o sin permiso de autorización."));

            supervisorNombre = ((string?)row.obs)?.Trim() ?? request.EmployeeNumber;
        }
        catch (Exception ex)
        {
            return Ok(Fail($"Error de base de datos: {ex.Message}"));
        }

        // ── 2. Auto-enrolar dispositivo (o reusar el existente) ───────────────
        var device = await _devices.GetByIdAsync(request.EmployeeNumber.Trim());

        if (device == null)
        {
            // Primer login en este dispositivo → crear enrolamiento automático
            var secret = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            device = new EnrolledDevice
            {
                OperatorId = request.EmployeeNumber.Trim(),
                Fingerprint = request.DeviceFingerprint,
                EncryptedSecret = _encryption.Encrypt(secret),
                PushToken = string.Empty,
                ColdStorageZoneId = string.Empty,
                IsActive = true,
                EnrolledAt = DateTimeOffset.UtcNow
            };
            await _devices.AddAsync(device);
        }
        else if (!string.IsNullOrEmpty(request.DeviceFingerprint)
              && device.Fingerprint != request.DeviceFingerprint)
        {
            // El mismo empleado inició sesión en un dispositivo diferente → actualizar
            var updated = new EnrolledDevice
            {
                OperatorId = device.OperatorId,
                Fingerprint = request.DeviceFingerprint,
                EncryptedSecret = device.EncryptedSecret,
                PushToken = device.PushToken,
                ColdStorageZoneId = device.ColdStorageZoneId,
                IsActive = true,
                EnrolledAt = device.EnrolledAt
            };
            await _devices.UpdateAsync(updated);
        }

        // ── 3. Responder ──────────────────────────────────────────────────────
        return Ok(new
        {
            success = true,
            message = "Login exitoso",
            data = new
            {
                token = $"atu-token-{request.EmployeeNumber}-{Guid.NewGuid():N}",
                supervisorId = request.EmployeeNumber.Trim(),
                supervisorName = supervisorNombre,
                role = "SupervisorCamaras",
                expiresAt = DateTime.UtcNow.AddHours(8),
                requiresBiometricEnrollment = false,
                isDeviceEnrolled = true
            },
            errors = Array.Empty<string>()
        });
    }

    private static object Fail(string message) => new
    {
        success = false,
        message,
        data = (object?)null,
        errors = new[] { "INVALID_CREDENTIALS" }
    };
}
