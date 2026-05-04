using ATU.CamaraFria.Models;
using ATU.CamaraFria.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace ATU.CamaraFria.ViewModels;

/// <summary>
/// ViewModel para la pantalla de generación de OTP.
/// El scanner Unitech/Honeywell actúa como teclado: escribe en el Entry y pulsa Enter.
/// No se usa cámara — el Entry recibe el foco automáticamente.
/// </summary>
public partial class OTPViewModel : BaseViewModel
{
    private readonly ATUApiClient _apiClient;
    private readonly IScannerService _scanner;

    // ── Paneles (solo uno visible a la vez) ─────────────────────────────────
    [ObservableProperty] private bool _showScanNormal = true;
    [ObservableProperty] private bool _showConfirmarFolio;
    [ObservableProperty] private bool _showOTP;

    // ── Panel 1: Escaneo libre ───────────────────────────────────────────────
    [ObservableProperty] private string _codigoEscaneado = string.Empty;
    [ObservableProperty] private string _statusMessage = "Escanea la etiqueta verde del pallet";
    [ObservableProperty] private string _statusColor = "#8AA0BC";
    [ObservableProperty] private bool _isProcessing;

    // ── Panel 2: Confirmar pallet (folio adelantado) ─────────────────────────
    [ObservableProperty] private string _codigoPalletConfirmar = string.Empty;
    [ObservableProperty] private string _instruccionConfirmar = string.Empty;
    [ObservableProperty] private string _palletConfirmadoTexto = string.Empty;
    [ObservableProperty] private bool _palletConfirmado;

    // Datos del folio adelantado
    [ObservableProperty] private string _embFolio = string.Empty;
    [ObservableProperty] private string _batchId = string.Empty;
    [ObservableProperty] private string _infoProducto = string.Empty;
    [ObservableProperty] private string _infoSolicitante = string.Empty;
    [ObservableProperty] private string _motivoSolicitud = string.Empty;
    [ObservableProperty] private bool _esFolioAdelantado;

    // ── Panel 3: OTP ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _otpCode = string.Empty;
    [ObservableProperty] private string _countdownText = "00:30";
    [ObservableProperty] private int _countdownSeconds = 30;

    private string _supervisorId = string.Empty;
    private CancellationTokenSource? _cts;

    // Evento para pedir foco al Entry desde la Page
    public event Action? SolicitarFocoEntrada;
    public event Action? SolicitarFocoConfirmar;

    public OTPViewModel(ATUApiClient apiClient, IScannerService scanner)
    {
        _apiClient = apiClient;
        _scanner = scanner;
        _supervisorId = Preferences.Get("SUPERVISOR_ID", string.Empty);
    }

    // ── Llamado desde SolicitudesPage cuando el supervisor toca AUTORIZAR ─────
    public void CargarFolioAdelantado(
        string embFolio, string prodClave, string reciboSug, string tarimaSug,
        string producto = "", string responsable = "", string motivo = "")
    {
        EmbFolio = embFolio;
        BatchId = _scanner.FormatearBatchId(prodClave, reciboSug, tarimaSug);
        InfoProducto = $"{prodClave.Trim()} — {producto.Trim()}";
        InfoSolicitante = responsable.Trim();
        MotivoSolicitud = motivo.Trim();
        EsFolioAdelantado = true;
        PalletConfirmado = false;
        PalletConfirmadoTexto = string.Empty;
        CodigoPalletConfirmar = string.Empty;

        InstruccionConfirmar =
            $"Lote esperado: {BatchId}\n\n" +
            "Escanea el código del pallet físico para confirmar antes de generar el OTP.";

        MostrarPanel(Panel.ConfirmarFolio);
        SolicitarFocoConfirmar?.Invoke();
    }

    // ── Panel 1: El scanner escribe en CodigoEscaneado y pulsa Enter ──────────
    [RelayCommand]
    private async Task ProcesarCodigoEscaneadoAsync()
    {
        var raw = CodigoEscaneado.Trim();
        CodigoEscaneado = string.Empty; // limpiar inmediatamente para el próximo escaneo

        if (string.IsNullOrEmpty(raw)) return;
        if (!_scanner.EsCodigoValido(raw))
        {
            StatusMessage = "Etiqueta no reconocida — vuelve a escanear";
            StatusColor = "#FF4444";
            SolicitarFocoEntrada?.Invoke();
            return;
        }

        IsProcessing = true;
        StatusMessage = "Procesando...";
        StatusColor = "#FFAA00";

        try
        {
            var resultado = _scanner.Parse(raw);

            if (resultado == null)
            {
                StatusMessage = "No se pudo leer la etiqueta — intenta de nuevo";
                StatusColor = "#FF4444";
                return;
            }

            if (resultado.RequiereLookup)
            {
                // Necesita lookup en el servidor (PTI Famous, SSCC, URL)
                await GenerarOTPConLookupAsync(raw, resultado);
                return;
            }

            // Tenemos todos los datos localmente
            BatchId = resultado.BatchId;
            EsFolioAdelantado = false;
            EmbFolio = string.Empty;

            StatusMessage = $"✓ Lote: {resultado.Recibo}  ·  Producto: {resultado.ProdClave}  ·  Tarima: {resultado.Tarima}";
            StatusColor = "#44FF44";

            await GenerarOTPNormalAsync(resultado.BatchId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            StatusColor = "#FF4444";
        }
        finally
        {
            IsProcessing = false;
            SolicitarFocoEntrada?.Invoke();
        }
    }

    // ── Panel 2: El scanner escribe en CodigoPalletConfirmar y pulsa Enter ───
    [RelayCommand]
    private async Task ConfirmarPalletAsync()
    {
        var raw = CodigoPalletConfirmar.Trim();
        CodigoPalletConfirmar = string.Empty;

        if (string.IsNullOrEmpty(raw))
        {
            SolicitarFocoConfirmar?.Invoke();
            return;
        }

        IsProcessing = true;
        try
        {
            var resultado = _scanner.Parse(raw);
            string batchEscaneado;

            if (resultado != null && resultado.EsCompleto)
            {
                batchEscaneado = resultado.BatchId;
            }
            else if (resultado?.RequiereLookup == true)
            {
                // Para la confirmación, hacemos lookup en el backend
                batchEscaneado = await ResolverBatchIdAsync(raw, resultado);
            }
            else
            {
                // Usar el código raw como referencia de comparación
                batchEscaneado = raw;
            }

            // Comparación flexible (el batchId del folio puede tener formato diferente)
            bool coincide = VerificarCoincidencia(BatchId, batchEscaneado, raw);

            if (!coincide)
            {
                InstruccionConfirmar =
                    $"El pallet escaneado no coincide con la solicitud.\n\n" +
                    $"Esperado: {BatchId}\n" +
                    $"Escaneado: {batchEscaneado}\n\n" +
                    "Verifica que estés en el pallet correcto y vuelve a escanear.";
                PalletConfirmadoTexto = string.Empty;
                SolicitarFocoConfirmar?.Invoke();
                return;
            }

            PalletConfirmado = true;
            PalletConfirmadoTexto = $"✓ Pallet verificado: {batchEscaneado}";
            InstruccionConfirmar = "Pallet confirmado. Generando OTP...";

            await GenerarOTPFolioAsync();
        }
        catch (Exception ex)
        {
            InstruccionConfirmar = $"Error: {ex.Message}";
            SolicitarFocoConfirmar?.Invoke();
        }
        finally
        {
            IsProcessing = false;
        }
    }

    // ── Reset ─────────────────────────────────────────────────────────────────
    [RelayCommand]
    public void ResetScan()
    {
        _cts?.Cancel();
        CodigoEscaneado = string.Empty;
        CodigoPalletConfirmar = string.Empty;
        BatchId = string.Empty;
        EmbFolio = string.Empty;
        OtpCode = string.Empty;
        EsFolioAdelantado = false;
        PalletConfirmado = false;
        PalletConfirmadoTexto = string.Empty;
        CountdownSeconds = ATUApiClient.OtpTtlSeconds;
        CountdownText = $"00:{ATUApiClient.OtpTtlSeconds:D2}";
        StatusMessage = "Escanea la etiqueta verde del pallet";
        StatusColor = "#8AA0BC";
        MostrarPanel(Panel.ScanNormal);
        SolicitarFocoEntrada?.Invoke();
    }

    // ── Generación de OTP ────────────────────────────────────────────────────

    private async Task GenerarOTPNormalAsync(string batchId)
    {
        var resp = await _apiClient.GenerateOTPAsync(new OTPRequest
        {
            SupervisorId = _supervisorId,
            BatchId = batchId
        });

        if (resp?.Success == true && resp.Data != null)
            MostrarOTP(resp.Data.Code, resp.Data.SecondsRemaining);
        else
        {
            StatusMessage = resp?.Message ?? "Error al generar OTP";
            StatusColor = "#FF4444";
        }
    }

    private async Task GenerarOTPConLookupAsync(string raw, EtiquetaParseResult resultado)
    {
        // El lookup se hace en el backend enviando el código raw
        // El endpoint resolve-barcode devuelve prod_clave, recibo, tarima
        var resp = await _apiClient.GenerateOTPAsync(new OTPRequest
        {
            SupervisorId = _supervisorId,
            BatchId = raw  // el backend resuelve si es PTI Famous, SSCC, etc.
        });

        if (resp?.Success == true && resp.Data != null)
        {
            BatchId = resp.Data.BatchId;
            MostrarOTP(resp.Data.Code, resp.Data.SecondsRemaining);
        }
        else
        {
            StatusMessage = resp?.Message ?? "Error al generar OTP";
            StatusColor = "#FF4444";
        }
    }

    [RelayCommand]
    private async Task GenerarOTPFolioAsync()
    {
        IsProcessing = true;
        try
        {
            var resp = await _apiClient.GenerateOTPForFolioAsync(EmbFolio, _supervisorId);
            if (resp?.Success == true && resp.Data != null)
            {
                if (!string.IsNullOrEmpty(resp.Data.BatchId)) BatchId = resp.Data.BatchId;
                MostrarOTP(resp.Data.Code, resp.Data.SecondsRemaining);
            }
            else
            {
                InstruccionConfirmar = resp?.Message ?? "Error al generar OTP";
                PalletConfirmado = false;
                SolicitarFocoConfirmar?.Invoke();
            }
        }
        finally { IsProcessing = false; }
    }

    private void MostrarOTP(string code, int seconds)
    {
        OtpCode = code;
        CountdownSeconds = seconds > 0 ? seconds : ATUApiClient.OtpTtlSeconds;
        MostrarPanel(Panel.OTP);
        IniciarCountdown();
        if (HapticFeedback.IsSupported)
            HapticFeedback.Perform(HapticFeedbackType.LongPress);
    }

    private void IniciarCountdown()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            while (CountdownSeconds > 0)
            {
                try { await Task.Delay(1000, _cts.Token); }
                catch (OperationCanceledException) { return; }
                CountdownSeconds--;
                var s = Math.Max(0, CountdownSeconds);
                MainThread.BeginInvokeOnMainThread(() =>
                    CountdownText = $"00:{s:D2}");
            }
            MainThread.BeginInvokeOnMainThread(() => ResetScan());
        }, _cts.Token);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private enum Panel { ScanNormal, ConfirmarFolio, OTP }

    private void MostrarPanel(Panel p)
    {
        ShowScanNormal = p == Panel.ScanNormal;
        ShowConfirmarFolio = p == Panel.ConfirmarFolio;
        ShowOTP = p == Panel.OTP;
    }

    private static bool VerificarCoincidencia(string esperado, string escaneado, string raw)
    {
        // Comparación normalizada: quitar guiones, ceros al inicio, mayúsculas
        string Normalizar(string s) => s.Replace("-", "").TrimStart('0').ToUpperInvariant();

        var e = Normalizar(esperado);
        var s = Normalizar(escaneado);
        var r = Normalizar(raw);

        return e == s || e.Contains(s) || s.Contains(e) || e == r || e.Contains(r);
    }

    private async Task<string> ResolverBatchIdAsync(string raw, EtiquetaParseResult resultado)
    {
        // Por ahora devolver el código raw para comparación
        // En una versión futura el backend resuelve via /api/otp/resolve-barcode
        await Task.CompletedTask;
        return raw;
    }
}
