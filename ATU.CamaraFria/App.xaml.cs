// ATU.CamaraFria/App.xaml.cs
// La lógica de SignalR y notificaciones vive en ATUNotificationService (Foreground Service).
// App.xaml.cs solo gestiona la sesión y la navegación inicial.

using ATU.CamaraFria.Services;
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

    public App(IServiceProvider services)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Android.Util.Log.Error("ATUApp", $"UNHANDLED EXCEPTION: {ex}");
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Android.Util.Log.Error("ATUApp", $"UNOBSERVED TASK EXCEPTION: {args.Exception}");
            args.SetObserved();
        };
        Android.Util.Log.Debug("ATUApp", "App() constructor START");
        try
        {
            InitializeComponent();
            Android.Util.Log.Debug("ATUApp", "InitializeComponent OK");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ATUApp", $"InitializeComponent FAILED: {ex}");
            throw; // Re-lanzamos para que se vea
        }

        _services = services;
        Android.Util.Log.Debug("ATUApp", "App() constructor END");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Android.Util.Log.Debug("ATUApp", "CreateWindow START");
        try
        {
            var tieneSession = !string.IsNullOrEmpty(
                Preferences.Get("AUTH_TOKEN", string.Empty));
            Android.Util.Log.Debug("ATUApp", $"tieneSession = {tieneSession}");

            if (tieneSession)
            {
                Android.Util.Log.Debug("ATUApp", "Creando AppShell");
                return new Window(new AppShell());
            }
            else
            {
                Android.Util.Log.Debug("ATUApp", "Creando LoginPage");
                var loginPage = _services.GetRequiredService<LoginPage>();
                return new Window(new NavigationPage(loginPage));
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("ATUApp", $"CreateWindow ERROR: {ex}");
            return new Window(PaginaDeError(ex));
        }
    }

    protected override void OnStart()
    {
        base.OnStart();
        Android.Util.Log.Debug("ATUApp", "OnStart START");
        // El Foreground Service (ATUNotificationService) lo inicia MainActivity
        // Aquí solo procesamos la cola de sincronización offline si hay red
        var syncQueue = _services.GetService<SyncQueueService>();
        if (syncQueue != null && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            _ = syncQueue.ProcessQueueAsync();
    }

    // ── Pantalla de error para depuración ────────────────────────────────────

    private static ContentPage PaginaDeError(Exception ex)
        => new ContentPage
        {
            BackgroundColor = Colors.DarkRed,
            Content = new ScrollView
            {
                Content = new Label
                {
                    Text = Desempacar(ex),
                    TextColor = Colors.White,
                    FontSize = 12,
                    Margin = 20,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };

    private static string Desempacar(Exception? ex, int n = 0)
    {
        if (ex == null) return "(null)";
        var sb = new System.Text.StringBuilder();
        var pad = new string(' ', n * 2);
        sb.AppendLine($"{pad}[{ex.GetType().Name}] {ex.Message}");
        if (ex.StackTrace != null)
            foreach (var l in ex.StackTrace.Split('\n').Take(8))
                sb.AppendLine($"{pad}  {l.Trim()}");
        if (ex.InnerException != null)
        {
            sb.AppendLine($"{pad}── Inner ──");
            sb.Append(Desempacar(ex.InnerException, n + 1));
        }
        return sb.ToString();
    }
}

// ── DeviceFingerprintService ─────────────────────────────────────────────────

public class DeviceFingerprintService : IDeviceFingerprintService
{
    private string? _fp;

    public string GetFingerprint()
    {
        if (_fp != null) return _fp;
        var id = Preferences.Get("DEVICE_ID", string.Empty);
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString("N")[..16];
            Preferences.Set("DEVICE_ID", id);
        }
        var raw = $"{id}|{DeviceInfo.Platform}|{DeviceInfo.Model}|{DeviceInfo.VersionString}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        _fp = Convert.ToHexString(hash).ToLowerInvariant();
        return _fp;
    }
}
