using ATU.Shared;
using ATU.Shared.Models;

namespace ATU.AuthService;

public sealed class InMemoryOtpRepository : IOtpRepository
{
    private static readonly List<OtpRecord> _records = new();

    public Task SaveAsync(OtpRecord record)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<OtpRecord?> GetByBatchAndSupervisorAsync(string batchId, string supervisorId)
        => Task.FromResult(_records.FirstOrDefault(r =>
            r.BatchId == batchId && r.SupervisorId == supervisorId && !r.IsUsed));

    public Task<OtpRecord?> GetByOtpAsync(string otp)
        => Task.FromResult(_records.FirstOrDefault(r => r.Otp == otp && !r.IsUsed));

    public Task UpdateAsync(OtpRecord record)
    {
        var idx = _records.FindIndex(r => r.Id == record.Id);
        if (idx >= 0) _records[idx] = record;
        return Task.CompletedTask;
    }

    public Task<OtpRecord?> GetPendingByBatchAsync(string batchId)
        => Task.FromResult(_records.FirstOrDefault(r =>
            r.BatchId == batchId && !r.IsUsed && r.ExpiresAt > DateTimeOffset.Now));
}