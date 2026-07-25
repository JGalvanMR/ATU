using ATU.CamaraFria.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Org.Apache.Http.Client;
using Refit;
using System.Data;
using System.Net.Http;
using System.Text.Json;

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

    [Get("/api/otp/solicitudes-pendientes")]
    Task<SolicitudesResponse> GetSolicitudesPendientes();

    [Post("/api/auth/login")]
    Task<LoginResponse> Login([Body] LoginRequest request);

    [Get("/health")]
    Task<HealthResponse> HealthCheck();

    [Get("/api/scanner/catalogo")]
    Task<List<Dictionary<string, string>>> GetCatalogoDll();

    [Post("/api/otp/authorize-folio")]
    Task<AuthorizeFolioResponse> AuthorizeFolio([Body] AuthorizeFolioRequest request);
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class ValidateOTPRequest
{
    public string Code { get; set; } = string.Empty;
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
    public string ReciboCap { get; set; } = string.Empty;
    public string ProdClave { get; set; } = string.Empty;
    public string TarimaCap { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
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

public class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

// En ATUApiClient.cs, después de las otras clases, agregar:

public class AuthorizeFolioRequest
{
    public string SupervisorId { get; set; } = string.Empty;
    public string EmbFolio { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
}

public class AuthorizeFolioResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public DateTime AuthorizedAt { get; set; }
}

// ── Cliente principal ─────────────────────────────────────────────────────────

public class ATUApiClient
{
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
        //_api = BuildApi(Preferences.Get(BASE_URL_KEY, "http://192.168.123.155:5002"));
        //_api = BuildApi(Preferences.Get(BASE_URL_KEY, "http://atu-web.int.mrlucky.com:83/auth"));
        _api = BuildApi(Preferences.Get(BASE_URL_KEY, "http://192.168.123.244:83/auth"));
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

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        request.DeviceFingerprint = _deviceFingerprint;
        try { return await _api.Login(request); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en login");
            return new LoginResponse { Success = false, Message = $"Error de conexión: {ex.Message}" };
        }
    }

    // ── Generar OTP normal (etiqueta verde) ───────────────────────────────────

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
                    Message = "Sin conexión. La solicitud se guardará para sincronizar.",
                    Errors = new List<string> { "OFFLINE_MODE" }
                };
            }
            var response = await _api.GenerateOTP(request);
            if (response.Success && response.Data != null) await SaveLastOTPAsync(response.Data);
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error HTTP al generar OTP");
            await _syncQueue.EnqueueAsync(SyncType.OTPGeneration, request);
            return new OTPResponse { Success = false, Message = "Error de red.", Errors = new List<string> { ex.Message } };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al generar OTP");
            return new OTPResponse { Success = false, Message = ex.Message, Errors = new List<string> { ex.Message } };
        }
    }

    // ── Generar OTP para folio adelantado ────────────────────────────────────

    public async Task<OTPResponse?> GenerateOTPForFolioAsync(string embFolio, string reciboCap, string prodClave, string tarimaCap, string supervisorId, string batchId)
    {
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return new OTPResponse { Success = false, Message = "Sin conexión. El OTP requiere conexión.", Errors = new List<string> { "OFFLINE_MODE" } };

            return await _api.GenerateOTPForFolio(new GenerateOtpFolioRequest
            {
                EmbFolio = embFolio,
                BatchId = batchId,
                ReciboCap = reciboCap,
                ProdClave = prodClave,
                TarimaCap = tarimaCap,
                SupervisorId = supervisorId,
                DeviceFingerprint = _deviceFingerprint
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando OTP para folio {Folio}", embFolio);
            return new OTPResponse { Success = false, Message = $"Error: {ex.Message}", Errors = new List<string> { ex.Message } };
        }
    }

    // ── Obtener solicitudes pendientes ────────────────────────────────────────

    public async Task<List<SolicitudVm>> GetSolicitudesPendientesAsync()
    {
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return new List<SolicitudVm>();

            var response = await _api.GetSolicitudesPendientes();
            return response?.Data ?? new List<SolicitudVm>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener solicitudes pendientes");
            return new List<SolicitudVm>();
        }
    }

    // ── Validar OTP ───────────────────────────────────────────────────────────

    public async Task<ValidationResponse?> ValidateOTPAsync(string code, string batchId, string supervisorId)
    {
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return new ValidationResponse { Success = false, Status = "Red", Message = "Sin conexión.", IsAuthorized = false };

            return await _api.ValidateOTP(new ValidateOTPRequest
            {
                Code = code,
                SupervisorId = supervisorId,
                DeviceFingerprint = _deviceFingerprint
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar OTP");
            return new ValidationResponse { Success = false, Status = "Red", Message = ex.Message, IsAuthorized = false };
        }
    }

    // ── Health check ──────────────────────────────────────────────────────────

    public async Task<bool> IsServerAvailableAsync()
    {
        try
        {
            var r = await _api.HealthCheck();
            return r?.Status?.Equals("Healthy", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch { return false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task SaveLastOTPAsync(OTPData otp)
    {
        Preferences.Set("LAST_OTP", JsonSerializer.Serialize(otp));
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

    public async Task<DataTable> GetCatalogoDllAsync()
    {
        try
        {
            // 1. Llamada directa a la interfaz Refit
            var lista = await _api.GetCatalogoDll();

            // 2. Crear el DataTable con la estructura que la DLL de anclaje requiere
            DataTable dt = new DataTable();
            dt.Columns.Add("prod_clave", typeof(string));
            dt.Columns.Add("prod_tipo", typeof(string));

            if (lista != null)
            {
                foreach (var item in lista)
                {
                    // Extraemos los valores del diccionario (JSON)
                    // Usamos TryGetValue para evitar errores si el API cambia nombres
                    string clave = item.ContainsKey("prod_clave") ? item["prod_clave"] : "";
                    string tipo = item.ContainsKey("prod_tipo") ? item["prod_tipo"] : "PTP";

                    dt.Rows.Add(clave, tipo);
                }
            }

            _logger.LogInformation("Catálogo sincronizado: {Count} productos", dt.Rows.Count);
            return dt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener catálogo para la DLL");
            return new DataTable(); // Retorna tabla vacía para no romper el scanner
        }
    }

    public async Task<AuthorizeFolioResponse?> AuthorizeFolioAsync(AuthorizeFolioRequest request)
    {
        try
        {
            request.DeviceFingerprint = _deviceFingerprint;
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return new AuthorizeFolioResponse { Success = false, Message = "Sin conexión." };

            return await _api.AuthorizeFolio(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en autorización remota");
            return new AuthorizeFolioResponse { Success = false, Message = $"Error: {ex.Message}" };
        }
    }


}

public interface IDeviceFingerprintService
{
    string GetFingerprint();
}
