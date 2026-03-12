using System.Security.Cryptography;
using System.Text;
using ATU.AuthService;
using ATU.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IDeviceRepository, InMemoryDeviceRepository>();
builder.Services.AddSingleton<IOtpRepository, InMemoryOtpRepository>();
builder.Services.AddSingleton<IEncryptionService, EphemeralEncryptionService>();
builder.Services.AddSingleton<DeviceEnrollmentService>();
builder.Services.AddSingleton<IGeofenceService, GeofenceService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapOtpEndpoints();
app.Run();

internal sealed class InMemoryDeviceRepository : IDeviceRepository
{
    private readonly List<EnrolledDevice> _devices = [];

    public Task<EnrolledDevice?> GetActiveByOperatorAsync(string operatorId)
        => Task.FromResult(_devices.LastOrDefault(d => d.OperatorId == operatorId && d.IsActive));

    public Task AddAsync(EnrolledDevice device)
    {
        _devices.Add(device);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(EnrolledDevice device) => Task.CompletedTask;
}

internal sealed class InMemoryOtpRepository : IOtpRepository
{
    private readonly List<OtpRecord> _records = [];

    public Task Save(OtpRecord record)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<OtpRecord?> GetLatest(string batchId, string supervisorId)
        => Task.FromResult(_records.LastOrDefault(r => r.BatchId == batchId && r.SupervisorId == supervisorId));

    public Task Update(OtpRecord record) => Task.CompletedTask;
}

internal sealed class EphemeralEncryptionService : IEncryptionService
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var clearBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(clearBytes, 0, clearBytes.Length);

        var output = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, output, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, output, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = data[..16];
        var cipher = data[16..];
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var clearBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(clearBytes);
    }
}
