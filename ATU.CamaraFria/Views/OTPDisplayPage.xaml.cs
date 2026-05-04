using ATU.CamaraFria.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;

namespace ATU.CamaraFria.Views;

public partial class OTPDisplayPage : ContentPage
{
    private readonly OTPViewModel _vm;

    public OTPDisplayPage(OTPViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _vm = viewModel;

        // El ViewModel pide foco → la Page lo da al Entry correcto
        _vm.SolicitarFocoEntrada += () => MainThread.BeginInvokeOnMainThread(EnfocarEntrada);
        _vm.SolicitarFocoConfirmar += () => MainThread.BeginInvokeOnMainThread(EnfocarConfirmar);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ActualizarConexion();

        // Enfocar el campo correcto según el panel activo
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_vm.ShowScanNormal)
                EnfocarEntrada();
            else if (_vm.ShowConfirmarFolio)
                EnfocarConfirmar();
        });
    }

    // El scanner escribe texto + Enter; el foco debe estar en el Entry
    private void EnfocarEntrada()
    {
        try { EntryEscaneo?.Focus(); }
        catch { /* ignorar si el control no está en pantalla */ }
    }

    private void EnfocarConfirmar()
    {
        try { EntryConfirmar?.Focus(); }
        catch { }
    }

    private void ActualizarConexion()
    {
        var ok = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        ConnectionIndicator.BackgroundColor = ok ? Colors.LimeGreen : Colors.Red;
    }
}
