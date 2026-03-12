using ATU.AuditService;
using ATU.AuthService;
using ATU.AuthService.Controllers;
using ATU.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ?? Servicios Core ????????????????????????????????????????????????????????????
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ?? SignalR para Dashboard en Tiempo Real ?????????????????????????????????????
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 32 * 1024; // 32KB
}).AddJsonProtocol();

// ?? Autenticación JWT ?????????????????????????????????????????????????????????
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Configurar con Azure AD / Auth0 / Keycloak en producción
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];

        // Soporte para JWT en SignalR (via query string)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlantManager", p => p.RequireRole("PlantManager"));
    options.AddPolicy("Supervisor", p => p.RequireRole("Supervisor", "PlantManager"));
    options.AddPolicy("Operator", p => p.RequireRole("Operator", "Supervisor", "PlantManager"));
});

// ?? Rate Limiting (anti-brute force) ?????????????????????????????????????????
builder.Services.AddRateLimiter(options =>
{
    // Máx 5 intentos de validación por minuto por IP
    options.AddFixedWindowLimiter("OTPValidation", config =>
    {
        config.Window = TimeSpan.FromMinutes(1);
        config.PermitLimit = 5;
        config.QueueLimit = 0;
    });

    // Máx 10 generaciones de OTP por hora por operador
    options.AddSlidingWindowLimiter("OTPGeneration", config =>
    {
        config.Window = TimeSpan.FromHours(1);
        config.PermitLimit = 10;
        config.SegmentsPerWindow = 6;
    });
});

// ?? CORS para PWA ?????????????????????????????????????????????????????????????
builder.Services.AddCors(options =>
{
    options.AddPolicy("PWAPolicy", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                ?? ["https://atu.empresa.com"])
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Necesario para SignalR
    });
});

// ?? Servicios de Dominio (DI) ?????????????????????????????????????????????????
builder.Services.AddScoped<DeviceEnrollmentService>();
builder.Services.AddScoped<GeofenceService>();
builder.Services.AddScoped<AuditEventPublisher>();

// En producción, implementar con EF Core + SQL Server / PostgreSQL
builder.Services.AddScoped<IDeviceRepository, /* EfDeviceRepository */ MockDeviceRepository>();
builder.Services.AddScoped<IOTPRepository, /* EfOTPRepository */ MockOTPRepository>();
builder.Services.AddScoped<IAuditEventRepository, /* EfAuditRepository */ MockAuditRepository>();
builder.Services.AddScoped<IZoneRepository, /* EfZoneRepository */ MockZoneRepository>();
builder.Services.AddScoped<IEncryptionService, AesEncryptionService>();
builder.Services.AddScoped<IGeofenceService, GeofenceService>();
builder.Services.AddScoped<IAuditEventPublisher, AuditEventPublisher>();

// ?? Health Checks ?????????????????????????????????????????????????????????????
builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")!, name: "database")
    .AddCheck("atu-core", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

var app = builder.Build();

// ?? Pipeline ??????????????????????????????????????????????????????????????????
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("PWAPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Hub de SignalR para el dashboard de auditoría
app.MapHub<AuditHub>("/hubs/audit");

app.MapHealthChecks("/health");

// ?? Service Worker para PWA ???????????????????????????????????????????????????
app.UseStaticFiles();
app.MapFallbackToFile("index.html"); // SPA fallback para la PWA

app.Run();

// ?? Implementaciones Mock (reemplazar con EF Core en producción) ??????????????

class MockDeviceRepository : IDeviceRepository
{
    private static readonly List<EnrolledDevice> _devices = [];
    public Task<EnrolledDevice?> GetActiveByOperatorAsync(string operatorId)
        => Task.FromResult(_devices.FirstOrDefault(d => d.OperatorId == operatorId && d.IsActive));
    public Task AddAsync(EnrolledDevice device) { _devices.Add(device); return Task.CompletedTask; }
    public Task UpdateAsync(EnrolledDevice device) => Task.CompletedTask;
}

class MockOTPRepository : IOTPRepository
{
    private static readonly List<OTPRecord> _records = [];
    public Task SaveAsync(OTPRecord record) { _records.Add(record); return Task.CompletedTask; }
    public Task<OTPRecord?> GetByBatchAndSupervisorAsync(string batchId, string supervisorId)
        => Task.FromResult(_records.FirstOrDefault(r => r.BatchId == batchId && r.SupervisorId == supervisorId));
    public Task UpdateAsync(OTPRecord record) => Task.CompletedTask;
}

class MockAuditRepository : IAuditEventRepository
{
    private static readonly List<AuditEvent> _events = [];
    public Task SaveAsync(AuditEvent evt) { _events.Add(evt); return Task.CompletedTask; }
    public Task<IEnumerable<AuditEvent>> GetRecentAsync(int count = 100)
        => Task.FromResult(_events.TakeLast(count).AsEnumerable());
    public Task<IEnumerable<AuditEvent>> GetBySupervisorAsync(string supervisorId, DateOnly date)
        => Task.FromResult(_events.Where(e => e.SupervisorId == supervisorId).AsEnumerable());
}

class MockZoneRepository : IZoneRepository
{
    public Task<LoadingZone?> GetByIdAsync(string zoneId)
        => Task.FromResult<LoadingZone?>(new LoadingZone
        {
            Id = zoneId,
            Name = "Zona Embarque Cámara 1",
            BoundaryPoints = [
                new(20.6721, -103.3475),
                new(20.6725, -103.3475),
                new(20.6725, -103.3470),
                new(20.6721, -103.3470)
            ]
        });
}

class AesEncryptionService : IEncryptionService
{
    // En producción: usar Azure Key Vault / AWS KMS
    private readonly byte[] _key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public string Encrypt(string plaintext)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        aes.IV.CopyTo(result, 0);
        cipherBytes.CopyTo(result, aes.IV.Length);
        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _key;
        aes.IV = data[..16];
        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(data, 16, data.Length - 16);
        return System.Text.Encoding.UTF8.GetString(decryptedBytes);
    }
}
