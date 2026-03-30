using System;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Maui.Biometric;
using Microsoft.Extensions.Logging;

namespace ATU.CamaraFria.Services;

public class BiometricService : IBiometricService
{
    private readonly IBiometric _biometric;
    private readonly ILogger<BiometricService> _logger;

    public BiometricService(IBiometric biometric, ILogger<BiometricService> logger)
    {
        _biometric = biometric;
        _logger = logger;
    }

    public async Task<BiometricCapability> GetCapabilitiesAsync()
    {
        return BiometricCapability.Available;
    }

    public async Task<BiometricResult> AuthenticateAsync(
        string title = "ATU Cámara Fría",
        string subtitle = "Autenticación requerida",
        string description = "Coloque su huella o cara para continuar")
    {
        try
        {
            var authRequest = new AuthenticationRequest
            {
                Title = title,
                Subtitle = subtitle,
                Description = description,
                NegativeText = "Cancelar"
            };

            var result = await _biometric.AuthenticateAsync(authRequest, CancellationToken.None);

            // Usar dynamic porque la v0.1.0 cambia los nombres de propiedades internas
            dynamic dynResult = result;
            bool success = false;
            string? errorMsg = null;

            try { success = dynResult.IsSuccess; } catch { }
            if (!success) try { success = dynResult.Authenticated; } catch { }

            try { errorMsg = dynResult.ErrorMessage; } catch { }
            if (string.IsNullOrEmpty(errorMsg)) try { errorMsg = dynResult.Error; } catch { }

            return new BiometricResult
            {
                Success = success,
                ErrorMessage = errorMsg,
                Status = success ? BiometricStatus.Success : BiometricStatus.Failed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en autenticación biométrica");
            return new BiometricResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Status = BiometricStatus.Error
            };
        }
    }

    public async Task<bool> QuickAuthenticateAsync()
    {
        var result = await AuthenticateAsync(
            "Verificación",
            "Confirme su identidad",
            "Toque el sensor para continuar"
        );
        return result.Success;
    }
}

public interface IBiometricService
{
    Task<BiometricCapability> GetCapabilitiesAsync();
    Task<BiometricResult> AuthenticateAsync(string title, string subtitle, string description);
    Task<bool> QuickAuthenticateAsync();
}

public enum BiometricCapability
{
    Available,
    NoPermission,
    NoHardware,
    NotEnrolled,
    Unknown
}

public class BiometricResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public BiometricStatus Status { get; set; }
}

public enum BiometricStatus
{
    Success,
    Failed,
    Error,
    Cancelled
}