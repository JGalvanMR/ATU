using System;
using ATU.CamaraFria.Services;
using ATU.CamaraFria.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ATU.CamaraFria;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    // Inyectar IServiceProvider en .NET 10
    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var hasSession = !string.IsNullOrEmpty(Preferences.Get("AUTH_TOKEN", string.Empty));

        if (hasSession)
        {
            return new Window(new AppShell());
        }
        else
        {
            return new Window(new NavigationPage(new LoginPage()));
        }
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Usar _services en lugar de this.Services
        var syncQueue = _services.GetService<SyncQueueService>();
        if (syncQueue != null && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            _ = syncQueue.ProcessQueueAsync();
        }
    }
}

public class DeviceFingerprintService : IDeviceFingerprintService
{
    private string? _fingerprint;

    public string GetFingerprint()
    {
        if (_fingerprint != null)
            return _fingerprint;

        var deviceId = Preferences.Get("DEVICE_ID", string.Empty);
        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = Guid.NewGuid().ToString("N")[..16];
            Preferences.Set("DEVICE_ID", deviceId);
        }

        var platform = DeviceInfo.Platform.ToString();
        var model = DeviceInfo.Model ?? "Unknown";
        var version = DeviceInfo.VersionString ?? "Unknown";

        var raw = $"{deviceId}|{platform}|{model}|{version}";

        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));

        _fingerprint = Convert.ToHexString(hash).ToLowerInvariant();

        return _fingerprint;
    }
}