using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ATU.Shared.Models
{
    public record GenerateOTPRequest(
    string OperatorId,           // IMEI del dispositivo
    string SupervisorId,         // ID del supervisor de cámaras
    string ProductoClave,
    string Recibo,
    string Tarima,
    string FechaCaducidad,       // DD/MM/YYYY
    double? Latitude,
    double? Longitude);

    public record ValidateOTPRequest(
        string Otp,
        string OperatorId,           // Quien generó (cámaras)
        string SupervisorId,           // Quien valida (embarques)
        string ProductoClave,        // Producto REAL escaneado
        string Recibo,               // Recibo REAL escaneado
        string Tarima,               // Tarima REAL escaneada
        string FechaCaducidad,
        string DeviceHwId,
        string UserAgent,
        string Platform,
        double? Latitude,
        double? Longitude);

    // === RESPONSES ===

    public record GenerateOTPResponse(
        string Otp,
        DateTimeOffset ExpiresAt,
        string ProductoClave,
        string Recibo,
        string Tarima,
        string FechaCaducidad);

    public record ValidateOTPResponse(
        string Status,               // "Green", "Yellow", "Red"
        string Message,
        bool IsAuthorized,
        string? ExpectedProducto,    // Producto del OTP
        string? ExpectedRecibo,
        string? ExpectedTarima);

    // === OFFLINE QUEUE ===

    public record PendingOTPValidation(
        string Otp,
        string ProductoClave,
        string Recibo,
        string Tarima,
        string FechaCaducidad,
        string OperatorId,           // Cámaras
        string SupervisorId,         // Embarques
        DateTimeOffset GeneratedAt,
        DateTimeOffset ValidationRequestedAt,
        bool IsOfflineValidation,
        string? GeofenceZoneId = null);
}
