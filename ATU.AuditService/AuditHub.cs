using Microsoft.AspNetCore.SignalR;
using ATU.AuthService;

namespace ATU.AuditService;

/// <summary>
/// Hub SignalR para el Dashboard de Auditoría en Tiempo Real.
/// El Gerente de Planta ve TODAS las autorizaciones en este preciso momento.
/// 
/// Grupos de conexión:
///   - "PlantManagers"  → reciben todos los eventos
///   - "Supervisors"    → reciben solo sus eventos
///   - "Operators"      → reciben solo sus eventos
/// </summary>
public class AuditHub : Hub
{
    public async Task JoinAsManager()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "PlantManagers");
        await Clients.Caller.SendAsync("Connected", new
        {
            role = "PlantManager",
            message = "Dashboard de auditoría activo. Recibiendo eventos en tiempo real."
        });
    }

    public async Task JoinAsSupervisor(string supervisorId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Supervisor_{supervisorId}");
        await Clients.Caller.SendAsync("Connected", new { role = "Supervisor", supervisorId });
    }

    public async Task JoinAsOperator(string operatorId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Operator_{operatorId}");
        await Clients.Caller.SendAsync("Connected", new { role = "Operator", operatorId });
    }
}

/// <summary>
/// Servicio que publica eventos de auditoría al hub SignalR y los persiste.
/// </summary>
public class AuditEventPublisher(
    IHubContext<AuditHub> hubContext,
    IAuditEventRepository auditRepo) : IAuditEventPublisher
{
    public async Task PublishAsync(AuditEvent evt)
    {
        // 1. Persistir en base de datos (inmutable, para forensics)
        await auditRepo.SaveAsync(evt);

        // 2. Determinar payload para el semáforo
        var semaphorePayload = new SemaphoreEvent
        {
            EventId = evt.Id,
            Timestamp = evt.Timestamp,
            Status = MapToSemaphoreStatus(evt),
            BadgeColor = GetBadgeColor(evt),
            Title = GetEventTitle(evt),
            Description = evt.Message,
            OperatorId = evt.OperatorId,
            SupervisorId = evt.SupervisorId,
            BatchId = evt.BatchId,
            IsCritical = evt.Severity >= AlertSeverity.High
        };

        // 3. Enviar a gerentes de planta (todos los eventos)
        await hubContext.Clients
            .Group("PlantManagers")
            .SendAsync("AuditEvent", semaphorePayload);

        // 4. Si es crítico, disparar alerta de fraude separada
        if (evt.Severity == AlertSeverity.Critical)
        {
            await hubContext.Clients
                .Group("PlantManagers")
                .SendAsync("FraudAlert", new FraudAlert
                {
                    EventId = evt.Id,
                    Timestamp = evt.Timestamp,
                    Message = evt.Message,
                    SupervisorId = evt.SupervisorId,
                    BatchId = evt.BatchId,
                    RequiresImmediateAction = true
                });
        }

        // 5. Notificar también al supervisor involucrado
        await hubContext.Clients
            .Group($"Supervisor_{evt.SupervisorId}")
            .SendAsync("AuthorizationUpdate", semaphorePayload);
    }

    private static string MapToSemaphoreStatus(AuditEvent evt) => evt.EventType switch
    {
        AuditEventType.OTPValidated => "green",
        AuditEventType.OTPExpired => "yellow",
        AuditEventType.OTPGenerated => "blue",
        AuditEventType.FraudAttempt => "red",
        AuditEventType.ReplayAttempt => "red",
        AuditEventType.GeofenceViolation => "red",
        _ => "gray"
    };

    private static string GetBadgeColor(AuditEvent evt) => evt.Severity switch
    {
        AlertSeverity.Critical => "#FF3B3B",
        AlertSeverity.High => "#FF9500",
        AlertSeverity.Warning => "#FFCC00",
        _ => "#34C759"
    };

    private static string GetEventTitle(AuditEvent evt) => evt.EventType switch
    {
        AuditEventType.OTPGenerated => "🔑 OTP Generado",
        AuditEventType.OTPValidated => "✅ Salida Autorizada",
        AuditEventType.OTPExpired => "⏱️ Código Expirado",
        AuditEventType.FraudAttempt => "🚨 INTENTO DE FRAUDE",
        AuditEventType.ReplayAttempt => "🚨 REUTILIZACIÓN DE CÓDIGO",
        AuditEventType.GeofenceViolation => "📍 VIOLACIÓN DE GEOFENCE",
        AuditEventType.DeviceEnrolled => "📱 Dispositivo Enrolado",
        AuditEventType.DeviceRevoked => "⛔ Dispositivo Revocado",
        _ => "ℹ️ Evento"
    };
}

public record SemaphoreEvent
{
    public Guid EventId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string Status { get; init; }      // green | yellow | red | blue
    public required string BadgeColor { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string OperatorId { get; init; }
    public required string SupervisorId { get; init; }
    public required string BatchId { get; init; }
    public bool IsCritical { get; init; }
}

public record FraudAlert
{
    public Guid EventId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string Message { get; init; }
    public required string SupervisorId { get; init; }
    public required string BatchId { get; init; }
    public bool RequiresImmediateAction { get; init; }
}

public interface IAuditEventRepository
{
    Task SaveAsync(AuditEvent evt);
    Task<IEnumerable<AuditEvent>> GetRecentAsync(int count = 100);
    Task<IEnumerable<AuditEvent>> GetBySupervisorAsync(string supervisorId, DateOnly date);
}
