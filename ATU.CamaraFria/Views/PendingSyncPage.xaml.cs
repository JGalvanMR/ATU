using ATU.CamaraFria.Models;
using ATU.CamaraFria.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;

namespace ATU.CamaraFria.Views;

public partial class PendingSyncPage : ContentPage
{
    private readonly SyncQueueService _syncQueue;
    private readonly ATUApiClient _apiClient;

    public PendingSyncPage()
    {
        InitializeComponent();

        var services = Application.Current!.Handler!.MauiContext!.Services;
        _syncQueue = services.GetRequiredService<SyncQueueService>();
        _apiClient = services.GetRequiredService<ATUApiClient>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarDatosAsync();
    }

    private async Task CargarDatosAsync()
    {
        var items = await _syncQueue.GetPendingItemsAsync();
        var pendiente = items.Count(x => x.Status == SyncStatus.Pending);
        var fallidos = items.Count(x => x.Status == SyncStatus.Failed);
        var conectado = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

        LblTotalPendientes.Text = pendiente.ToString();
        LblFallidos.Text = fallidos.ToString();
        LblConexion.Text = conectado ? "🟢" : "🔴";

        // Proyectar a un modelo simple para el CollectionView
        var vm = items.Select(x => new PendingItemVm
        {
            TipoIcono = x.Type switch
            {
                SyncType.OTPGeneration => "🔑",
                SyncType.Login => "👤",
                SyncType.DeviceEnrollment => "📱",
                _ => "📋"
            },
            TipoTexto = x.Type switch
            {
                SyncType.OTPGeneration => "Generación de OTP",
                SyncType.Login => "Inicio de sesión",
                SyncType.DeviceEnrollment => "Enrolamiento de dispositivo",
                _ => "Evento de auditoría"
            },
            CreatedAt = x.CreatedAt,
            RetryCount = x.RetryCount,
            ErrorMessage = x.ErrorMessage,
            TieneError = !string.IsNullOrEmpty(x.ErrorMessage)
        }).ToList();

        ListaPendientes.ItemsSource = vm;
    }

    private async void OnSincronizar(object sender, EventArgs e)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            await DisplayAlert("Sin red", "No hay conexión a internet.", "OK");
            return;
        }

        BtnSync.IsEnabled = false;
        BtnSync.Text = "⏳ Sincronizando...";

        var (procesados, fallidos) = await _syncQueue.ProcessQueueAsync();

        BtnSync.IsEnabled = true;
        BtnSync.Text = "🔄  Sincronizar ahora";

        await DisplayAlert("Sincronización completa",
            $"Procesados: {procesados}\nFallidos: {fallidos}", "OK");

        await CargarDatosAsync();
    }

    private class PendingItemVm
    {
        public string TipoIcono { get; set; } = "";
        public string TipoTexto { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int RetryCount { get; set; }
        public string? ErrorMessage { get; set; }
        public bool TieneError { get; set; }
    }
}
