namespace ATU.AuthService.Models;

// ── Datos leídos de tb_det_folio_adelantado ──────────────────────────────────

public class FolioRecord
{
    public string ProdClave { get; set; } = string.Empty;
    public string ReciboSug { get; set; } = string.Empty;
    public int TarimaSug { get; set; }
    public string FechaCaducidad { get; set; } = string.Empty;
    public string OtpStatus { get; set; } = string.Empty;   // PENDING / AUTHORIZED / FRAUD / EXPIRED
    public DateTime? OtpExpiresAt { get; set; }
    public string OtpGeneradoPor { get; set; } = string.Empty;
    public int OtpIntentos { get; set; }
    public string OtpDeviceFp { get; set; } = string.Empty;
}

// ── Request para crear una solicitud de folio adelantado ─────────────────────

public class AtuRequestRequest
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

// ── Requests para generar y validar OTP por folio ────────────────────────────

public class GenerateOtpFolioRequest
{
    public string EmbFolio { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string SupervisorId { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
}

public class ValidateOtpFolioRequest
{
    public string EmbFolio { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string SupervisorId { get; set; } = string.Empty;
    public string ActualProdClave { get; set; } = string.Empty;
    public string ActualRecibo { get; set; } = string.Empty;
    public int ActualTarima { get; set; }
}