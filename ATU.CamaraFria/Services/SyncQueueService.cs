using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ATU.CamaraFria.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ATU.CamaraFria.Services;

public class SyncDbContext : DbContext
{
    public DbSet<PendingSync> PendingSyncs { get; set; }

    private readonly string _dbPath;

    public SyncDbContext()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "atu_sync.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PendingSync>()
            .HasIndex(p => p.Status);

        modelBuilder.Entity<PendingSync>()
            .HasIndex(p => p.CreatedAt);
    }
}

public class SyncQueueService
{
    private readonly SyncDbContext _db;
    private readonly ILogger<SyncQueueService> _logger;
    private readonly ATUApiClient _apiClient;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public SyncQueueService(
        SyncDbContext db,
        ILogger<SyncQueueService> logger,
        ATUApiClient apiClient)
    {
        _db = db;
        _logger = logger;
        _apiClient = apiClient;

        _db.Database.EnsureCreated();
    }

    public async Task<int> EnqueueAsync<T>(SyncType type, T payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);

        var item = new PendingSync
        {
            Type = type,
            Payload = json,
            Status = SyncStatus.Pending,
            RetryCount = 0
        };

        _db.PendingSyncs.Add(item);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Elemento agregado a cola: {Type} - ID: {Id}", type, item.Id);

        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            _ = Task.Run(() => ProcessQueueAsync());
        }

        return item.Id;
    }

    public async Task<(int Processed, int Failed)> ProcessQueueAsync()
    {
        if (!await _syncLock.WaitAsync(0))
        {
            return (0, 0);
        }

        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                _logger.LogWarning("Sin conectividad - no se procesa la cola");
                return (0, 0);
            }

            var pending = await _db.PendingSyncs
                .Where(p => p.Status == SyncStatus.Pending && p.RetryCount < 5)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync();

            int processed = 0;
            int failed = 0;

            foreach (var item in pending)
            {
                item.Status = SyncStatus.InProgress;
                item.LastAttemptAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                try
                {
                    var success = await ProcessItemAsync(item);

                    if (success)
                    {
                        item.Status = SyncStatus.Completed;
                        item.SyncedAt = DateTime.UtcNow;
                        processed++;
                    }
                    else
                    {
                        item.Status = SyncStatus.Failed;
                        item.RetryCount++;
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    item.Status = SyncStatus.Failed;
                    item.RetryCount++;
                    item.ErrorMessage = ex.Message;
                    failed++;

                    _logger.LogError(ex, "Error procesando item {Id}", item.Id);
                }

                await _db.SaveChangesAsync();
            }

            var oldCompleted = await _db.PendingSyncs
                .Where(p => p.Status == SyncStatus.Completed && p.SyncedAt < DateTime.UtcNow.AddDays(-7))
                .ToListAsync();

            if (oldCompleted.Any())
            {
                _db.PendingSyncs.RemoveRange(oldCompleted);
                await _db.SaveChangesAsync();
            }

            var abandoned = await _db.PendingSyncs
                .Where(p => p.Status == SyncStatus.Failed && p.RetryCount >= 5)
                .ToListAsync();

            foreach (var item in abandoned)
            {
                item.Status = SyncStatus.Abandoned;
            }

            if (abandoned.Any())
            {
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation("Cola procesada: {Processed} exitosos, {Failed} fallidos", processed, failed);

            return (processed, failed);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<int> GetPendingCountAsync()
    {
        return await _db.PendingSyncs
            .CountAsync(p => p.Status == SyncStatus.Pending || p.Status == SyncStatus.Failed);
    }

    public async Task<List<PendingSync>> GetPendingItemsAsync()
    {
        return await _db.PendingSyncs
            .Where(p => p.Status == SyncStatus.Pending || p.Status == SyncStatus.Failed)
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .ToListAsync();
    }

    private async Task<bool> ProcessItemAsync(PendingSync item)
    {
        switch (item.Type)
        {
            case SyncType.OTPGeneration:
                var request = System.Text.Json.JsonSerializer.Deserialize<OTPRequest>(item.Payload);
                if (request == null) return false;

                var response = await _apiClient.GenerateOTPAsync(request);
                return response?.Success ?? false;

            case SyncType.Login:
                var loginRequest = System.Text.Json.JsonSerializer.Deserialize<LoginRequest>(item.Payload);
                if (loginRequest == null) return false;

                var loginResponse = await _apiClient.LoginAsync(loginRequest);
                return loginResponse?.Success ?? false;

            case SyncType.DeviceEnrollment:
                item.ErrorMessage = "Enrolamiento requiere interacción manual";
                return false;

            default:
                return false;
        }
    }
}