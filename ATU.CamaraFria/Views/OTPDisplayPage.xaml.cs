using ATU.CamaraFria.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;
using ZXing.Net.Maui;

namespace ATU.CamaraFria.Views;

public partial class OTPDisplayPage : ContentPage
{
    private readonly OTPViewModel _viewModel;
    private bool _procesandoCodigo = false; // evita disparos dobles de la cámara

    public OTPDisplayPage(OTPViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // ── Callback de ZXing cuando detecta un código ───────────────────────────

    private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        // ZXing dispara en hilo de cámara, lo pasamos al hilo UI
        var primer = e.Results.FirstOrDefault();
        if (primer == null) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Ignorar si ya estamos procesando o mostrando OTP
            if (_procesandoCodigo || _viewModel.ShowOTP) return;
            _procesandoCodigo = true;

            // Pausar detección mientras procesamos
            CameraScanner.IsDetecting = false;

            _viewModel.OnBarcodeDetected(primer.Value, primer.Format);

            // Reactivar después de 3 segundos si no se generó OTP
            await Task.Delay(3000);
            if (!_viewModel.ShowOTP)
                CameraScanner.IsDetecting = true;

            _procesandoCodigo = false;
        });
    }

    // ── Ciclo de vida de la página ────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        CameraScanner.IsDetecting = true;
        ActualizarIndicadorConexion();
        ActualizarFolioActivo();
    }

    protected override void OnDisappearing()
    {
        CameraScanner.IsDetecting = false;
        _viewModel.ResetScanCommand.Execute(null);
        base.OnDisappearing();
    }

    private void ActualizarIndicadorConexion()
    {
        var conectado = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        ConnectionIndicator.BackgroundColor = conectado ? Colors.LimeGreen : Colors.Red;
    }

    private void ActualizarFolioActivo()
    {
        LblFolioActivo.Text = string.IsNullOrEmpty(_viewModel.EmbFolio)
            ? ""
            : $"Folio: {_viewModel.EmbFolio}";
    }
}
