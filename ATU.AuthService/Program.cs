using ATU.AuthService;
using ATU.Shared;
using ATU.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ==========================================
// CORS: Permitir que el celular acceda
// ==========================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalNetwork", policy =>
    {
        policy.AllowAnyOrigin()    // En desarrollo, permitir cualquier IP de la red local
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Servicios ATU
builder.Services.AddSingleton<IZoneRepository, ZoneRepository>();
builder.Services.AddSingleton<IGeofenceService, GeofenceService>();
builder.Services.AddSingleton<IOtpRepository, InMemoryOtpRepository>();
builder.Services.AddSingleton<IDeviceRepository, InMemoryDeviceRepository>();
builder.Services.AddSingleton<IAuditEventPublisher, InMemoryAuditPublisher>();
builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// IMPORTANTE: CORS debe ir antes que los controllers
app.UseCors("AllowLocalNetwork");

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));

app.Run();

// ============================================================================
// IMPLEMENTACIONES IN MEMORY
// ============================================================================

public class InMemoryAuditPublisher : IAuditEventPublisher
{
    public Task PublishAsync(AuditEvent evt)
    {
        Console.WriteLine($"[AUDIT] {evt.Type}: {evt.BatchId} - {evt.Message}");
        return Task.CompletedTask;
    }
}