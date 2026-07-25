using ATU.CamaraFria.Models;
using ATU.CamaraFria.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using System.Data;

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
    [ObservableProperty] private string _reciboCap = string.Empty;
    [ObservableProperty] private string _prodClave = string.Empty;
    [ObservableProperty] private string _tarimaCap = string.Empty;

    // ── Panel 3: OTP ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _otpCode = string.Empty;
    [ObservableProperty] private string _countdownText = "00:30";
    [ObservableProperty] private int _countdownSeconds = 30;

    [ObservableProperty] private bool _showAutorizarRapido;
    private readonly string _deviceFingerprint;

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
        string embFolio,
        string reciboCap,
        string prodClave,
        string tarimaCap,
        string reciboSug,
        string tarimaSug,
        string producto = "",
        string responsable = "",
        string motivo = "")
    {
        EmbFolio = embFolio;
        ReciboCap = reciboCap;
        ProdClave = prodClave;
        TarimaCap = tarimaCap;
        BatchId = $"{reciboCap.TrimStart('0')}-{prodClave.Trim()}-{tarimaCap.TrimStart('0')}".ToUpper();
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

        if (string.IsNullOrEmpty(raw)) return;

        IsProcessing = true;
        try
        {
            // 1. Intentamos parsear para ver si es una etiqueta verde válida
            var resultado = _scanner.Parse(raw);
            string batchEscaneado;

            // 2. Resolvemos el ID para comparar (Usa la DLL interna)
            if (resultado != null && resultado.EsCompleto)
            {
                // Si la DLL lo reconoció, lo armamos en el orden correcto
                batchEscaneado = $"{resultado.Recibo}-{resultado.ProdClave}-{resultado.Tarima}";
            }
            else
            {
                // Si la DLL NO lo reconoció (como te pasó ahorita)
                // intentamos un split visual básico o mostramos el raw
                batchEscaneado = raw;
            }

            // 3. Validamos con el orden Recibo-Producto-Tarima
            bool coincide = VerificarCoincidencia(BatchId, raw);

            if (!coincide)
            {
                // Aquí es donde mostramos el error con el formato que quieres
                string textoAMostrar = FormatearLoteLeido(raw);

                InstruccionConfirmar = "❌ PALLET INCORRECTO\n\n" +
                                      $"Esperado: {BatchId}\n" +
                                      $"Leído: {textoAMostrar}\n\n" +
                                      "La etiqueta no coincide con el lote solicitado.";

                PalletConfirmadoTexto = string.Empty;
                SolicitarFocoConfirmar?.Invoke();
                return;
            }

            // Si coincide, procedemos...
            PalletConfirmado = true;
            PalletConfirmadoTexto = $"✓ Verificado: {batchEscaneado}";
            await GenerarOTPFolioAsync();
        }
        catch (Exception ex)
        {
            InstruccionConfirmar = $"Error: {ex.Message}";
        }
        finally { IsProcessing = false; }
    }
    private string FormatearLoteLeido(string codigoRaw)
    {
        var resultado = _scanner.Parse(codigoRaw);
        if (resultado?.EsCompleto == true)
            return resultado.BatchId;

        if (!string.IsNullOrEmpty(BatchId) && BatchId.Contains('-'))
        {
            var partes = BatchId.Split('-');
            if (partes.Length == 3)
            {
                string prod = partes[1].Trim();
                string rawLimpio = codigoRaw.Replace("-", "").ToUpperInvariant();

                if (rawLimpio.Contains(prod))
                {
                    int idx = rawLimpio.IndexOf(prod, StringComparison.Ordinal);
                    string rec = rawLimpio.Substring(0, idx).TrimStart('0');

                    // ✅ APLICAR LIMPIADOR A LA TARIMA PARA MOSTRARLA
                    string tarimaRaw = rawLimpio.Substring(idx + prod.Length);
                    string tar = LimpiarTarimaEscaneada(tarimaRaw);

                    return $"{rec}-{prod}-{tar}";
                }
            }
        }

        return codigoRaw;
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

    [RelayCommand]
    private async Task AutorizarRapidoAsync()
    {
        if (string.IsNullOrEmpty(EmbFolio))
        {
            await Application.Current.MainPage.DisplayAlert("Error", "No hay folio seleccionado", "OK");
            return;
        }

        var confirm = await Application.Current.MainPage.DisplayAlert(
            "Confirmar autorización",
            $"¿Autorizar el folio {EmbFolio} sin verificación física?\n\n" +
            "El operador de CargaEmbarques podrá surtir el producto de inmediato.",
            "Sí, autorizar", "Cancelar");

        if (!confirm) return;

        IsProcessing = true;
        try
        {
            var response = await _apiClient.AuthorizeFolioAsync(new AuthorizeFolioRequest
            {
                SupervisorId = _supervisorId,
                EmbFolio = EmbFolio,
                BatchId = BatchId,
                DeviceFingerprint = _deviceFingerprint,
                Comments = "Autorización remota por ausencia física - Aprobación rápida"
            });

            if (response?.Success == true)
            {
                await Application.Current.MainPage.DisplayAlert("✅ Éxito",
                    $"Folio {EmbFolio} autorizado correctamente.", "OK");
                await Shell.Current.GoToAsync(".."); // Volver a la lista de solicitudes
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Error",
                    response?.Message ?? "No se pudo autorizar", "OK");
            }
        }
        catch (Exception ex)
        {
            await Application.Current.MainPage.DisplayAlert("Error",
                $"Error inesperado: {ex.Message}", "OK");
        }
        finally
        {
            IsProcessing = false;
        }
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
            var resp = await _apiClient.GenerateOTPForFolioAsync(EmbFolio, ReciboCap, ProdClave, TarimaCap, _supervisorId, BatchId);
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
        if (HapticFeedback.Default.IsSupported)
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
    private bool VerificarCoincidencia(string esperado, string escaneado)
    {
        string normEsperado = esperado.Replace("-", "").TrimStart('0').ToUpperInvariant();
        string normEscaneado = escaneado.Replace("-", "").TrimStart('0').ToUpperInvariant();

        if (normEsperado == normEscaneado)
            return true;

        var partes = esperado.Split('-');
        if (partes.Length == 3)
        {
            string productoEsperado = partes[1].Trim();

            if (normEscaneado.Contains(productoEsperado))
            {
                int idxProducto = normEscaneado.IndexOf(productoEsperado, StringComparison.Ordinal);

                string reciboEscaneado = normEscaneado.Substring(0, idxProducto).TrimStart('0');

                // ✅ APLICAR LIMPIADOR A LA TARIMA ESCANEADA
                string tarimaRawEscaneada = normEscaneado.Substring(idxProducto + productoEsperado.Length);
                string tarimaEscaneada = LimpiarTarimaEscaneada(tarimaRawEscaneada);

                bool reciboOk = reciboEscaneado == partes[0].TrimStart('0');
                bool tarimaOk = tarimaEscaneada == partes[2].TrimStart('0');

                return reciboOk && tarimaOk;
            }
        }

        return false;
    }

    private async Task<string> ResolverBatchIdAsync(string raw, EtiquetaParseResult resultado)
    {
        // Si el resultado ya tiene el BatchId (porque el primer intento fue exitoso)
        if (resultado != null && !string.IsNullOrEmpty(resultado.BatchId))
        {
            return resultado.BatchId;
        }

        // Si no, forzamos un segundo intento de Parseo usando la DLL 
        // (Esto ayuda si el primer intento fue superficial)
        var segundoIntento = _scanner.Parse(raw);

        if (segundoIntento != null && !string.IsNullOrEmpty(segundoIntento.BatchId))
        {
            return segundoIntento.BatchId;
        }

        // Si de plano la etiqueta es ilegible para la DLL, regresamos el raw
        // pero habiendo esperado un ciclo de CPU para no bloquear la UI
        await Task.Yield();
        return raw;
    }

    public async Task OnAppearing()
    {
        // Solo cargamos si el catálogo está vacío para no gastar datos/tiempo de más
        if (_scanner.CatalogoActual == null || _scanner.CatalogoActual.Rows.Count == 0)
        {
            await SincronizarCatalogoAsync();
        }
    }
    private async Task SincronizarCatalogoAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = "Sincronizando catálogo de productos...";

            // Bajamos el DataTable desde el API Client que corregimos antes
            DataTable dt = await _apiClient.GetCatalogoDllAsync();

            // Inyectamos el catálogo en el servicio del scanner (donde está la DLL)
            _scanner.CatalogoActual = dt;

            if (dt.Rows.Count > 0)
            {
                StatusMessage = "Scanner listo (Modo Inteligente)";
                StatusColor = "#8AA0BC";
            }
            else
            {
                StatusMessage = "Scanner listo (Modo Básico)";
                StatusColor = "#FFAA00";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Error de sincronización";
            StatusColor = "#FF4444";
        }
        finally
        {
            IsProcessing = false;
            SolicitarFocoEntrada?.Invoke();
        }
    }

    // --- Agrega este método dentro de OTPViewModel ---
    private static string LimpiarTarimaEscaneada(string tarimaRaw)
    {
        if (string.IsNullOrWhiteSpace(tarimaRaw)) return tarimaRaw;

        string tarima = tarimaRaw.Trim();
        int longitud = tarima.Length;

        if (longitud == 3)
            return tarima.TrimStart('0');
        else if (longitud == 4)
            return tarima.Substring(0, 2).TrimStart('0');
        else if (longitud == 6)
            return tarima.Substring(0, 3).TrimStart('0');

        return tarima.TrimStart('0');
    }
}
