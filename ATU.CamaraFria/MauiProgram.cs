using ATU.CamaraFria.Services;
using ATU.CamaraFria.ViewModels;
using ATU.CamaraFria.Views;
using Plugin.Maui.Biometric;
using ZXing.Net.Maui; // NECESARIO para UseBarcodeReader

namespace ATU.CamaraFria;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
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

        // Biometría (La clase concreta se llama 'Biometric')
        builder.Services.AddSingleton<IBiometric, Biometric>();
        builder.Services.AddSingleton<BiometricService>();

        builder.Services.AddSingleton<IScannerService, ScannerService>();

        // ViewModels
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<OTPViewModel>();

        // Páginas
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<OTPDisplayPage>();

        return builder.Build();
    }
}