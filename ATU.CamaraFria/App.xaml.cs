using ATU.CamaraFria.Services;
using ATU.CamaraFria.ViewModels;
using ATU.CamaraFria.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace ATU.CamaraFria;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly SignalRListenerService _signalR;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _signalR = services.GetRequiredService<SignalRListenerService>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            var hasSession = !string.IsNullOrEmpty(Preferences.Get("AUTH_TOKEN", string.Empty));
            return hasSession
                ? new Window(new AppShell())
                : new Window(new NavigationPage(_services.GetRequiredService<LoginPage>()));
        }
        catch (Exception ex)
        {
            return new Window(PaginaDeError(ex));
        }
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Arrancar SignalR para recibir notificaciones de folios adelantados
        _signalR.NuevoFolioRecibido += OnNuevoFolioRecibido;
        _ = _signalR.IniciarAsync();

        // Procesar cola offline si hay red
        var syncQueue = _services.GetService<SyncQueueService>();
        if (syncQueue != null && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            _ = syncQueue.ProcessQueueAsync();
    }

    // El supervisor toca "GENERAR OTP" en el alert
    private void OnNuevoFolioRecibido(FolioAdelantatadoNotification notif)
        => _ = MostrarAlertaYNavegar(notif);

    private async Task MostrarAlertaYNavegar(FolioAdelantatadoNotification notif)
    {
        if (Current?.MainPage == null) return;

        if (HapticFeedback.IsSupported)
            HapticFeedback.Perform(HapticFeedbackType.LongPress);

        bool responde = await Current.MainPage.DisplayAlert(
            "🔔 Folio Adelantado Requerido",
            $"Folio:     {notif.EmbFolio}\n" +
            $"Producto:  {notif.ProdClave} — {notif.Producto}\n" +
            $"Recibo:    {notif.ReciboSug}  |  Tarima: {notif.TarimaSug}\n" +
            $"Solicitó:  {notif.Responsable}\n" +
            $"Motivo:    {notif.Motivo}",
            "GENERAR OTP",
            "Ver después");

        if (!responde) return;

        // Navegar a la pestaña Escanear
        await Shell.Current.GoToAsync("//Scan");
        await Task.Delay(300);

        // Pre-cargar el folio en el ViewModel y generar OTP
        if (Shell.Current.CurrentPage?.BindingContext is OTPViewModel vm)
        {
            vm.CargarFolioAdelantado(notif.EmbFolio, notif.ProdClave,
                                     notif.ReciboSug, notif.TarimaSug);
            await vm.GenerateOTPForFolioCommandCommand.ExecuteAsync(null);
        }
    }

    private static ContentPage PaginaDeError(Exception ex)
        => new ContentPage
        {
            BackgroundColor = Colors.DarkRed,
            Content = new ScrollView
            {
                Content = new Label
                {
                    Text = DesempacarExcepcion(ex),
                    TextColor = Colors.White,
                    FontSize = 12,
                    Margin = 20,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };

    private static string DesempacarExcepcion(Exception? ex, int n = 0)
    {
        if (ex == null) return "(null)";
        var sb = new System.Text.StringBuilder();
        var pad = new string(' ', n * 2);
        sb.AppendLine($"{pad}[{ex.GetType().Name}] {ex.Message}");
        if (ex.StackTrace != null)
            foreach (var l in ex.StackTrace.Split('\n').Take(10))
                sb.AppendLine($"{pad}  {l.Trim()}");
        if (ex.InnerException != null)
        { sb.AppendLine($"{pad}── Inner ──"); sb.Append(DesempacarExcepcion(ex.InnerException, n + 1)); }
        return sb.ToString();
    }
}

public class DeviceFingerprintService : IDeviceFingerprintService
{
    private string? _fingerprint;
    public string GetFingerprint()
    {
        if (_fingerprint != null) return _fingerprint;
        var deviceId = Preferences.Get("DEVICE_ID", string.Empty);
        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = Guid.NewGuid().ToString("N")[..16];
            Preferences.Set("DEVICE_ID", deviceId);
        }
        var raw = $"{deviceId}|{DeviceInfo.Platform}|{DeviceInfo.Model}|{DeviceInfo.VersionString}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        _fingerprint = Convert.ToHexString(hash).ToLowerInvariant();
        return _fingerprint;
    }
}
