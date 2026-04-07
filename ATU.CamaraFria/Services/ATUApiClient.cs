using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ATU.CamaraFria.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Refit;

namespace ATU.CamaraFria.Services;

// ── Interfaz Refit ────────────────────────────────────────────────────────────

public interface IATUApi
{
    [Post("/api/otp/generate")]
    Task<OTPResponse> GenerateOTP([Body] OTPRequest request);

    [Post("/api/otp/generate-folio")]
    Task<OTPResponse> GenerateOTPForFolio([Body] GenerateOtpFolioRequest request);

    [Post("/api/otp/validate")]
    Task<ValidationResponse> ValidateOTP([Body] ValidateOTPRequest request);

    [Post("/api/otp/request")]
    Task<GenericResponse> CreateFolioRequest([Body] FolioAdelantatadoRequest request);

    [Post("/api/auth/login")]
    Task<LoginResponse> Login([Body] LoginRequest request);

    [Get("/health")]
    Task<HealthResponse> HealthCheck();
}

// ── DTOs adicionales ──────────────────────────────────────────────────────────

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

public class GenerateOtpFolioRequest
{
    public string EmbFolio { get; set; } = string.Empty;
    public string SupervisorId { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
}

public class FolioAdelantatadoRequest
{
    public string EmbFolio { get; set; } = string.Empty;
    public string ReciboCap { get; set; } = string.Empty;
    public string ReciboSug { get; set; } = string.Empty;
    public string FechaRecCap { get; set; } = string.Empty;
    public string FechaRecSug { get; set; } = string.Empty;
    public string ProdClave { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string Cantidad { get; set; } = string.Empty;
    public string TarimaCap { get; set; } = string.Empty;
    public string TarimaSug { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public string Imei { get; set; } = string.Empty;
}

public class GenericResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
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

public class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

// ── Cliente principal ─────────────────────────────────────────────────────────

public class ATUApiClient
{
    /// <summary>Segundos de vida del OTP — coincide con el backend.</summary>
    public const int OtpTtlSeconds = 30;

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
        _api = BuildApi(baseUrl);
    }

    public void SetBaseUrl(string url)
    {
        Preferences.Set(BASE_URL_KEY, url);
        _api = BuildApi(url);
    }

    private static IATUApi BuildApi(string baseUrl)
        => RestService.For<IATUApi>(baseUrl, new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        });

    // ── Generar OTP (flujo normal — etiqueta verde) ───────────────────────────

    public async Task<OTPResponse?> GenerateOTPAsync(OTPRequest request)
    {
        request.DeviceFingerprint = _deviceFingerprint;

        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
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
                await SaveLastOTPAsync(response.Data);

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

    // ── Generar OTP para folio adelantado ────────────────────────────────────

    public async Task<OTPResponse?> GenerateOTPForFolioAsync(string embFolio, string supervisorId)
    {
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return new OTPResponse
                {
                    Success = false,
                    Message = "Sin conexión. La generación de OTP requiere conexión.",
                    Errors = new List<string> { "OFFLINE_MODE" }
                };

            var request = new GenerateOtpFolioRequest
            {
                EmbFolio = embFolio,
                SupervisorId = supervisorId,
                DeviceFingerprint = _deviceFingerprint
            };

            return await _api.GenerateOTPForFolio(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al generar OTP para folio {Folio}", embFolio);
            return new OTPResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    // ── Validar OTP ───────────────────────────────────────────────────────────

    public async Task<ValidationResponse?> ValidateOTPAsync(
        string code, string batchId, string supervisorId)
    {
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return new ValidationResponse
                {
                    Success = false,
                    Status = "Red",
                    Message = "Sin conexión. No se puede validar el OTP.",
                    IsAuthorized = false
                };

            return await _api.ValidateOTP(new ValidateOTPRequest
            {
                Code = code,
                BatchId = batchId,
                SupervisorId = supervisorId,
                DeviceFingerprint = _deviceFingerprint
            });
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

    // ── Login ─────────────────────────────────────────────────────────────────

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

    // ── Health check ──────────────────────────────────────────────────────────

    public async Task<bool> IsServerAvailableAsync()
    {
        try
        {
            var response = await _api.HealthCheck();
            return response?.Status?.Equals("Healthy", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch { return false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task SaveLastOTPAsync(OTPData otp)
    {
        var json = JsonSerializer.Serialize(otp);
        Preferences.Set("LAST_OTP", json);
        Preferences.Set("LAST_OTP_TIME", otp.GeneratedAt.ToBinary());
        await Task.CompletedTask;
    }

    public OTPData? GetLastOTP()
    {
        var json = Preferences.Get("LAST_OTP", null);
        if (json == null) return null;
        try { return JsonSerializer.Deserialize<OTPData>(json); }
        catch { return null; }
    }
}

public interface IDeviceFingerprintService
{
    string GetFingerprint();
}
