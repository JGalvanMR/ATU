using ATU.PWA.Hubs;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<AuditHub>("/hubs/audit");

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

app.Run();