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

    public AuthController(IDeviceRepository d, IEncryptionService e, IConfiguration cfg)
    {
        _devices = d;
        _encryption = e;
        _connStr = cfg.GetConnectionString("SqlServer") ?? string.Empty;
    }

    public class LoginMobileRequest
    {
        public string EmployeeNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DeviceFingerprint { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginMobileRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmployeeNumber))
            return Ok(Fail("Número de empleado requerido."));
        if (string.IsNullOrWhiteSpace(req.Password))
            return Ok(Fail("Contraseña requerida."));
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(Fail("Cadena de conexión SQL Server no configurada en el servidor ATU."));

        try
        {
            await using var conn = new SqlConnection(_connStr);

            // Valida empleado + contraseña (igual que CargaEmbarques original)
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT usuario, obs
                FROM   Tb_Autoriza_OdeP
                WHERE  LTRIM(RTRIM(usuario)) = @emp
                  AND  LTRIM(RTRIM(clave))   = 'EM'
                  AND  (LTRIM(RTRIM(password))      = @pwd
                     OR LTRIM(RTRIM(passwordlineal)) = @pwd)",
                new { emp = req.EmployeeNumber.Trim(), pwd = req.Password.Trim().ToUpper() });

            if (row == null)
                return Ok(Fail("Credenciales incorrectas o sin permiso de autorización."));

            string nombre = ((string?)row.obs)?.Trim() ?? req.EmployeeNumber;

            // Auto-enrolamiento del dispositivo
            var device = await _devices.GetByIdAsync(req.EmployeeNumber.Trim());
            if (device == null)
            {
                var secret = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                await _devices.AddAsync(new EnrolledDevice
                {
                    OperatorId = req.EmployeeNumber.Trim(),
                    Fingerprint = req.DeviceFingerprint,
                    EncryptedSecret = _encryption.Encrypt(secret),
                    PushToken = string.Empty,
                    ColdStorageZoneId = string.Empty,
                    IsActive = true,
                    EnrolledAt = DateTimeOffset.Now
                });
            }
            else if (!string.IsNullOrEmpty(req.DeviceFingerprint)
                  && device.Fingerprint != req.DeviceFingerprint)
            {
                await _devices.UpdateAsync(new EnrolledDevice
                {
                    OperatorId = device.OperatorId,
                    Fingerprint = req.DeviceFingerprint,
                    EncryptedSecret = device.EncryptedSecret,
                    PushToken = device.PushToken,
                    ColdStorageZoneId = device.ColdStorageZoneId,
                    IsActive = true,
                    EnrolledAt = device.EnrolledAt
                });
            }

            return Ok(new
            {
                success = true,
                message = "Login exitoso",
                data = new
                {
                    token = $"atu-{req.EmployeeNumber}-{Guid.NewGuid():N}",
                    supervisorId = req.EmployeeNumber.Trim(),
                    supervisorName = nombre,
                    role = "SupervisorCamaras",
                    expiresAt = DateTime.Now.AddHours(8),
                    requiresBiometricEnrollment = false,
                    isDeviceEnrolled = true
                },
                errors = Array.Empty<string>()
            });
        }
        catch (Exception ex) { return Ok(Fail($"Error del servidor: {ex.Message}")); }
    }

    private static object Fail(string msg) => new
    {
        success = false,
        message = msg,
        data = (object?)null,
        errors = new[] { "INVALID_CREDENTIALS" }
    };
}
