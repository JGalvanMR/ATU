using ATU.CamaraFria.Services;
using ATU.CamaraFria.ViewModels;
using ATU.CamaraFria.Views;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Biometric;
using ZXing.Net.Maui.Controls;

namespace ATU.CamaraFria;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        try
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()          // ZXing — REQUERIDO o crashea
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            // ── Infraestructura ──────────────────────────────────────────────
            builder.Services.AddSingleton<SyncDbContext>();
            builder.Services.AddSingleton<SyncQueueService>();
            builder.Services.AddSingleton<IDeviceFingerprintService, DeviceFingerprintService>();
            builder.Services.AddSingleton<ATUApiClient>();

            // ── SignalR listener (NUEVO — notificaciones desde CargaEmbarques) ─
            builder.Services.AddSingleton<SignalRListenerService>();

            // ── Biometría ────────────────────────────────────────────────────
            builder.Services.AddSingleton<IBiometric, MockBiometric>();
            builder.Services.AddSingleton<BiometricService>();

            // ── Scanner ──────────────────────────────────────────────────────
            builder.Services.AddSingleton<IScannerService, ScannerService>();

            // ── ViewModels ───────────────────────────────────────────────────
            builder.Services.AddTransient<LoginViewModel>();
            builder.Services.AddTransient<MainViewModel>();
            builder.Services.AddTransient<OTPViewModel>();

            // ── Páginas ──────────────────────────────────────────────────────
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<OTPDisplayPage>();
            builder.Services.AddTransient<PendingSyncPage>();
            builder.Services.AddTransient<SettingsPage>();

            return builder.Build();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ATU FATAL en MauiProgram]: {ex}");
            throw;
        }
    }
}

// MockBiometric sin cambios
public class MockBiometric : IBiometric
{
    public Task<AuthenticationResponse> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new AuthenticationResponse());

    public Task<BiometricHwStatus> GetAuthenticationStatusAsync(AuthenticatorStrength strength)
        => Task.FromResult(BiometricHwStatus.Unsupported);

    public Task<BiometricType[]> GetEnrolledBiometricTypesAsync()
        => Task.FromResult(Array.Empty<BiometricType>());

    public bool IsPlatformSupported => true;
}
