using System;
using Microsoft.Maui.Controls;

namespace ATU.CamaraFria.Services;

/// <summary>
///     Servicio singleton que sincroniza el tema de la app con el del sistema.
///     Se suscribe a <see cref="Application.RequestedThemeChanged"/>, que MAUI
///     dispara automáticamente cuando iOS/Android notifican un cambio de
///     apariencia. Cualquier recurso declarado con AppThemeBinding en XAML se
///     reevalúa sin intervención manual.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly Lazy<ThemeService> _lazy = new(() => new ThemeService());

    public static ThemeService Instance => _lazy.Value;

    private ThemeService()
    {
        // Nos enganchamos al evento de la Application. Lo hacemos en cuanto la
        // plataforma cree la Application (lo normal: durante App.xaml.cs).
        // Usamos un hook perezoso para sobrevivir a pruebas unitarias donde la
        // app aún no existe.
        HookSystemTheme();
    }

    public AppTheme CurrentTheme =>
        Application.Current?.RequestedTheme ?? AppTheme.Unspecified;

    public bool IsDark => CurrentTheme == AppTheme.Dark;

    public event EventHandler<AppTheme>? ThemeChanged;

    public void ApplyTheme(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null) return;

        // AppTheme.Unspecified => sigue al sistema. Light/Dark => override.
        app.UserAppTheme = theme;

        RaiseThemeChanged(app.RequestedTheme);
    }

    public void ToggleTheme()
    {
        ApplyTheme(IsDark ? AppTheme.Light : AppTheme.Dark);
    }

    public void FollowSystemTheme() => ApplyTheme(AppTheme.Unspecified);

    /// <summary>
    ///     Suscribe UNA sola vez el handler al evento global. Si el servicio se
    ///     construye antes que la Application (típico en tests), reintenta.
    /// </summary>
    private void HookSystemTheme()
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += OnSystemThemeChanged;
            return;
        }

        // No existe Application.CurrentChanged en MAUI.
        // Alternativa: reintentar periódicamente hasta que Application.Current esté disponible.
        Device.StartTimer(TimeSpan.FromMilliseconds(100), () =>
        {
            if (Application.Current is not null)
            {
                Application.Current.RequestedThemeChanged -= OnSystemThemeChanged;
                Application.Current.RequestedThemeChanged += OnSystemThemeChanged;
                return false; // Detener el temporizador.
            }
            return true; // Seguir esperando.
        });
    }

    private void OnSystemThemeChanged(object? sender, AppThemeChangedEventArgs e)
        => RaiseThemeChanged(e.RequestedTheme);

    private void RaiseThemeChanged(AppTheme theme)
    {
        try
        {
            ThemeChanged?.Invoke(this, theme);
        }
        catch (Exception ex)
        {
            // Nunca propagues excepciones desde un evento global; podrías
            // dejar a la app en estado roto.
            System.Diagnostics.Debug.WriteLine($"[ThemeService] subscriber threw: {ex}");
        }
    }
}