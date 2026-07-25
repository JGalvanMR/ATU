using System;

namespace ATU.CamaraFria.Models;

public class OTPRequest
{
    /// <summary>
    /// ID del supervisor que genera el OTP
    /// </summary>
    public string SupervisorId { get; set; } = string.Empty;

    /// <summary>
    /// ID del lote/producto a autorizar
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// Fingerprint del dispositivo enrolado
    /// </summary>
    public string DeviceFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Ubicación GPS actual (latitud)
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Ubicación GPS actual (longitud)
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Datos del escaneo de etiqueta verde
    /// </summary>
    public LabelScanData? LabelData { get; set; }
}

public class LabelScanData
{
    /// <summary>
    /// Código de barras/QR de la etiqueta verde
    /// </summary>
    public string RawCode { get; set; } = string.Empty;

    /// <summary>
    /// Formato detectado (QR_CODE, CODE_128, etc.)
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp del escaneo
    /// </summary>
    public DateTime ScannedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Lote extraído del código
    /// </summary>
    public string ExtractedBatchId { get; set; } = string.Empty;

    /// <summary>
    /// Producto extraído del código
    /// </summary>
    public string? ExtractedProduct { get; set; }
}