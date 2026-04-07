using ATU.CamaraFria.Services;
using ATU.CamaraFria.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace ATU.CamaraFria.Views;

public partial class SettingsPage : ContentPage
{
    private readonly ATUApiClient _apiClient;

    public SettingsPage()
    {
        InitializeComponent();
        _apiClient = Application.Current!.Handler!.MauiContext!
                        .Services.GetRequiredService<ATUApiClient>();
        CargarDatos();
    }

    private void CargarDatos()
    {
        // Sesión
        LblNombre.Text = Preferences.Get("SUPERVISOR_NAME", "—");
        LblEmpleado.Text = $"Empleado: {Preferences.Get("SUPERVISOR_ID", "—")}";

        // URL guardada
        EntryUrl.Text = Preferences.Get("SERVER_URL", "http://192.168.123.155:5059");

        // Dispositivo
        LblModelo.Text = DeviceInfo.Model ?? "—";
        LblAndroid.Text = DeviceInfo.VersionString ?? "—";
        LblDeviceId.Text = Preferences.Get("DEVICE_ID", "—");
    }

    private void OnGuardarUrl(object sender, EventArgs e)
    {
        var url = EntryUrl.Text?.Trim() ?? "";
        if (!url.StartsWith("http"))
        {
            LblEstadoConexion.Text = "❌ URL inválida (debe empezar con http)";
            LblEstadoConexion.TextColor = Colors.Red;
            return;
        }

        Preferences.Set("SERVER_URL", url);
        _apiClient.SetBaseUrl(url);

        LblEstadoConexion.Text = "✅ URL guardada";
        LblEstadoConexion.TextColor = Colors.LimeGreen;
    }

    private async void OnProbarConexion(object sender, EventArgs e)
    {
        LblEstadoConexion.Text = "⏳ Probando conexión...";
        LblEstadoConexion.TextColor = Colors.Yellow;

        var ok = await _apiClient.IsServerAvailableAsync();

        LblEstadoConexion.Text = ok ? "🟢 Servidor disponible" : "🔴 Sin respuesta del servidor";
        LblEstadoConexion.TextColor = ok ? Colors.LimeGreen : Colors.Red;
    }

    private async void OnCerrarSesion(object sender, EventArgs e)
    {
        bool confirmar = await DisplayAlert(
            "Cerrar Sesión",
            "¿Deseas cerrar sesión en este dispositivo?",
            "Sí, cerrar", "Cancelar");

        if (!confirmar) return;

        // Limpiar todos los datos de sesión
        Preferences.Remove("AUTH_TOKEN");
        Preferences.Remove("SUPERVISOR_ID");
        Preferences.Remove("SUPERVISOR_NAME");
        Preferences.Remove("LAST_OTP");
        Preferences.Remove("LAST_OTP_TIME");
        // DEVICE_ID y SERVER_URL se conservan para el próximo login

        // Navegar a login
        var loginPage = Application.Current!.Handler!.MauiContext!
                            .Services.GetRequiredService<LoginPage>();

        Application.Current.Windows[0].Page = new NavigationPage(loginPage);
    }
}
