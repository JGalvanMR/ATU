using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Maui.Storage;

namespace ATU.CamaraFria.Services;

/// <summary>
/// Escucha eventos del backend ATU via SignalR.
/// Se inicia al arrancar la app y permanece activo en segundo plano.
/// </summary>
public class SignalRListenerService
{
    private HubConnection? _connection;
    private readonly string _baseUrl;

    public event Action<FolioAdelantatadoNotification>? NuevoFolioRecibido;
    public event Action<string>? ConexionCambiada;

    public bool EstaConectado => _connection?.State == HubConnectionState.Connected;

    public SignalRListenerService()
    {
        _baseUrl = Preferences.Get("SERVER_URL", "http://192.168.123.244:83/audit");
    }

    public async Task IniciarAsync()
    {
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/audit-hub")
                .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2),
                                                TimeSpan.FromSeconds(5),
                                                TimeSpan.FromSeconds(10) })
                .Build();

            // Evento: nuevo folio adelantado detectado por CargaEmbarques
            _connection.On<FolioAdelantatadoNotification>("NuevoFolioAdelantado", notif =>
            {
                Console.WriteLine($"[ATU SignalR] Folio adelantado recibido: {notif.EmbFolio}");
                MainThread.BeginInvokeOnMainThread(() => NuevoFolioRecibido?.Invoke(notif));
            });

            _connection.Reconnecting += _ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ConexionCambiada?.Invoke("Reconectando..."));
                return Task.CompletedTask;
            };

            _connection.Reconnected += _ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ConexionCambiada?.Invoke("Conectado"));
                return Task.CompletedTask;
            };

            _connection.Closed += _ =>
            {
                MainThread.BeginInvokeOnMainThread(() => ConexionCambiada?.Invoke("Desconectado"));
                return Task.CompletedTask;
            };

            await _connection.StartAsync();

            // Unirse al grupo de supervisores de cámaras frías
            await _connection.InvokeAsync("SubscribeToAuditFeed");

            Console.WriteLine("[ATU SignalR] Conectado al hub de auditoría");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ATU SignalR] Error al conectar: {ex.Message}");
        }
    }

    public async Task DetenerAsync()
    {
        if (_connection != null)
            await _connection.StopAsync();
    }
}

public class FolioAdelantatadoNotification
{
    public string EmbFolio { get; set; } = string.Empty;
    public string ProdClave { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string ReciboSug { get; set; } = string.Empty;
    public string TarimaSug { get; set; } = string.Empty;
    public string Responsable { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
}
