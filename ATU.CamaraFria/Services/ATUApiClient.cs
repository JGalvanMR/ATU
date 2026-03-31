using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ATU.CamaraFria.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Refit;

namespace ATU.CamaraFria.Services;

public interface IATUApi
{
    [Post("/api/otp/generate")]
    Task<OTPResponse> GenerateOTP([Body] OTPRequest request);

    [Post("/api/otp/validate")]
    Task<ValidationResponse> ValidateOTP([Body] ValidateOTPRequest request);

    [Post("/api/auth/login")]
    Task<LoginResponse> Login([Body] LoginRequest request);

    [Post("/api/device/enroll")]
    Task<EnrollResponse> EnrollDevice([Body] EnrollDeviceRequest request);

    [Get("/api/device/status")]
    Task<DeviceStatusResponse> GetDeviceStatus([Header("X-Device-Fingerprint")] string fingerprint);

    [Get("/health")]
    Task<HealthResponse> HealthCheck();
}

public class ValidateOTPRequest
{
    public string Code { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string SupervisorId { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
}

public class ValidationResponse
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsAuthorized { get; set; }
}

public class EnrollDeviceRequest
{
    public string SupervisorId { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
}

public class EnrollResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DeviceSecret { get; set; }
}

public class DeviceStatusResponse
{
    public bool IsEnrolled { get; set; }
    public string? DeviceId { get; set; }
    public DateTime? EnrolledAt { get; set; }
    public bool IsActive { get; set; }
}

public class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class ATUApiClient
{
    private IATUApi _api;
    private readonly SyncQueueService _syncQueue;
    private readonly ILogger<ATUApiClient> _logger;
    private readonly string _deviceFingerprint;

    private const string BASE_URL_KEY = "ATU_BASE_URL";

    public ATUApiClient(
        SyncQueueService syncQueue,
        ILogger<ATUApiClient> logger,
        IDeviceFingerprintService fingerprintService)
    {
        _syncQueue = syncQueue;
        _logger = logger;
        _deviceFingerprint = fingerprintService.GetFingerprint();

        var baseUrl = Preferences.Get(BASE_URL_KEY, "http://192.168.123.155:5059");

        var settings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
        };

        _api = RestService.For<IATUApi>(baseUrl, settings);
    }

    public void SetBaseUrl(string url)
    {
        Preferences.Set(BASE_URL_KEY, url);

        var settings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
        };
        _api = RestService.For<IATUApi>(url, settings);
    }

    public async Task<OTPResponse?> GenerateOTPAsync(OTPRequest request)
    {
        request.DeviceFingerprint = _deviceFingerprint;

        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                _logger.LogWarning("Sin conectividad - guardando en cola de sincronización");
                await _syncQueue.EnqueueAsync(SyncType.OTPGeneration, request);
                return new OTPResponse
                {
                    Success = false,
                    Message = "Sin conexión. La solicitud se sincronizará cuando haya red.",
                    Errors = new List<string> { "OFFLINE_MODE" }
                };
            }

            var response = await _api.GenerateOTP(request);

            if (response.Success && response.Data != null)
            {
                await SaveLastOTPAsync(response.Data);
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error de conexión al generar OTP");
            await _syncQueue.EnqueueAsync(SyncType.OTPGeneration, request);

            return new OTPResponse
            {
                Success = false,
                Message = "Error de conexión. Se guardará para sincronizar.",
                Errors = new List<string> { ex.Message }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al generar OTP");
            return new OTPResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    public async Task<ValidationResponse?> ValidateOTPAsync(string code, string batchId, string supervisorId)
    {
        try
        {
            var request = new ValidateOTPRequest
            {
                Code = code,
                BatchId = batchId,
                SupervisorId = supervisorId,
                DeviceFingerprint = _deviceFingerprint
            };

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                return new ValidationResponse
                {
                    Success = false,
                    Status = "Red",
                    Message = "Sin conexión. No se puede validar el OTP.",
                    IsAuthorized = false
                };
            }

            return await _api.ValidateOTP(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar OTP");
            return new ValidationResponse
            {
                Success = false,
                Status = "Red",
                Message = $"Error de validación: {ex.Message}",
                IsAuthorized = false
            };
        }
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        request.DeviceFingerprint = _deviceFingerprint;

        try
        {
            return await _api.Login(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en login");
            return new LoginResponse
            {
                Success = false,
                Message = $"Error de conexión: {ex.Message}"
            };
        }
    }

    public async Task<bool> IsServerAvailableAsync()
    {
        try
        {
            var response = await _api.HealthCheck();
            return response?.Status == "Healthy";
        }
        catch
        {
            return false;
        }
    }

    private async Task SaveLastOTPAsync(OTPData otp)
    {
        var json = JsonSerializer.Serialize(otp);
        Preferences.Set("LAST_OTP", json);
        Preferences.Set("LAST_OTP_TIME", otp.GeneratedAt.ToBinary());
    }

    public OTPData? GetLastOTP()
    {
        var json = Preferences.Get("LAST_OTP", null);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<OTPData>(json);
        }
        catch
        {
            return null;
        }
    }
}

public interface IDeviceFingerprintService
{
    string GetFingerprint();
}