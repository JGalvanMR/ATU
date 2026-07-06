using System;

namespace ATU.CamaraFria.Services;

/// <summary>
///     Contrato del servicio de temas. Expone el tema efectivo actual y un evento
///     que se dispara cada vez que el sistema operativo (o el usuario) cambia la
///     preferencia de apariencia.
/// </summary>
public interface IThemeService
{
    /// <summary>Tema efectivo que la app está usando ahora mismo.</summary>
    AppTheme CurrentTheme { get; }

    /// <summary>True cuando el tema efectivo es oscuro.</summary>
    bool IsDark { get; }

    /// <summary>
    ///     Se dispara cuando el sistema cambia su preferencia (o cuando el usuario
    ///     la cambia programáticamente). Las páginas se suscriben para refrescar
    ///     lógica dependiente del tema (p. ej. colores en ViewModels).
    /// </summary>
    event EventHandler<AppTheme> ThemeChanged;

    /// <summary>
    ///     Aplica un tema explícito. <see cref="AppTheme.Unspecified"/> delega en
    ///     el sistema operativo.
    /// </summary>
    void ApplyTheme(AppTheme theme);

    /// <summary>Alterna entre Light y Dark sin tocar el ajuste del sistema.</summary>
    void ToggleTheme();

    /// <summary>Restituye el seguimiento automático al tema del sistema.</summary>
    void FollowSystemTheme();
}