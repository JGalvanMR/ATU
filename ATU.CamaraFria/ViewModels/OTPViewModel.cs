using System.Text.Json;
using ATU.CamaraFria.Models;
using ATU.CamaraFria.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using ZXing.Net.Maui;

namespace ATU.CamaraFria.ViewModels;

public partial class OTPViewModel : BaseViewModel
{
    private readonly ATUApiClient _apiClient;
    private readonly IScannerService _scannerService;

    [ObservableProperty] private bool _showScanNormal = true;
    [ObservableProperty] private bool _showConfirmarFolio;
    [ObservableProperty] private bool _showOTP;

    [ObservableProperty] private string _scannedCode = string.Empty;
    [ObservableProperty] private string _statusMessage = "Apunta la cámara a la etiqueta verde del pallet";
    [ObservableProperty] private string _statusColor = "#8AA0BC";
    [ObservableProperty] private bool _isGenerating;

    [ObservableProperty] private string _embFolio = string.Empty;
    [ObservableProperty] private string _batchId = string.Empty;
    [ObservableProperty] private string _infoProducto = string.Empty;
    [ObservableProperty] private string _infoSolicitante = string.Empty;
    [ObservableProperty] private string _motivoSolicitud = string.Empty;
    [ObservableProperty] private bool _esFolioAdelantado;

    [ObservableProperty] private string _otpCode = string.Empty;
    [ObservableProperty] private int _countdownSeconds = 30;
    [ObservableProperty] private string _countdownText = "00:30";

    [ObservableProperty] private string _instruccionConfirmar = "Escanea el código QR del pallet físico";
    [ObservableProperty] private bool _palletVerificado;
    [ObservableProperty] private string _palletVerificadoTexto = string.Empty;

    private string _supervisorId = string.Empty;
    private CancellationTokenSource? _countdownCts;
    private LabelScanData? _lastScanData;
    private bool _procesando;

    public OTPViewModel(ATUApiClient apiClient, IScannerService scannerService)
    {
        _apiClient = apiClient;
        _scannerService = scannerService;
        _supervisorId = Preferences.Get("SUPERVISOR_ID", string.Empty);
    }

    public void CargarFolioAdelantado(
        string embFolio, string prodClave, string reciboSug, string tarimaSug,
        string producto = "", string responsable = "", string motivo = "")
    {
        EmbFolio = embFolio;
        BatchId = $"{prodClave.Trim()}-{reciboSug.Trim()}-{tarimaSug.Trim()}";
        InfoProducto = $"{prodClave.Trim()} — {producto}";
        InfoSolicitante = $"Solicitó: {responsable}";
        MotivoSolicitud = $"Motivo: {motivo}";
        EsFolioAdelantado = true;
        PalletVerificado = false;
        PalletVerificadoTexto = string.Empty;
        MostrarPantallaConfirmar();
    }

    public void OnBarcodeDetected(string code, BarcodeFormat format)
    {
        if (_procesando) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _procesando = true;
            if (ShowScanNormal)
                await ProcesarCodigoNormalAsync(code, format);
            else if (ShowConfirmarFolio && !PalletVerificado)
                await ConfirmarPalletFisicoAsync(code, format);
            await Task.Delay(2000);
            _procesando = false;
        });
    }

    [RelayCommand]
    private async Task ProcessScannedCodeAsync()
    {
        if (!string.IsNullOrWhiteSpace(ScannedCode))
            await ProcesarCodigoNormalAsync(ScannedCode, BarcodeFormat.QrCode);
    }

    private async Task ProcesarCodigoNormalAsync(string code, BarcodeFormat format)
    {
        if (!_scannerService.IsValidLabelCode(code))
        { StatusMessage = "Etiqueta no reconocida — intenta de nuevo"; StatusColor = "#FF4444"; return; }

        IsGenerating = true; StatusMessage = "Procesando etiqueta..."; StatusColor = "#FFAA00";
        try
        {
            _lastScanData = _scannerService.ParseScannedCode(code, format);
            BatchId = _lastScanData.ExtractedBatchId;
            EmbFolio = string.Empty; EsFolioAdelantado = false;
            StatusMessage = $"Lote: {_scannerService.FormatBatchIdForDisplay(BatchId)}";
            StatusColor = "#44FF44";
            await GenerarOTPNormalAsync();
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; StatusColor = "#FF4444"; }
        finally { IsGenerating = false; }
    }

    private async Task GenerarOTPNormalAsync()
    {
        var response = await _apiClient.GenerateOTPAsync(new OTPRequest
        { SupervisorId = _supervisorId, BatchId = BatchId, LabelData = _lastScanData });
        if (response?.Success == true && response.Data != null)
            MostrarOTP(response.Data.Code, response.Data.SecondsRemaining);
        else { StatusMessage = response?.Message ?? "Error al generar OTP"; StatusColor = "#FF4444"; }
    }

    private void MostrarPantallaConfirmar()
    {
        ShowScanNormal = false; ShowConfirmarFolio = true; ShowOTP = false;
        InstruccionConfirmar =
            $"Escanea el QR o código de barras del pallet físico\n" +
            $"para confirmar antes de autorizar.\n\n" +
            $"Lote esperado: {BatchId}";
    }

    private async Task ConfirmarPalletFisicoAsync(string code, BarcodeFormat format)
    {
        try
        {
            var scan = _scannerService.ParseScannedCode(code, format);
            var batch = scan.ExtractedBatchId;
            bool ok = BatchId.Contains(batch, StringComparison.OrdinalIgnoreCase)
                   || batch.Contains(BatchId, StringComparison.OrdinalIgnoreCase)
                   || BatchId.Replace("-", "").Contains(batch.Replace("-", ""), StringComparison.OrdinalIgnoreCase);

            if (!ok)
            {
                InstruccionConfirmar =
                    $"El pallet escaneado ({batch}) no coincide con la solicitud ({BatchId}).\n" +
                    $"Verifica el pallet correcto e intenta de nuevo.";
                return;
            }
            PalletVerificado = true;
            PalletVerificadoTexto = $"Pallet confirmado: {batch}";
            InstruccionConfirmar = "Pallet verificado. Generando código OTP...";
            await GenerarOTPParaFolioAsync();
        }
        catch (Exception ex) { InstruccionConfirmar = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task GenerateOTPForFolioCommand() => await GenerarOTPParaFolioAsync();

    private async Task GenerarOTPParaFolioAsync()
    {
        IsGenerating = true;
        try
        {
            var r = await _apiClient.GenerateOTPForFolioAsync(EmbFolio, _supervisorId);
            if (r?.Success == true && r.Data != null)
            {
                if (!string.IsNullOrEmpty(r.Data.BatchId)) BatchId = r.Data.BatchId;
                MostrarOTP(r.Data.Code, r.Data.SecondsRemaining);
            }
            else { InstruccionConfirmar = r?.Message ?? "Error al generar OTP"; PalletVerificado = false; }
        }
        catch (Exception ex) { InstruccionConfirmar = $"Error: {ex.Message}"; }
        finally { IsGenerating = false; }
    }

    private void MostrarOTP(string code, int seconds)
    {
        OtpCode = code; CountdownSeconds = seconds > 0 ? seconds : ATUApiClient.OtpTtlSeconds;
        ShowScanNormal = false; ShowConfirmarFolio = false; ShowOTP = true;
        StartCountdown();
        if (HapticFeedback.IsSupported) HapticFeedback.Perform(HapticFeedbackType.LongPress);
    }

    [RelayCommand]
    public void ResetScan()
    {
        _countdownCts?.Cancel();
        ScannedCode = string.Empty; BatchId = string.Empty; EmbFolio = string.Empty;
        OtpCode = string.Empty; EsFolioAdelantado = false;
        PalletVerificado = false; PalletVerificadoTexto = string.Empty;
        CountdownSeconds = ATUApiClient.OtpTtlSeconds; CountdownText = $"00:{ATUApiClient.OtpTtlSeconds:D2}";
        ShowScanNormal = true; ShowConfirmarFolio = false; ShowOTP = false;
        StatusMessage = "Apunta la cámara a la etiqueta verde del pallet"; StatusColor = "#8AA0BC";
    }

    private void StartCountdown()
    {
        _countdownCts?.Cancel(); _countdownCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (CountdownSeconds > 0)
            {
                try { await Task.Delay(1000, _countdownCts.Token); }
                catch (OperationCanceledException) { return; }
                CountdownSeconds--;
                var s = Math.Max(0, CountdownSeconds);
                MainThread.BeginInvokeOnMainThread(() => { CountdownText = $"00:{s:D2}"; });
            }
            MainThread.BeginInvokeOnMainThread(() => ResetScan());
        }, _countdownCts.Token);
    }
}
