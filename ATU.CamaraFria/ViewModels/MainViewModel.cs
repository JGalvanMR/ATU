using ATU.CamaraFria.Services;
using ATU.CamaraFria.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using System;
using System.Threading.Tasks;

namespace ATU.CamaraFria.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly ATUApiClient _apiClient;
    private readonly IBiometricService _biometricService;
    private readonly SyncQueueService _syncQueue;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private string _supervisorName = string.Empty;

    [ObservableProperty]
    private string _serverUrl = "http://192.168.123.155:5001";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _pendingSyncCount;

    [ObservableProperty]
    private string _connectionStatus = "Desconectado";

    [ObservableProperty]
    private string _connectionStatusColor = "#FF4444";

    [ObservableProperty]
    private DateTime _lastSyncAt;

    public MainViewModel(
        ATUApiClient apiClient,
        IBiometricService biometricService,
        SyncQueueService syncQueue,
        ILogger<MainViewModel> logger)
    {
        _apiClient = apiClient;
        _biometricService = biometricService;
        _syncQueue = syncQueue;
        _logger = logger;

        _serverUrl = Preferences.Get("SERVER_URL", "http://192.168.123.155:5001");
        _supervisorName = Preferences.Get("SUPERVISOR_NAME", string.Empty);
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await CheckConnectionAsync();
        await UpdatePendingCountAsync();

        Connectivity.Current.ConnectivityChanged += async (_, _) =>
        {
            await CheckConnectionAsync();
            if (IsConnected)
            {
                await ProcessSyncQueueAsync();
            }
        };
    }

    [RelayCommand]
    private async Task CheckConnectionAsync()
    {
        IsConnected = await _apiClient.IsServerAvailableAsync();

        if (IsConnected)
        {
            ConnectionStatus = "Conectado";
            ConnectionStatusColor = "#44FF44";
            LastSyncAt = DateTime.UtcNow;
        }
        else
        {
            ConnectionStatus = "Desconectado (Modo Offline)";
            ConnectionStatusColor = "#FF4444";
        }
    }

    [RelayCommand]
    private async Task GoToScanAsync()
    {
        var requiresAuth = Preferences.Get("REQUIRES_AUTH", true);

        if (requiresAuth)
        {
            var authenticated = await _biometricService.QuickAuthenticateAsync();
            if (!authenticated)
            {
                // 🔽 Cambio aquí: DisplayAlert (sin Async)
                await Shell.Current.DisplayAlert(
                    "Acceso Denegado",
                    "Autenticación biométrica requerida",
                    "OK");
                return;
            }
        }

        await Shell.Current.GoToAsync(nameof(OTPDisplayPage));
    }

    [RelayCommand]
    private async Task GoToPendingSyncAsync()
    {
        await Shell.Current.GoToAsync(nameof(PendingSyncPage));
    }

    [RelayCommand]
    private async Task ProcessSyncQueueAsync()
    {
        if (!IsConnected) return;

        var (processed, failed) = await _syncQueue.ProcessQueueAsync();
        await UpdatePendingCountAsync();

        if (processed > 0 || failed > 0)
        {
            // 🔽 Cambio aquí: DisplayAlert (sin Async)
            await Shell.Current.DisplayAlert(
                "Sincronización",
                $"Procesados: {processed}\nFallidos: {failed}",
                "OK");
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        // 🔽 Cambio aquí: DisplayAlert (sin Async) y devuelve bool
        var confirm = await Shell.Current.DisplayAlert(
            "Cerrar Sesión",
            "¿Desea cerrar sesión?",
            "Sí", "No");

        if (confirm)
        {
            Preferences.Clear();
            await Shell.Current.GoToAsync("//LoginPage");
        }
    }

    [RelayCommand]
    private void SaveServerUrl()
    {
        Preferences.Set("SERVER_URL", ServerUrl);
        _apiClient.SetBaseUrl(ServerUrl);
        _ = CheckConnectionAsync();
    }

    private async Task UpdatePendingCountAsync()
    {
        PendingSyncCount = await _syncQueue.GetPendingCountAsync();
    }
}

// partial es OBLIGATORIO en .NET 10 porque ObservableObject también es partial
public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _title = string.Empty;
}