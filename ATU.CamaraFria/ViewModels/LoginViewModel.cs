using ATU.CamaraFria.Models;
using ATU.CamaraFria.Services;
using ATU.CamaraFria.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace ATU.CamaraFria.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly ATUApiClient _apiClient;

    [ObservableProperty] private string _employeeNumber = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _serverUrl = "http://192.168.123.155:5001";
    [ObservableProperty] private bool _showServerConfig;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    public LoginViewModel(ATUApiClient apiClient)
    {
        _apiClient = apiClient;
        _serverUrl = Preferences.Get("SERVER_URL", "http://192.168.123.155:5001");
    }

    [RelayCommand]
    private void ToggleServerConfig() => ShowServerConfig = !ShowServerConfig;

    [RelayCommand]
    private async Task LoginAsyncOG()
    {
        if (string.IsNullOrWhiteSpace(EmployeeNumber))
        { ErrorMessage = "Ingresa tu número de empleado"; HasError = true; return; }
        if (string.IsNullOrWhiteSpace(Password))
        { ErrorMessage = "Ingresa tu contraseña"; HasError = true; return; }

        IsBusy = true; HasError = false; ErrorMessage = string.Empty;
        try
        {
            _apiClient.SetBaseUrl(ServerUrl);
            Preferences.Set("SERVER_URL", ServerUrl);

            var response = await _apiClient.LoginAsync(new LoginRequest
            {
                EmployeeNumber = EmployeeNumber.Trim(),
                Password = Password.Trim(),
                DeviceName = DeviceInfo.Model ?? "Android"
            });

            if (response?.Success == true && response.Data != null)
            {
                Preferences.Set("AUTH_TOKEN", response.Data.Token);
                Preferences.Set("SUPERVISOR_ID", response.Data.SupervisorId);
                Preferences.Set("SUPERVISOR_NAME", response.Data.SupervisorName);
                Password = string.Empty;
                if (Application.Current?.Windows.Count > 0)
                    Application.Current.Windows[0].Page = new AppShell();
            }
            else
            { ErrorMessage = response?.Message ?? "Credenciales inválidas"; HasError = true; }
        }
        catch (Exception ex)
        { ErrorMessage = $"Error de conexión: {ex.Message}"; HasError = true; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        try
        {
            Android.Util.Log.Debug("ATUApp", "LoginAsync START");
            Android.Util.Log.Debug("ATUApp", $"EmployeeNumber: {EmployeeNumber}, ServerUrl: {ServerUrl}");

            if (string.IsNullOrWhiteSpace(EmployeeNumber))
            { ErrorMessage = "Ingresa tu número de empleado"; HasError = true; return; }
            if (string.IsNullOrWhiteSpace(Password))
            { ErrorMessage = "Ingresa tu contraseña"; HasError = true; return; }

            IsBusy = true; HasError = false; ErrorMessage = string.Empty;

            Android.Util.Log.Debug("ATUApp", "Configurando URL base...");
            _apiClient.SetBaseUrl(ServerUrl);
            Preferences.Set("SERVER_URL", ServerUrl);

            Android.Util.Log.Debug("ATUApp", "Llamando a LoginAsync en API...");
            var response = await _apiClient.LoginAsync(new LoginRequest
            {
                EmployeeNumber = EmployeeNumber.Trim(),
                Password = Password.Trim(),
                DeviceName = DeviceInfo.Model ?? "Android"
            });
            Android.Util.Log.Debug("ATUApp", $"Respuesta recibida, Success: {response?.Success}");

            if (response?.Success == true && response.Data != null)
            {
                Android.Util.Log.Debug("ATUApp", "Login exitoso, guardando token...");
                Preferences.Set("AUTH_TOKEN", response.Data.Token);
                Preferences.Set("SUPERVISOR_ID", response.Data.SupervisorId);
                Preferences.Set("SUPERVISOR_NAME", response.Data.SupervisorName);
                Password = string.Empty;

                Android.Util.Log.Debug("ATUApp", "Navegando a AppShell...");
                if (Application.Current?.Windows.Count > 0)
                {
                    Application.Current.Windows[0].Page = new AppShell();
                    Android.Util.Log.Debug("ATUApp", "Navegación exitosa");
                }
                else
                {
                    Android.Util.Log.Error("ATUApp", "No hay ventanas disponibles para navegar");
                    ErrorMessage = "Error: No hay ventanas disponibles";
                    HasError = true;
                }
            }
            else
            {
                var msg = response?.Message ?? "Credenciales inválidas";
                Android.Util.Log.Warn("ATUApp", $"Login fallido: {msg}");
                ErrorMessage = msg;
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ATUApp", $"EXCEPCIÓN en LoginAsync: {ex}");
            // Muestra el mensaje completo en la UI (para depuración, luego lo puedes acortar)
            ErrorMessage = $"Error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsBusy = false;
            Android.Util.Log.Debug("ATUApp", "LoginAsync END");
        }
    }

}
