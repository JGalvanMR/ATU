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
                OperatorId = "12345", // ID de prueba que usarás en la app
                Fingerprint = "test-fingerprint",
                // SIN encriptación para la prueba de conectividad
                EncryptedSecret = Convert.ToBase64String(Encoding.UTF8.GetBytes("SUPER_SECRET_KEY_32_CHARS!!")),
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
        Console.WriteLine($"[AUDIT] {evt.Type}: {evt.BatchId} - {evt.Message}");
        return Task.CompletedTask;
    }
}

// Encripción temporal con llave ESTÁTICA para que no explote al reiniciar
public class AesEncryptionService : IEncryptionService
{
    // Llave estática fija de 32 bytes
    private static readonly byte[] _key = Encoding.UTF8.GetBytes("SUPER_SECRET_KEY_32_CHARS!!");

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
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
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = data.Take(16).ToArray();
        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(data, 16, data.Length - 16);
        return Encoding.UTF8.GetString(decrypted);
    }
}