using ATU.Shared;
using ATU.Shared.Models;
using ATU.AuthService.Controllers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

app.UseHttpsRedirection();
app.UseAuthorization();

// Mapear Controllers
app.MapControllers();

// Endpoints adicionales de prueba (Minimal API)
app.MapGet("/api/geofence/test/{zoneId}", async (
    string zoneId,
    double lat,
    double lon,
    IGeofenceService geofence) =>
{
    var result = await geofence.ValidateAsync(zoneId, lat, lon);
    return Results.Ok(new
    {
        result.IsValid,
        result.Message,
        result.DistanceMeters
    });
})
.WithName("TestGeofence")
.WithOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "OK", timestamp = DateTime.UtcNow }));

app.Run();

// ============================================================================
// IMPLEMENTACIONES
// ============================================================================

public class ZoneRepository : IZoneRepository
{
    private static readonly Dictionary<string, LoadingZone> _zones = new()
    {
        ["CAMARA-FRIA-PRINCIPAL"] = new LoadingZone
        {
            Id = "CAMARA-FRIA-PRINCIPAL",
            Name = "Cámara Fría Principal",
            BoundaryPoints = new[]
            {
                new GeoPoint(20.6721, -103.3475),
                new GeoPoint(20.6725, -103.3475),
                new GeoPoint(20.6725, -103.3470),
                new GeoPoint(20.6721, -103.3470)
            }
        }
    };

    public Task<LoadingZone?> GetByIdAsync(string zoneId)
    {
        _zones.TryGetValue(zoneId.ToUpperInvariant(), out var zone);
        return Task.FromResult(zone);
    }
}

public class GeofenceService : IGeofenceService
{
    public Task<GeofenceResult> ValidateAsync(string zoneId, double latitude, double longitude)
    {
        return Task.FromResult(new GeofenceResult(
            isValid: true,
            distanceMeters: 0,
            message: $"Zona {zoneId} válida"));
    }
}

public class InMemoryOtpRepository : IOtpRepository
{
    private static readonly List<OtpRecord> _records = new();
    public Task SaveAsync(OtpRecord record) { _records.Add(record); return Task.CompletedTask; }
    public Task<OtpRecord?> GetByBatchAndSupervisorAsync(string batchId, string supervisorId)
        => Task.FromResult(_records.FirstOrDefault(r => r.BatchId == batchId && r.SupervisorId == supervisorId && !r.IsUsed));
    public Task UpdateAsync(OtpRecord record) => Task.CompletedTask;
    public Task<OtpRecord?> GetByOtpAsync(string otp)
        => Task.FromResult(_records.FirstOrDefault(r => r.Otp == otp && !r.IsUsed));
}

public class InMemoryDeviceRepository : IDeviceRepository
{
    private static readonly List<EnrolledDevice> _devices = new();

    public InMemoryDeviceRepository()
    {
        if (_devices.Count == 0)
        {
            _devices.Add(new EnrolledDevice
            {
                Id = Guid.NewGuid(),
                OperatorId = "SUP-CAMARAS-001",
                Fingerprint = "test-fingerprint",
                EncryptedSecret = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-secret-32-chars-long!!")),
                PushToken = "",
                ColdStorageZoneId = "CAMARA-FRIA-PRINCIPAL",
                IsActive = true
            });
        }
    }

    public Task<EnrolledDevice?> GetByIdAsync(string deviceId)
        => Task.FromResult(_devices.FirstOrDefault(d => d.OperatorId == deviceId && d.IsActive));

    public Task<EnrolledDevice?> GetActiveByOperatorAsync(string operatorId)
        => Task.FromResult(_devices.FirstOrDefault(d => d.OperatorId == operatorId && d.IsActive));

    public Task AddAsync(EnrolledDevice device) { _devices.Add(device); return Task.CompletedTask; }
    public Task UpdateAsync(EnrolledDevice device) => Task.CompletedTask;
}

public class InMemoryAuditPublisher : IAuditEventPublisher
{
    public Task PublishAsync(AuditEvent evt)
    {
        Console.WriteLine($"[AUDIT] {evt.Type}: {evt.BatchId}");
        return Task.CompletedTask;
    }
}

public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Encrypt(string plaintext)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        var iv = aes.IV;
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var result = new byte[iv.Length + cipherBytes.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, iv.Length, cipherBytes.Length);
        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _key;
        aes.IV = data.Take(16).ToArray();
        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(data, 16, data.Length - 16);
        return Encoding.UTF8.GetString(decrypted);
    }
}