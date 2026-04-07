using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ATU.CamaraFria.Models;
using ATU.CamaraFria.Services;
using Microsoft.Extensions.Logging;
using ZXing.Net.Maui;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;

namespace ATU.CamaraFria.ViewModels;

public partial class OTPViewModel : BaseViewModel
{
    private readonly ATUApiClient _apiClient;
    private readonly IScannerService _scannerService;
    private readonly ILogger<OTPViewModel> _logger;

    // ── Propiedades observables ──────────────────────────────────────────────

    [ObservableProperty] private string _scannedCode = string.Empty;
    [ObservableProperty] private string _batchId = string.Empty;
    [ObservableProperty] private string? _productName;
    [ObservableProperty] private string _otpCode = string.Empty;
    [ObservableProperty] private int _countdownSeconds;
    [ObservableProperty] private string _countdownText = "00:30";
    [ObservableProperty] private string _statusMessage = "Escanea la etiqueta verde del producto";
    [ObservableProperty] private string _statusColor = "#FFFFFF";
    [ObservableProperty] private bool _showOTP;
    [ObservableProperty] private bool _showScan;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private string _supervisorId = string.Empty;

    // Campos para flujo de folio adelantado (desde CargaEmbarques)
    [ObservableProperty] private string _embFolio = string.Empty;
    [ObservableProperty] private bool _esFolioAdelantado;

    private CancellationTokenSource? _countdownCts;
    private LabelScanData? _lastScanData;

    // ── Constructor ──────────────────────────────────────────────────────────

    public OTPViewModel(
        ATUApiClient apiClient,
        IScannerService scannerService,
        ILogger<OTPViewModel> logger)
    {
        _apiClient = apiClient;
        _scannerService = scannerService;
        _logger = logger;

        _showScan = true;
        _showOTP = false;
        _countdownSeconds = ATUApiClient.OtpTtlSeconds;
        _supervisorId = Preferences.Get("SUPERVISOR_ID", string.Empty);
    }

    // ── Método público llamado desde la cámara (sin RelayCommand) ────────────

    public void OnBarcodeDetected(string code, BarcodeFormat format)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
            await ProcessCodeFromCameraAsync(code, format));
    }

    // ── Pre-cargar folio adelantado (llamado desde App cuando llega SignalR) ─

    public void CargarFolioAdelantado(string embFolio,string prodClave,string reciboSug,string tarimaSug)
    {
        EmbFolio = embFolio;
        EsFolioAdelantado = true;
        BatchId = $"{prodClave.Trim()}-{reciboSug.Trim()}-{tarimaSug.Trim()}";
        StatusMessage = $"📦 Folio {embFolio} — Lote: {BatchId}";
        StatusColor = "#00BFFF";
    }

    // ── RelayCommand: procesar código ingresado manualmente ──────────────────

    [RelayCommand]
    private async Task ProcessScannedCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(ScannedCode))
        {
            StatusMessage = "❌ Ingresa un código";
            StatusColor = "#FF4444";
            return;
        }

        await ProcessCodeFromCameraAsync(ScannedCode, BarcodeFormat.QrCode);
    }

    // ── Lógica compartida de procesamiento de código ─────────────────────────

    private async Task ProcessCodeFromCameraAsync(string code, BarcodeFormat format)
    {
        // ¿Es un QR de solicitud de folio adelantado? (viene de CargaEmbarques)
        if (code.StartsWith("{") && code.Contains("embFolio"))
        {
            await ProcesarQrFolioAdelantadoAsync(code);
            return;
        }

        // Flujo normal: etiqueta verde del pallet
        if (!_scannerService.IsValidLabelCode(code))
        {
            StatusMessage = "❌ Código no válido. Intenta de nuevo.";
            StatusColor = "#FF4444";
            return;
        }

        IsGenerating = true;
        StatusMessage = "⏳ Procesando etiqueta...";
        StatusColor = "#FFAA00";

        try
        {
            _lastScanData = _scannerService.ParseScannedCode(code, format);
            BatchId = _lastScanData.ExtractedBatchId;
            ProductName = _lastScanData.ExtractedProduct;
            EmbFolio = string.Empty;
            EsFolioAdelantado = false;

            StatusMessage = $"✅ Lote: {_scannerService.FormatBatchIdForDisplay(BatchId)}";
            StatusColor = "#44FF44";

            await GenerateOTPAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
            StatusColor = "#FF4444";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    // ── Procesar QR de solicitud de folio adelantado ─────────────────────────

    private async Task ProcesarQrFolioAdelantadoAsync(string jsonCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonCode);
            var root = doc.RootElement;

            EmbFolio = root.TryGetProperty("embFolio", out var f) ? f.GetString() ?? "" : "";
            var prod = root.TryGetProperty("prodClave", out var p) ? p.GetString() ?? "" : "";
            var rec = root.TryGetProperty("reciboSug", out var r) ? r.GetString() ?? "" : "";
            var tar = root.TryGetProperty("tarimaSug", out var t) ? t.GetString() ?? "" : "";

            CargarFolioAdelantado(EmbFolio, prod, rec, tar);
            await GenerateOTPForFolioAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ QR inválido: {ex.Message}";
            StatusColor = "#FF4444";
        }
    }

    // ── Generar OTP para flujo normal (etiqueta verde) ───────────────────────

    [RelayCommand]
    private async Task GenerateOTPAsync()
    {
        if (string.IsNullOrEmpty(BatchId))
        {
            StatusMessage = "❌ Escanea un lote primero";
            StatusColor = "#FF4444";
            return;
        }

        if (string.IsNullOrEmpty(SupervisorId))
        {
            StatusMessage = "❌ Sesión no iniciada";
            StatusColor = "#FF4444";
            return;
        }

        IsGenerating = true;
        StatusMessage = "⏳ Generando código OTP...";
        StatusColor = "#FFAA00";

        try
        {
            var request = new OTPRequest
            {
                SupervisorId = SupervisorId,
                BatchId = BatchId,
                LabelData = _lastScanData
            };

            var response = await _apiClient.GenerateOTPAsync(request);

            if (response?.Success == true && response.Data != null)
            {
                MostrarOTP(response.Data.Code, response.Data.SecondsRemaining);
            }
            else
            {
                var errorMsg = response?.Message ?? "Error desconocido";
                StatusMessage = response?.Errors?.Contains("OFFLINE_MODE") == true
                    ? "📵 Sin conexión — Solicitud guardada para sincronizar"
                    : $"❌ {errorMsg}";
                StatusColor = "#FF4444";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando OTP");
            StatusMessage = $"❌ Error: {ex.Message}";
            StatusColor = "#FF4444";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    // ── Generar OTP para folio adelantado (usa endpoint /generate-folio) ─────

    [RelayCommand]
    private async Task GenerateOTPForFolioAsync()
    {
        if (string.IsNullOrEmpty(EmbFolio))
        {
            StatusMessage = "❌ No hay folio adelantado cargado";
            StatusColor = "#FF4444";
            return;
        }

        if (string.IsNullOrEmpty(SupervisorId))
        {
            StatusMessage = "❌ Sesión no iniciada";
            StatusColor = "#FF4444";
            return;
        }

        IsGenerating = true;
        StatusMessage = "⏳ Generando OTP para folio adelantado...";
        StatusColor = "#FFAA00";

        try
        {
            // Reutiliza GenerateOTPAsync del ATUApiClient pero con el endpoint de folio
            var response = await _apiClient.GenerateOTPForFolioAsync(EmbFolio, SupervisorId);

            if (response?.Success == true && response.Data != null)
            {
                BatchId = response.Data.BatchId;
                MostrarOTP(response.Data.Code, response.Data.SecondsRemaining);
                StatusMessage = "✅ Código generado — Léalo en voz alta al operador de embarques";
            }
            else
            {
                StatusMessage = $"❌ {response?.Message ?? "Error desconocido"}";
                StatusColor = "#FF4444";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando OTP para folio");
            StatusMessage = $"❌ Error: {ex.Message}";
            StatusColor = "#FF4444";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    // ── Mostrar OTP en pantalla y arrancar countdown ──────────────────────────

    private void MostrarOTP(string code, int seconds)
    {
        OtpCode = code;
        CountdownSeconds = seconds > 0 ? seconds : ATUApiClient.OtpTtlSeconds;
        ShowScan = false;
        ShowOTP = true;
        StatusMessage = "✅ Código generado — Léalo en voz alta al operador";
        StatusColor = "#44FF44";

        StartCountdown();

        if (HapticFeedback.IsSupported)
            HapticFeedback.Perform(HapticFeedbackType.LongPress);
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ResetScan()
    {
        _countdownCts?.Cancel();

        ScannedCode = string.Empty;
        BatchId = string.Empty;
        ProductName = null;
        OtpCode = string.Empty;
        EmbFolio = string.Empty;
        EsFolioAdelantado = false;
        CountdownSeconds = ATUApiClient.OtpTtlSeconds;
        CountdownText = $"00:{ATUApiClient.OtpTtlSeconds:D2}";
        ShowScan = true;
        ShowOTP = false;
        StatusMessage = "Escanea la etiqueta verde del producto";
        StatusColor = "#FFFFFF";
    }

    // ── Countdown ─────────────────────────────────────────────────────────────

    private void StartCountdown()
    {
        _countdownCts?.Cancel();
        _countdownCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            while (CountdownSeconds > 0)
            {
                try { await Task.Delay(1000, _countdownCts.Token); }
                catch (OperationCanceledException) { return; }

                CountdownSeconds--;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var s = Math.Max(0, CountdownSeconds);
                    CountdownText = $"00:{s:D2}";

                    if (s <= 5)
                    {
                        StatusColor = "#FF4444";
                        StatusMessage = $"⚠️ Código expira en {s}s";
                    }
                    else if (s <= 10)
                    {
                        StatusColor = "#FFAA00";
                    }
                });
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = "⏰ Código expirado — Genera uno nuevo";
                StatusColor = "#FF4444";
                ShowOTP = false;
                ShowScan = true;

                if (HapticFeedback.IsSupported)
                    HapticFeedback.Perform(HapticFeedbackType.Click);
            });
        }, _countdownCts.Token);
    }
}
