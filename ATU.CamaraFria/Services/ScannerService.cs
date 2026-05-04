using System.Text.RegularExpressions;
using ATU.CamaraFria.Models;

namespace ATU.CamaraFria.Services;

/// <summary>
/// Procesa los códigos leídos por los scanners Unitech PA768 y Honeywell ScanPal EDA-50.
/// Los scanners funcionan como teclado virtual: envían texto al Entry enfocado y simulan Enter.
/// Las reglas de parseo siguen exactamente la lógica de FragmentoCapturarPedido.cs.
/// </summary>
public class ScannerService : IScannerService
{
    // URLs internas de trazabilidad mrlucky
    private static readonly string[] UrlsTrazabilidad =
    {
        "http://www.mrlucky.com.mx/tr/trazabilidad2_dmi.php?id_codigo=",
        "HTTP://WWW.MRLUCKY.COM.MX/TR/TRAZABILIDAD2_DMI.PHP?ID_CODIGO=",
        "http://gab.mrlucky.com.mx/tr/trazabilidad2_dmi.php?id_codigo=",
        "HTTP://GAB.MRLUCKY.COM.MX/TR/TRAZABILIDAD2_DMI.PHP?ID_CODIGO="
    };

    // ── API pública ────────────────────────────────────────────────────────────

    public EtiquetaParseResult? Parse(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode)) return null;

        var code = rawCode.Trim();

        // Ignorar lecturas inválidas (longitud 10 = FAC/adicionales, no etiqueta verde)
        if (code.Length == 10 || code.Contains("FAC"))
            return null;

        // Ignorar URLs de trazabilidad (se procesan aparte si fuera necesario)
        if (EsUrlTrazabilidad(code))
            return ParseDesdeUrl(code);

        // Ignorar SPLITs
        if (code.Contains("SPLIT*"))
            return null;

        // Mínimo de longitud para ser etiqueta verde
        if (code.Length <= 10)
            return null;

        return ProcesarEtiquetaVerde(code);
    }

    public bool EsCodigoValido(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var c = code.Trim();
        if (c.Length <= 10) return false;
        if (c.Contains("SPLIT*")) return false;
        return true;
    }

    public string FormatearBatchId(string prodClave, string recibo, string tarima)
        => $"{prodClave.Trim()}-{recibo.TrimStart('0').Trim()}-{tarima.TrimStart('0').Trim()}";

    // ── Lógica de parseo — espejo de FragmentoCapturarPedido.cs ───────────────

    private EtiquetaParseResult? ProcesarEtiquetaVerde(string code)
    {
        string vRecibo = "", vPrd = "", mtar = "", mtipo = "";

        // Paso 1: intentar con la función ProcesarEtiqueta equivalente
        var info = TryProcesarEtiqueta(code);
        if (info != null)
        {
            vRecibo = info.Recibo;
            vPrd = info.ProdClave;
            mtar = info.Tarima;
            mtipo = info.Tipo;
        }

        bool incompleto = string.IsNullOrEmpty(vRecibo) || string.IsNullOrEmpty(mtar)
                       || string.IsNullOrEmpty(vPrd) || string.IsNullOrEmpty(mtipo);

        // PTI Famous: exactamente 12 dígitos
        if (incompleto && code.Length == 12)
        {
            var ptiFamous = code.StartsWith("0") ? code.TrimStart('0') : code;
            // En ATU no tenemos DB local, marcamos para lookup remoto
            return new EtiquetaParseResult
            {
                ProdClave = vPrd,
                Recibo = vRecibo,
                Tarima = mtar,
                Tipo = mtipo,
                CodigoRaw = code,
                RequiereLookup = true,
                LookupTipo = "PTI_FAMOUS",
                LookupValor = ptiFamous
            };
        }

        // SSCC: contiene el prefijo de contenedor de envío
        if (incompleto && (code.Contains("00") && code.Length >= 18))
        {
            return new EtiquetaParseResult
            {
                CodigoRaw = code,
                RequiereLookup = true,
                LookupTipo = "SSCC",
                LookupValor = code
            };
        }

        // PTI Clave: sin espacios
        if (incompleto && !code.Contains(" "))
        {
            var resultado = TryValidarEtiquetaNueva(code);
            if (resultado != null)
            {
                vRecibo = resultado.Recibo;
                vPrd = resultado.ProdClave;
                mtar = resultado.Tarima;
                mtipo = resultado.Tipo;
                incompleto = false;
            }
            else
            {
                // Lookup por pti_clave en DB
                return new EtiquetaParseResult
                {
                    CodigoRaw = code,
                    RequiereLookup = true,
                    LookupTipo = "PTI_CLAVE",
                    LookupValor = code
                };
            }
        }

        // Etiqueta con espacios (formato anterior)
        if (incompleto && code.Contains(" "))
        {
            if (code.Length < 18)
            {
                mtar = code.Substring(code.Length - 3, 3);
                vRecibo = code.Substring(0, 5);
                vPrd = code.Replace(vRecibo, "").Replace(mtar, "").Trim();
                mtar = mtar.Replace(" ", "0");
                mtipo = "PTC";
            }
            else
            {
                mtar = code.Substring(code.Length - 3, 3);
                vRecibo = code.Substring(0, 6);
                vPrd = code.Replace(vRecibo, "").Replace(mtar, "").Trim();
                mtar = mtar.Replace(" ", "0");
                mtipo = "PTP";
                if (vRecibo.StartsWith("0"))
                {
                    mtipo = "PTC";
                    vRecibo = int.Parse(vRecibo).ToString();
                }
            }
            incompleto = false;
        }

        // Formato por descarte (sin espacios, longitud variable)
        if (incompleto)
        {
            var tam = code.Length;
            mtar = code.Substring(tam - 3, 3);
            vRecibo = code.Substring(0, 6);
            mtipo = "PTP";
            if (vRecibo.StartsWith("0"))
            {
                mtipo = "PTC";
                vRecibo = int.Parse(vRecibo).ToString();
            }
            int lCad = tam - 9;
            vPrd = lCad > 0 ? code.Substring(6, lCad) : string.Empty;
        }

        // Normalizar
        vRecibo = vRecibo.TrimStart('0').Trim();
        mtar = mtar.TrimStart('0').Trim();

        if (string.IsNullOrEmpty(vRecibo) || string.IsNullOrEmpty(vPrd))
            return null;

        return new EtiquetaParseResult
        {
            ProdClave = vPrd.Trim(),
            Recibo = vRecibo,
            Tarima = mtar,
            Tipo = mtipo,
            CodigoRaw = code,
            RequiereLookup = false
        };
    }

    /// <summary>
    /// Equivalente a ProcesarEtiqueta() del original.
    /// Detecta el formato estándar interno de la empresa.
    /// </summary>
    private static EtiquetaInfo? TryProcesarEtiqueta(string code)
    {
        // Formato conocido: URL de trazabilidad con id_codigo
        if (EsUrlTrazabilidad(code))
            return null; // ya se maneja antes

        // Intentar extraer con regex el formato más común:
        // [Recibo 4-6 chars][ProdClave N chars][Tarima 3 chars]
        // Sin espacio, longitud típica 13-20 chars
        if (!code.Contains(" ") && code.Length >= 13)
        {
            // Si empieza con 0 → PTC, recibo sin ceros
            string tipo = code.StartsWith("0") ? "PTC" : "PTP";
            string recibo = code.Substring(0, 6);
            if (recibo.StartsWith("0") && int.TryParse(recibo, out int reciboNum))
            {
                recibo = reciboNum.ToString();
                tipo = "PTC";
            }

            string tarima = code.Substring(code.Length - 3, 3);
            int longPrd = code.Length - 6 - 3;
            if (longPrd > 0)
            {
                string prod = code.Substring(6, longPrd);
                return new EtiquetaInfo
                {
                    Recibo = recibo.TrimStart('0'),
                    ProdClave = prod.Trim(),
                    Tarima = tarima.TrimStart('0'),
                    Tipo = tipo
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Equivalente a ValidarEtiquetaVerde() del original.
    /// Detecta el formato nuevo de etiqueta.
    /// </summary>
    private static EtiquetaInfo? TryValidarEtiquetaNueva(string code)
    {
        // Formato nuevo: puede contener guiones o estructura diferente
        // Ejemplo: "12345PROD001001" o "12345-PROD-001"
        if (code.Contains("-"))
        {
            var partes = code.Split('-');
            if (partes.Length >= 3)
            {
                return new EtiquetaInfo
                {
                    Recibo = partes[0].TrimStart('0'),
                    ProdClave = partes[1].Trim(),
                    Tarima = partes[partes.Length - 1].TrimStart('0'),
                    Tipo = "PTC"
                };
            }
        }
        return null;
    }

    private static EtiquetaParseResult? ParseDesdeUrl(string code)
    {
        // Extraer id_codigo de la URL y marcarlo para lookup
        foreach (var url in UrlsTrazabilidad)
        {
            if (code.Contains(url, StringComparison.OrdinalIgnoreCase))
            {
                var idCodigo = code.Substring(code.IndexOf("id_codigo=", StringComparison.OrdinalIgnoreCase) + 10);
                return new EtiquetaParseResult
                {
                    CodigoRaw = code,
                    RequiereLookup = true,
                    LookupTipo = "URL_TRAZABILIDAD",
                    LookupValor = idCodigo
                };
            }
        }
        return null;
    }

    private static bool EsUrlTrazabilidad(string code)
        => UrlsTrazabilidad.Any(u => code.Contains(u, StringComparison.OrdinalIgnoreCase));

    private class EtiquetaInfo
    {
        public string Recibo { get; set; } = string.Empty;
        public string ProdClave { get; set; } = string.Empty;
        public string Tarima { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
    }
}

// ── Resultado del parseo ──────────────────────────────────────────────────────

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

    public string BatchId => $"{ProdClave.Trim()}-{Recibo.TrimStart('0').Trim()}-{Tarima.TrimStart('0').Trim()}";
    public bool EsCompleto => !string.IsNullOrEmpty(ProdClave) && !string.IsNullOrEmpty(Recibo) && !RequiereLookup;
}

// ── Interfaz ──────────────────────────────────────────────────────────────────

public interface IScannerService
{
    EtiquetaParseResult? Parse(string rawCode);
    bool EsCodigoValido(string code);
    string FormatearBatchId(string prodClave, string recibo, string tarima);
}
