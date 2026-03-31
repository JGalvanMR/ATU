using ATU.Shared.Models;
using System.Text;

namespace ATU.AuthService;

public sealed class InMemoryDeviceRepository : IDeviceRepository
{
    private readonly List<EnrolledDevice> _devices = new();

    public Task<EnrolledDevice?> GetActiveByOperatorAsync(string operatorId)
    {
        var active = _devices
            .Where(d => d.OperatorId == operatorId && d.IsActive && d.RevokedAt == null)
            .OrderByDescending(d => d.EnrolledAt)
            .FirstOrDefault();
        return Task.FromResult(active);
    }

    public Task AddAsync(EnrolledDevice device)
    {
        _devices.Add(device);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(EnrolledDevice device)
    {
        var idx = _devices.FindIndex(d => d.Id == device.Id);
        if (idx >= 0) _devices[idx] = device;
        return Task.CompletedTask;
    }

    public Task<EnrolledDevice?> GetByIdAsync(string deviceId)
        => Task.FromResult(_devices.FirstOrDefault(d => d.OperatorId == deviceId && d.IsActive));
}