using System;

namespace ATU.CamaraFria.Models;

/// <summary>
/// Registro pendiente de sincronización para modo offline
/// </summary>
public class PendingSync
{
    public int Id { get; set; }
    public SyncType Type { get; set; }
    public string Payload { get; set; } = string.Empty; // JSON serializado
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int RetryCount { get; set; }
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? SyncedAt { get; set; }
}

public enum SyncType
{
    OTPGeneration,
    Login,
    DeviceEnrollment,
    AuditEvent
}

public enum SyncStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Abandoned
}