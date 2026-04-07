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
    options.AddPolicy("AllowLocalNetwork", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ── Servicios ATU ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IZoneRepository, ZoneRepository>();
builder.Services.AddSingleton<IGeofenceService, GeofenceService>();
builder.Services.AddSingleton<IOtpRepository, InMemoryOtpRepository>();
builder.Services.AddSingleton<IDeviceRepository, InMemoryDeviceRepository>();
builder.Services.AddSingleton<IAuditEventPublisher, InMemoryAuditPublisher>();
builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();

// ── App ───────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowLocalNetwork");
app.UseAuthorization();

// Mapear AuditHub para que IHubContext<AuditHub> funcione en OTPController
app.MapHub<AuditHub>("/audit-hub");
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();

// ── Implementaciones internas ─────────────────────────────────────────────────

public class InMemoryAuditPublisher : IAuditEventPublisher
{
    public Task PublishAsync(AuditEvent evt)
    {
        Console.WriteLine($"[AUDIT] {evt.Type}: {evt.BatchId} — {evt.Message}");
        return Task.CompletedTask;
    }
}