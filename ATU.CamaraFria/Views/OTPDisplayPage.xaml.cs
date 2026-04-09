using ATU.CamaraFria.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;
using ZXing.Net.Maui;

namespace ATU.CamaraFria.Views;

public partial class OTPDisplayPage : ContentPage
{
    private readonly OTPViewModel _viewModel;

    public OTPDisplayPage(OTPViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // ── Callback de ZXing — funciona para CameraScanner y CameraConfirm ──────

    private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        var primer = e.Results.FirstOrDefault();
        if (primer == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Pausar la cámara que detectó
            if (sender is ZXing.Net.Maui.Controls.CameraBarcodeReaderView cam)
                cam.IsDetecting = false;

            _viewModel.OnBarcodeDetected(primer.Value, primer.Format);
        });
    }

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ActualizarCamaras();
        ActualizarConexion();
    }

    protected override void OnDisappearing()
    {
        // Detener ambas cámaras al salir
        CameraScanner.IsDetecting = false;
        CameraConfirm.IsDetecting = false;
        base.OnDisappearing();
    }

    private void ActualizarCamaras()
    {
        CameraScanner.IsDetecting = _viewModel.ShowScanNormal;
        CameraConfirm.IsDetecting = _viewModel.ShowConfirmarFolio;

        // Reactivar cámara si el ViewModel cambia de panel
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_viewModel.ShowScanNormal))
            {
                CameraScanner.IsDetecting = _viewModel.ShowScanNormal;
                CameraConfirm.IsDetecting = false;
            }
            else if (args.PropertyName == nameof(_viewModel.ShowConfirmarFolio))
            {
                CameraConfirm.IsDetecting = _viewModel.ShowConfirmarFolio;
                CameraScanner.IsDetecting = false;
            }
            else if (args.PropertyName == nameof(_viewModel.ShowOTP))
            {
                // Detener ambas cámaras cuando se muestra el OTP
                if (_viewModel.ShowOTP)
                {
                    CameraScanner.IsDetecting = false;
                    CameraConfirm.IsDetecting = false;
                }
            }
            else if (args.PropertyName == nameof(_viewModel.PalletVerificado)
                  && !_viewModel.PalletVerificado)
            {
                // Si el pallet no coincidió, reactivar confirmación
                CameraConfirm.IsDetecting = _viewModel.ShowConfirmarFolio;
            }
        };
    }

    private void ActualizarConexion()
    {
        var ok = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        ConnectionIndicator.BackgroundColor = ok ? Colors.LimeGreen : Colors.Red;
    }
}
