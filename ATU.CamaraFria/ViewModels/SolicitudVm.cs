namespace ATU.CamaraFria.Models;

/// <summary>
/// Representa una solicitud de folio adelantado pendiente de autorizar.
/// Devuelta por GET /api/otp/solicitudes-pendientes
/// </summary>
public class SolicitudVm
{
    public string EmbFolio { get; set; } = string.Empty;
    public string ProdClave { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string ReciboSug { get; set; } = string.Empty;
    public string TarimaSug { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }

    // ── Propiedades de display (calculadas, no se serializan) ───────────────
    public string TituloFolio => $"Folio {EmbFolio}";
    public string InfoProducto => $"{ProdClave} — {Producto}";
    public string InfoPallet => $"Recibo: {ReciboSug}  ·  Tarima: {TarimaSug}";
    public string InfoSolicitante => $"Solicitó: {Responsable}";
    public string ColorBorde => "#00BFFF";

    public string TiempoTranscurrido
    {
        get
        {
            var diff = DateTime.Now - FechaCreacion;
            if (diff.TotalSeconds < 60) return "ahora";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min";
            return $"{(int)diff.TotalHours} h";
        }
    }
}

public class SolicitudesResponse
{
    public bool Success { get; set; }
    public List<SolicitudVm>? Data { get; set; }
}
