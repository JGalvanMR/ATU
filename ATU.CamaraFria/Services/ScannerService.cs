using ATU.Trazabilidad.Core;
using System.Data;

namespace ATU.CamaraFria.Services;

public class ScannerService : IScannerService
{
    private readonly ValidadorEtiquetas _validadorDll = new(); // Mantenemos la DLL por si se necesita para otra cosa
    private DataTable? _catalogoActual;

    public DataTable? CatalogoActual
    {
        get => Volatile.Read(ref _catalogoActual);
        set => Volatile.Write(ref _catalogoActual, value);
    }

    public EtiquetaParseResult? Parse(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode)) return null;
        var code = rawCode.Trim();

        // Filtros rápidos
        if (code.Length <= 10 || code.Contains("FAC") || code.Contains("SPLIT*"))
            return null;

        // 1. ESTRATEGIA PRINCIPAL: El Ancla (Tu nueva lógica)
        if (CatalogoActual != null && CatalogoActual.Rows.Count > 0)
        {
            var resultadoAncla = ParsearPorAncla(code, CatalogoActual);
            if (resultadoAncla != null)
                return resultadoAncla;
        }

        // 2. ESTRATEGIA SECUNDARIA: Lookup forzoso si no hay catálogo
        // (PTI Famous de 12 dígitos puros)
        if (code.Length == 12 && System.Linq.Enumerable.All(code, char.IsDigit))
        {
            return new EtiquetaParseResult
            {
                CodigoRaw = code,
                RequiereLookup = true,
                LookupTipo = "PTI_FAMOUS",
                LookupValor = code.TrimStart('0')
            };
        }

        // Si no hay catálogo y no es PTI, no podemos procesarla
        return null;
    }

    /// <summary>
    /// Lógica del Ancla: Busca el producto dentro del texto y deduce Recibo/Tarima.
    /// Asume que el DataTable viene ordenado por LEN(prod_clave) DESC.
    /// </summary>
    private EtiquetaParseResult? ParsearPorAncla(string textoEtiqueta, DataTable catalogo)
    {
        foreach (DataRow row in catalogo.Rows)
        {
            string clave = row["prod_clave"].ToString().Trim();
            string tipo = row["prod_tipo"].ToString().Trim();

            if (textoEtiqueta.Contains(clave))
            {
                int indexProducto = textoEtiqueta.IndexOf(clave);

                string vRecibo = textoEtiqueta.Substring(0, indexProducto).TrimStart('0');

                // ✅ OBTENEMOS EL RAW Y LO LIMPIAMOS CON LA NUEVA REGLA
                string tarimaRaw = textoEtiqueta.Substring(indexProducto + clave.Length);
                string vTarima = ExtraerTarimaReal(tarimaRaw);

                if (string.IsNullOrWhiteSpace(vRecibo))
                    continue;

                return new EtiquetaParseResult
                {
                    ProdClave = clave,
                    Recibo = vRecibo,
                    Tarima = vTarima, // ✅ Ahora tendrá solo "14" en vez de "1405" o "014023"
                    Tipo = tipo,
                    CodigoRaw = textoEtiqueta,
                    RequiereLookup = false
                };
            }
        }
        return null;
    }

    // --- Agrega este método ---
    private static string ExtraerTarimaReal(string tarimaRaw)
    {
        if (string.IsNullOrWhiteSpace(tarimaRaw)) return tarimaRaw;

        string tarima = tarimaRaw.Trim();
        int longitud = tarima.Length;

        if (longitud == 3)
        {
            // Regla: 3 dígitos -> Son puros números de tarima
            return tarima.TrimStart('0');
        }
        else if (longitud == 4)
        {
            // Regla: 4 dígitos -> Los primeros 2 son tarima, los últimos 2 son total
            return tarima.Substring(0, 2).TrimStart('0');
        }
        else if (longitud == 6)
        {
            // Regla: 6 dígitos -> Los primeros 3 son tarima, los últimos 3 son total
            return tarima.Substring(0, 3).TrimStart('0');
        }

        // Fallback para cualquier otra cosa rara que escaneen
        return tarima.TrimStart('0');
    }

    public bool EsCodigoValido(string code) => !string.IsNullOrWhiteSpace(code) && code.Length > 10;
}

// ── Resultado del parseo ──────────────────────────────────────────────────

public class EtiquetaParseResult
{
    public string ProdClave { get; set; } = string.Empty;
    public string Recibo { get; set; } = string.Empty;
    public string Tarima { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string CodigoRaw { get; set; } = string.Empty;
    public bool RequiereLookup { get; set; }
    public string LookupTipo { get; set; } = string.Empty;
    public string LookupValor { get; set; } = string.Empty;

    // ✅ ORDEN CORRECTO: Recibo - Producto - Tarima
    public string BatchId =>
        $"{Recibo.TrimStart('0')}-{ProdClave.Trim()}-{Tarima.TrimStart('0')}".ToUpper();

    public bool EsCompleto => !string.IsNullOrEmpty(ProdClave)
                           && !string.IsNullOrEmpty(Recibo)
                           && !RequiereLookup;
}

// ── Interfaz ──────────────────────────────────────────────────────────────

public interface IScannerService
{
    DataTable? CatalogoActual { get; set; }
    EtiquetaParseResult? Parse(string rawCode);
    bool EsCodigoValido(string code);
}