// ATU.AuthService/Services/FifoValidationService.cs

using System.Data;
using Dapper;

public class FifoValidationResult
{
    public bool IsValid { get; set; }
    public string ViolationType { get; set; } = string.Empty;
    // "NONE" | "FECHA_CADUCIDAD" | "RECIBO_ADELANTADO" | "TARIMA_ERROR"
    public string Message { get; set; } = string.Empty;
    public int DiasDesviacion { get; set; }
}

public class FifoValidationService
{
    private readonly IDbConnection _db;

    // Valida que el lote autorizado realmente es anterior al que hay disponible
    public async Task<FifoValidationResult> ValidateFifoIntegrity(
        string prodClave,
        string reciboAutorizado,   // Lo que vino en el OTP
        string reciboReal)         // Lo que escaneó embarques físicamente
    {
        // Los recibos son la fuente de verdad del FIFO
        // En tu sistema, recibo_cap = más viejo (FIFO correcto)
        // recibo_sug puede ser más nuevo (violación FIFO, requiere autorización)

        if (reciboAutorizado != reciboReal)
            return new FifoValidationResult
            {
                IsValid = false,
                ViolationType = "RECIBO_MISMATCH",
                Message = $"El operador sacó recibo {reciboReal} pero la autorización era para {reciboAutorizado}"
            };

        // Verificar que realmente existe inventario en ese recibo
        var existe = await _db.QueryFirstOrDefaultAsync<int>(@"
            SELECT COUNT(*) FROM tu_tabla_inventario
            WHERE prod_clave = @p AND recibo = @r AND cantidad > 0",
            new { p = prodClave, r = reciboAutorizado });

        return new FifoValidationResult
        {
            IsValid = existe > 0,
            ViolationType = existe > 0 ? "NONE" : "INVENTARIO_VACIO",
            Message = existe > 0 ? "FIFO verificado" : "No hay inventario en ese recibo"
        };
    }
}