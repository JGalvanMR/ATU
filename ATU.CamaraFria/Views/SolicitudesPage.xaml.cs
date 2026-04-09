using ATU.CamaraFria.Models;
using ATU.CamaraFria.Services;
using ATU.CamaraFria.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace ATU.CamaraFria.Views;

public partial class SolicitudesPage : ContentPage
{
    private readonly ATUApiClient _apiClient;
    private List<SolicitudVm> _solicitudes = new();

    public SolicitudesPage()
    {
        InitializeComponent();
        _apiClient = Application.Current!.Handler!.MauiContext!
                        .Services.GetRequiredService<ATUApiClient>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarSolicitudesAsync();
    }

    public async Task CargarSolicitudesAsync()
    {
        try
        {
            LblUltimaActualizacion.Text = "Cargando...";
            _solicitudes = await _apiClient.GetSolicitudesPendientesAsync();
            ListaSolicitudes.ItemsSource = null;
            ListaSolicitudes.ItemsSource = _solicitudes;
            LblUltimaActualizacion.Text =
                $"Actualizado {DateTime.Now:HH:mm}  ·  {_solicitudes.Count} pendiente(s)";
        }
        catch (Exception ex)
        {
            LblUltimaActualizacion.Text = $"Error: {ex.Message}";
        }
        finally
        {
            RefreshView.IsRefreshing = false;
        }
    }

    private async void OnRefrescar(object? sender, EventArgs e)
        => await CargarSolicitudesAsync();

    private async void OnAutorizarSolicitud(object sender, EventArgs e)
    {
        if (((Button)sender).CommandParameter is not SolicitudVm solicitud) return;

        await Shell.Current.GoToAsync("//Scan");
        await Task.Delay(300);

        if (Shell.Current.CurrentPage?.BindingContext is OTPViewModel vm)
        {
            vm.CargarFolioAdelantado(
                solicitud.EmbFolio,
                solicitud.ProdClave,
                solicitud.ReciboSug,
                solicitud.TarimaSug,
                solicitud.Producto,
                solicitud.Responsable,
                solicitud.Motivo);
        }
    }

    private void OnIgnorarSolicitud(object sender, EventArgs e)
    {
        if (((SwipeItem)sender).CommandParameter is SolicitudVm s)
        {
            _solicitudes.Remove(s);
            ListaSolicitudes.ItemsSource = null;
            ListaSolicitudes.ItemsSource = _solicitudes;
        }
    }
}
