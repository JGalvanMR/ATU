using System;
using System.Collections.Generic;

namespace ATU.CamaraFria.Models;

/// <summary>
/// Respuesta del backend al generar OTP
/// </summary>
public class OTPResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public OTPData? Data { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class OTPData
{
    /// <summary>
    /// Código OTP de 6 dígitos
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp de generación (UTC)
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// Timestamp de expiración (UTC)
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Segundos restantes para expirar
    /// </summary>
    public int SecondsRemaining { get; set; }

    /// <summary>
    /// ID del lote vinculado
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// ID de la transacción para auditoría
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;
}