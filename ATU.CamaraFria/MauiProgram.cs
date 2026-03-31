using System;
using System.Threading;
using System.Threading.Tasks;
using ATU.CamaraFria.Services;
using ATU.CamaraFria.ViewModels;
using ATU.CamaraFria.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Plugin.Maui.Biometric;

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
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // === INYECCIÓN DE DEPENDENCIAS ===

            builder.Services.AddSingleton<SyncDbContext>();
            builder.Services.AddSingleton<SyncQueueService>();
            builder.Services.AddSingleton<IDeviceFingerprintService, DeviceFingerprintService>();
            builder.Services.AddSingleton<ATUApiClient>();

            builder.Services.AddSingleton<IBiometric, MockBiometric>();
            builder.Services.AddSingleton<BiometricService>();

            builder.Services.AddSingleton<IScannerService, ScannerService>();

            // ViewModels
            builder.Services.AddTransient<LoginViewModel>();
            builder.Services.AddTransient<MainViewModel>();
            builder.Services.AddTransient<OTPViewModel>();

            // Páginas
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<OTPDisplayPage>();
            builder.Services.AddTransient<PendingSyncPage>();
            builder.Services.AddTransient<SettingsPage>();

            return builder.Build();
        }
        catch (Exception ex)
        {
            // Si falla la creación del builder, esto escribirá en el log de Android
            Console.WriteLine($"🔥 FATAL EN MAUIPROGRAM: {ex.Message}\n{ex.StackTrace}");
            throw; // Lanzarlo para que lo atrape App.xaml.cs
        }
    }
}

public class MockBiometric : IBiometric
{
    public Task<AuthenticationResponse> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AuthenticationResponse());
    }

    public Task<BiometricHwStatus> GetAuthenticationStatusAsync(AuthenticatorStrength strength)
    {
        return Task.FromResult(BiometricHwStatus.Unsupported);
    }

    public Task<BiometricType[]> GetEnrolledBiometricTypesAsync()
    {
        return Task.FromResult(Array.Empty<BiometricType>());
    }

    public bool IsPlatformSupported => true;
}