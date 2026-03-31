using System;
using System.Threading.Tasks;
using ATU.CamaraFria.Models;
using ATU.CamaraFria.Services;
using ATU.CamaraFria.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace ATU.CamaraFria.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly ATUApiClient _apiClient;
    private readonly ILogger<LoginViewModel> _logger;

    [ObservableProperty]
    private string _employeeNumber = string.Empty;

    [ObservableProperty]
    private string _serverUrl = "http://192.168.1.100:5001";

    [ObservableProperty]
    private bool _showServerConfig = false;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError = false;

    [RelayCommand]
    private void ToggleServerConfig()
    {
        ShowServerConfig = !ShowServerConfig;
    }

    public LoginViewModel(ATUApiClient apiClient, ILogger<LoginViewModel> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
        _serverUrl = Preferences.Get("SERVER_URL", "http://192.168.1.100:5001");
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(EmployeeNumber))
        {
            ErrorMessage = "Ingrese el número de empleado";
            HasError = true;
            return;
        }

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var request = new LoginRequest
            {
                EmployeeNumber = EmployeeNumber,
                DeviceName = DeviceInfo.Model ?? "Unknown"
            };

            var response = await _apiClient.LoginAsync(request);

            if (response?.Success == true && response.Data != null)
            {
                // Guardar sesión
                Preferences.Set("AUTH_TOKEN", response.Data.Token);
                Preferences.Set("SUPERVISOR_ID", response.Data.SupervisorId);
                Preferences.Set("SUPERVISOR_NAME", response.Data.SupervisorName);
                Preferences.Set("SERVER_URL", ServerUrl);

                // Navegar al main
                // Cambiar la página raíz al AppShell
                App.Current.Windows[0].Page = new AppShell();
            }
            else
            {
                ErrorMessage = response?.Message ?? "Credenciales inválidas";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en login");
            ErrorMessage = $"Error de conexión: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}