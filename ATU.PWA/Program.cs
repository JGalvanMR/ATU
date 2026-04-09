using ATU.PWA.Hubs;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

//app.MapHub<AuditHub>("/hubs/audit");
app.MapHub<AuditHub>("/audit-hub");

app.MapGet("/test-event", async (IHubContext<AuditHub> hub) =>
{
    var evt = new
    {
        status = "green",
        title = "✅ Autorización Real",
        batchId = "LOT-REAL-001",
        supervisorId = "SUP-Galvan",
        operatorId = "OP-Test",
        message = "Evento enviado desde backend",
        timestamp = DateTime.UtcNow
    };

    await hub.Clients.All.SendAsync("AuditEvent", evt);

    return Results.Ok("Evento enviado");
});

app.MapGet("/test-event/{status}", async (string status, IHubContext<AuditHub> hub) =>
{
    // Validar status permitido
    var validStatuses = new[] { "green", "yellow", "red", "blue" };
    if (!validStatuses.Contains(status.ToLower()))
    {
        return Results.BadRequest(new
        {
            error = "Status inválido",
            validStatuses = validStatuses
        });
    }

    var evt = CreateTestEvent(status.ToLower());

    await hub.Clients.All.SendAsync("AuditEvent", evt);

    return Results.Ok(new
    {
        message = $"Evento '{status}' enviado",
        eventData = evt
    });
});

// Endpoint para enviar todos los eventos de prueba secuencialmente
app.MapGet("/test-all-events", async (IHubContext<AuditHub> hub) =>
{
    var statuses = new[] { "blue", "green", "yellow", "red" };
    var results = new List<object>();

    foreach (var status in statuses)
    {
        var evt = CreateTestEvent(status);
        await hub.Clients.All.SendAsync("AuditEvent", evt);
        results.Add(evt);

        // Pequeña pausa para visualizar en el dashboard
        await Task.Delay(500);
    }

    return Results.Ok(new
    {
        message = "Todos los eventos enviados",
        events = results
    });
});

// Endpoint para simular escenario completo FIFO
app.MapGet("/test-fifo-scenario", async (IHubContext<AuditHub> hub) =>
{
    // 1. OTP Generado (cámara fría)
    var otpGenerated = CreateTestEvent("blue", new
    {
        title = "🔑 OTP Generado",
        batchId = "PROD-A-150324",
        message = "Juan generó OTP para producto que caduca 15/03",
        expirationDate = "15/03/2024",
        daysToExpire = 2
    });
    await hub.Clients.All.SendAsync("AuditEvent", otpGenerated);
    await Task.Delay(1000);

    // 2. Autorización válida (coinciden productos)
    var authorized = CreateTestEvent("green", new
    {
        title = "✅ Salida Autorizada",
        batchId = "PROD-A-150324",
        message = "Pedro escaneó producto correcto. FIFO respetado.",
        otpUsed = "847291",
        validatedBy = "SUP-Galvan"
    });
    await hub.Clients.All.SendAsync("AuditEvent", authorized);
    await Task.Delay(1000);

    // 3. OTP Expirado (intento tardío)
    var expired = CreateTestEvent("yellow", new
    {
        title = "⏱️ Código Expirado",
        batchId = "PROD-B-200324",
        message = "Pedro intentó usar OTP de hace 5 minutos",
        otpAge = "300 segundos"
    });
    await hub.Clients.All.SendAsync("AuditEvent", expired);
    await Task.Delay(1000);

    // 4. FRAUDE - Producto diferente
    var fraud = CreateTestEvent("red", new
    {
        title = "🚨 INTENTO DE FRAUDE",
        batchId = "PROD-B-200324",
        message = "FRAUDE: Código vinculado a PROD-A-150324 usado en PROD-B-200324",
        claimedProduct = "PROD-A-150324",
        actualProduct = "PROD-B-200324",
        isFraud = true
    });
    await hub.Clients.All.SendAsync("AuditEvent", fraud);
    await hub.Clients.All.SendAsync("FraudAlert", new
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        Message = "Intento de fraude detectado: Producto no coincide con OTP",
        SupervisorId = "PEDRO-EMBARQUES",
        BatchId = "PROD-B-200324",
        RequiresImmediateAction = true
    });

    return Results.Ok(new
    {
        scenario = "FIFO Fraud Scenario",
        events = new[] { "OTP Generated", "Authorized", "Expired", "FRAUD" }
    });
});

// Helper para crear eventos
static object CreateTestEvent(string status, object? overrides = null)
{
    var baseEvent = new
    {
        status = status,
        title = status switch
        {
            "green" => "✅ Salida Autorizada",
            "yellow" => "⏱️ Código Expirado",
            "red" => "🚨 INTENTO DE FRAUDE",
            "blue" => "🔑 OTP Generado",
            _ => "ℹ️ Evento"
        },
        batchId = "LOT-TEST-001",
        supervisorId = "SUP-Test",
        operatorId = "OP-Test",
        message = status switch
        {
            "green" => "Autorización válida. Despacho aprobado.",
            "yellow" => "Código expirado. Solicite uno nuevo.",
            "red" => "FRAUDE: Código vinculado a otro lote.",
            "blue" => "OTP generado. Esperando validación.",
            _ => "Evento de prueba"
        },
        timestamp = DateTime.UtcNow,
        isFraud = status == "red",
        eventId = Guid.NewGuid().ToString()
    };

    if (overrides == null) return baseEvent;

    // Merge con overrides usando reflection simple
    var dict = new Dictionary<string, object?>();
    foreach (var prop in baseEvent.GetType().GetProperties())
        dict[prop.Name] = prop.GetValue(baseEvent);

    foreach (var prop in overrides.GetType().GetProperties())
        dict[prop.Name] = prop.GetValue(overrides);

    return dict;
}

app.Run();