using System.Security.Cryptography;
using System.Text;

namespace ATU.Shared.Models;

/// <summary>
/// Registro de OTP en base de datos (para prevención de replay attacks)
/// </summary>
public class OtpRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Otp { get; set; } = string.Empty;  // El hash o el OTP mismo (encriptado)
    public string ProductoClave { get; set; } = string.Empty;
    public string Recibo { get; set; } = string.Empty;
    public string Tarima { get; set; } = string.Empty;
    public string BatchId => $"{ProductoClave}-{Recibo}-{Tarima}";
    public string SupervisorId { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;  // IMEI/Dispositivo
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
    public DateTimeOffset? UsedAt { get; set; }
    public string? UsedByDevice { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>
/// Evento de auditoría para el dashboard
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;  // OTP_GENERATED, OTP_VALIDATED, FRAUDE_ATTEMPT, etc.
    public string SupervisorId { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string Recibo { get; set; } = string.Empty;
    public string Tarima { get; set; } = string.Empty;
    public string FechaCaducidad { get; set; } = string.Empty;
    public bool IsAuthorized { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? GeofenceZoneId { get; set; }
    public bool RequiresImmediateAction { get; set; } = false;
}

/// <summary>
/// Interfaz para repositorio de OTP
/// </summary>
public interface IOtpRepository
{
    Task SaveAsync(OtpRecord record);
    Task<OtpRecord?> GetByBatchAndSupervisorAsync(string batchId, string supervisorId);
    Task UpdateAsync(OtpRecord record);
    Task<OtpRecord?> GetByOtpAsync(string otp);
}

/// <summary>
/// Interfaz para publicar eventos de auditoría
/// </summary>
public interface IAuditEventPublisher
{
    Task PublishAsync(AuditEvent evt);
}

/// <summary>
/// Interfaz para repositorio de dispositivos enrolados
/// </summary>
public interface IDeviceRepository
{
    Task<EnrolledDevice?> GetByIdAsync(string deviceId);
    Task<EnrolledDevice?> GetActiveByOperatorAsync(string operatorId);
    Task AddAsync(EnrolledDevice device);
    Task UpdateAsync(EnrolledDevice device);
}

/// <summary>
/// Resultado de validación de geofencing
/// </summary>


/// <summary>
/// Interfaz para servicio de geofencing
/// </summary>
public interface IGeofenceService
{
    Task<GeofenceResult> ValidateAsync(string zoneId, double latitude, double longitude);
}

// Agregar a RepositoryModels.cs

public interface IZoneRepository
{
    Task<LoadingZone?> GetByIdAsync(string zoneId);
}

public class LoadingZone
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GeoPoint[] BoundaryPoints { get; set; } = Array.Empty<GeoPoint>();
}

public record GeoPoint(double Latitude, double Longitude);

public class GeofenceResult
{
    public bool IsValid { get; set; }
    public double DistanceMeters { get; set; }
    public string Message { get; set; }

    public GeofenceResult(bool isValid, double distanceMeters, string message)
    {
        IsValid = isValid;
        DistanceMeters = distanceMeters;
        Message = message;
    }
}