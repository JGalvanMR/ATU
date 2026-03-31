using Microsoft.AspNetCore.Mvc;

namespace ATU.AuthService.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Login simulado para probar conectividad con la app Android
    /// </summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginMobileRequest request)
    {
        // ID de prueba que configuramos en el InMemoryDeviceRepository
        if (request.EmployeeNumber == "12345")
        {
            return Ok(new
            {
                success = true,
                message = "Login exitoso",
                data = new
                {
                    token = "mock-jwt-token-camaras-frias",
                    supervisorId = "12345",
                    supervisorName = "Supervisor de Prueba",
                    role = "SupervisorCamaras",
                    expiresAt = DateTime.UtcNow.AddHours(8),
                    requiresBiometricEnrollment = false,
                    isDeviceEnrolled = true
                },
                errors = new List<string>()
            });
        }

        return Ok(new
        {
            success = false,
            message = "Empleado no encontrado. Use '12345' para prueba.",
            data = (object?)null,
            errors = new List<string> { "INVALID_CREDENTIALS" }
        });
    }
}

/// <summary>
/// DTO que espera la App Android
/// </summary>
public class LoginMobileRequest
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}