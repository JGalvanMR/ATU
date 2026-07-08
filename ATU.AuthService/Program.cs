using ATU.AuditService;
using ATU.AuthService;
using ATU.Shared;
using ATU.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers ──────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── SignalR ───────────────────────────────────────────────────────────────────
// AuditHub vive en ATU.AuditService pero lo reutilizamos aquí para IHubContext
builder.Services.AddSignalR();

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ── Servicios ATU ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IZoneRepository, ZoneRepository>();
builder.Services.AddSingleton<IGeofenceService, GeofenceService>();
builder.Services.AddSingleton<IOtpRepository, InMemoryOtpRepository>();

// ✅ CORRECCIÓN: Eliminado InMemoryDeviceRepository. Solo usamos SQL.
builder.Services.AddSingleton<IDeviceRepository, SqlDeviceRepository>();

builder.Services.AddSingleton<IAuditEventPublisher, InMemoryAuditPublisher>();
builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();

// ── App ───────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// El orden importa: CORS debe ir antes que los Hubs y Controladores
app.UseCors("AllowAll");
app.UseAuthorization();

// Mapear AuditHub para que IHubContext<AuditHub> funcione en OTPController
// y para que la PWA se conecte a http://localhost:5000/audit-hub
app.MapHub<AuditHub>("/audit-hub");
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();

// ── Implementaciones internas ─────────────────────────────────────────────────

public class InMemoryAuditPublisher : IAuditEventPublisher
{
    public Task PublishAsync(AuditEvent evt)
    {
        // Esto solo escribe en la consola del servidor donde corre AuthService.
        // Los eventos en tiempo real hacia la PWA los manda directamente 
        // el OTPController usando IHubContext<AuditHub>.
        Console.WriteLine($"[AUDIT DB LOG] {evt.Type}: {evt.BatchId} — {evt.Message}");
        return Task.CompletedTask;
    }
}