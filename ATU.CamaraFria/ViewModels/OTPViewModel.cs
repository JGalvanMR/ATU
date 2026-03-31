using System;
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

    [ObservableProperty]
    private string _scannedCode = string.Empty;

    [ObservableProperty]
    private string _batchId = string.Empty;

    [ObservableProperty]
    private string? _productName;

    [ObservableProperty]
    private string _otpCode = string.Empty;

    [ObservableProperty]
    private int _countdownSeconds;

    [ObservableProperty]
    private string _countdownText = "00:30";

    [ObservableProperty]
    private string _statusMessage = "Escaneé la etiqueta verde del producto";

    [ObservableProperty]
    private string _statusColor = "#FFFFFF";

    [ObservableProperty]
    private bool _showOTP;

    [ObservableProperty]
    private bool _showScan;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private string _supervisorId = string.Empty;

    private CancellationTokenSource? _countdownCts;
    private LabelScanData? _lastScanData;

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
        _countdownSeconds = 30;

        _supervisorId = Preferences.Get("SUPERVISOR_ID", string.Empty);
    }

    /// <summary>
    /// Callback desde la cámara - NO usa RelayCommand
    /// </summary>
    public void OnBarcodeDetected(string code, BarcodeFormat format)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await ProcessCodeFromCameraAsync(code, format);
        });
    }

    /// <summary>
    /// RelayCommand SIN parámetros - lee del Entry (ScannedCode)
    /// </summary>
    [RelayCommand]
    private async Task ProcessScannedCodeAsync()
    {
        var code = ScannedCode;

        if (string.IsNullOrWhiteSpace(code))
        {
            StatusMessage = "❌ Ingrese un código";
            StatusColor = "#FF4444";
            return;
        }

        await ProcessCodeFromCameraAsync(code, BarcodeFormat.QrCode);
    }

    /// <summary>
    /// Lógica compartida de procesamiento (NO decorada con RelayCommand)
    /// </summary>
    private async Task ProcessCodeFromCameraAsync(string code, BarcodeFormat format)
    {
        if (!_scannerService.IsValidLabelCode(code))
        {
            StatusMessage = "❌ Código no válido. Intente de nuevo.";
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

    [RelayCommand]
    private async Task GenerateOTPAsync()
    {
        if (string.IsNullOrEmpty(BatchId))
        {
            StatusMessage = "❌ Escanee un lote primero";
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
                OtpCode = response.Data.Code;
                CountdownSeconds = response.Data.SecondsRemaining;

                ShowScan = false;
                ShowOTP = true;

                StatusMessage = "✅ Código generado - Léalo en voz alta al operador";
                StatusColor = "#44FF44";

                StartCountdown();

                if (HapticFeedback.IsSupported)
                {
                    HapticFeedback.Perform(HapticFeedbackType.LongPress);
                }
            }
            else
            {
                var errorMsg = response?.Message ?? "Error desconocido";

                if (response?.Errors?.Contains("OFFLINE_MODE") == true)
                {
                    StatusMessage = "📵 Sin conexión - Solicitud guardada para sincronizar";
                    StatusColor = "#FFAA00";
                }
                else
                {
                    StatusMessage = $"❌ {errorMsg}";
                    StatusColor = "#FF4444";
                }
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

    [RelayCommand]
    private void ResetScan()
    {
        _countdownCts?.Cancel();

        ScannedCode = string.Empty;
        BatchId = string.Empty;
        ProductName = null;
        OtpCode = string.Empty;
        CountdownSeconds = 30;
        CountdownText = "00:30";

        ShowScan = true;
        ShowOTP = false;

        StatusMessage = "Escaneé la etiqueta verde del producto";
        StatusColor = "#FFFFFF";
    }

    private void StartCountdown()
    {
        _countdownCts?.Cancel();
        _countdownCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            while (CountdownSeconds > 0)
            {
                _countdownCts.Token.ThrowIfCancellationRequested();

                await Task.Delay(1000, _countdownCts.Token);

                CountdownSeconds--;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var seconds = Math.Max(0, CountdownSeconds);
                    CountdownText = $"00:{seconds:D2}";

                    if (seconds <= 5)
                    {
                        StatusColor = "#FF4444";
                        StatusMessage = $"⚠️ Código expira en {seconds}s";
                    }
                    else if (seconds <= 10)
                    {
                        StatusColor = "#FFAA00";
                    }
                });
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = "⏰ Código expirado - Genere uno nuevo";
                StatusColor = "#FF4444";
                ShowOTP = false;
                ShowScan = true;

                if (HapticFeedback.IsSupported)
                {
                    HapticFeedback.Perform(HapticFeedbackType.Click);
                }
            });
        }, _countdownCts.Token);
    }
}