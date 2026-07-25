using ATU.Shared.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace ATU.AuthService;

/// <summary>
/// Repositorio de dispositivos enrolados persistido en SQL Server.
/// Reemplaza InMemoryDeviceRepository — los enrolamientos sobreviven reinicios.
/// </summary>
public sealed class SqlDeviceRepository : IDeviceRepository
{
    private readonly string _connStr;

    public SqlDeviceRepository(IConfiguration configuration)
    {
        _connStr = configuration.GetConnectionString("SqlServer") ?? string.Empty;
    }

    public async Task<EnrolledDevice?> GetByIdAsync(string operatorId)
    {
        if (string.IsNullOrWhiteSpace(_connStr)) return null;
        try
        {
            await using var conn = new SqlConnection(_connStr);
            var row = await conn.QueryFirstOrDefaultAsync<DeviceRow>(@"
                SELECT operator_id, fingerprint, encrypted_secret,
                       push_token, cold_storage_zone_id,
                       is_active, enrolled_at, revoked_at
                FROM   tb_atu_devices
                WHERE  LTRIM(RTRIM(operator_id)) = @id
                  AND  is_active = 1",
                new { id = operatorId.Trim() });

            return row == null ? null : MapToDevice(row);
        }
        catch { return null; }
    }

    public async Task<EnrolledDevice?> GetActiveByOperatorAsync(string operatorId)
        => await GetByIdAsync(operatorId);

    public async Task AddAsync(EnrolledDevice device)
    {
        if (string.IsNullOrWhiteSpace(_connStr)) return;
        try
        {
            await EnsureTableExistsAsync();
            await using var conn = new SqlConnection(_connStr);
            await conn.ExecuteAsync(@"
                -- Desactivar registros anteriores del mismo operador
                UPDATE tb_atu_devices SET is_active = 0, revoked_at = GETDATE()
                WHERE  operator_id = @operatorId;

                -- Insertar el nuevo
                INSERT INTO tb_atu_devices
                    (operator_id, fingerprint, encrypted_secret,
                     push_token, cold_storage_zone_id,
                     is_active, enrolled_at)
                VALUES
                    (@operatorId, @fingerprint, @encryptedSecret,
                     @pushToken, @zoneId,
                     1, GETDATE())",
                new
                {
                    operatorId = device.OperatorId.Trim(),
                    fingerprint = device.Fingerprint,
                    encryptedSecret = device.EncryptedSecret,
                    pushToken = device.PushToken,
                    zoneId = device.ColdStorageZoneId
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ATU] SqlDeviceRepository.AddAsync error: {ex.Message}");
        }
    }

    public async Task UpdateAsync(EnrolledDevice device)
    {
        if (string.IsNullOrWhiteSpace(_connStr)) return;
        try
        {
            await using var conn = new SqlConnection(_connStr);
            await conn.ExecuteAsync(@"
                UPDATE tb_atu_devices SET
                    fingerprint           = @fingerprint,
                    encrypted_secret      = @encryptedSecret,
                    is_active             = @isActive,
                    revoked_at            = CASE WHEN @isActive = 0 THEN GETDATE() ELSE NULL END
                WHERE operator_id = @operatorId",
                new
                {
                    operatorId = device.OperatorId.Trim(),
                    fingerprint = device.Fingerprint,
                    encryptedSecret = device.EncryptedSecret,
                    isActive = device.IsActive ? 1 : 0
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ATU] SqlDeviceRepository.UpdateAsync error: {ex.Message}");
        }
    }

    // Crea la tabla si no existe (se llama la primera vez que se enrola un dispositivo)
    private async Task EnsureTableExistsAsync()
    {
        try
        {
            await using var conn = new SqlConnection(_connStr);
            await conn.ExecuteAsync(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_NAME = 'tb_atu_devices')
                BEGIN
                    CREATE TABLE [dbo].[tb_atu_devices] (
                        [id]                   INT IDENTITY(1,1) PRIMARY KEY,
                        [operator_id]          VARCHAR(15)   NOT NULL,
                        [fingerprint]          VARCHAR(64)   NOT NULL,
                        [encrypted_secret]     VARCHAR(256)  NOT NULL,
                        [push_token]           VARCHAR(256)  NULL,
                        [cold_storage_zone_id] VARCHAR(50)   NULL,
                        [is_active]            BIT           NOT NULL DEFAULT 1,
                        [enrolled_at]          DATETIME      NOT NULL DEFAULT GETDATE(),
                        [revoked_at]           DATETIME      NULL
                    );
                    CREATE NONCLUSTERED INDEX IX_atu_devices_operator
                        ON [dbo].[tb_atu_devices] ([operator_id], [is_active]);
                END");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ATU] EnsureTableExists error: {ex.Message}");
        }
    }

    private static EnrolledDevice MapToDevice(DeviceRow row) => new()
    {
        OperatorId = row.operator_id.Trim(),
        Fingerprint = row.fingerprint,
        EncryptedSecret = row.encrypted_secret,
        PushToken = row.push_token ?? string.Empty,
        ColdStorageZoneId = row.cold_storage_zone_id ?? string.Empty,
        IsActive = row.is_active,
        EnrolledAt = new DateTimeOffset(row.enrolled_at, TimeSpan.Zero),
        RevokedAt = row.revoked_at.HasValue
                            ? new DateTimeOffset(row.revoked_at.Value, TimeSpan.Zero)
                            : null
    };

    private class DeviceRow
    {
        public string operator_id { get; set; } = string.Empty;
        public string fingerprint { get; set; } = string.Empty;
        public string encrypted_secret { get; set; } = string.Empty;
        public string? push_token { get; set; }
        public string? cold_storage_zone_id { get; set; }
        public bool is_active { get; set; }
        public DateTime enrolled_at { get; set; }
        public DateTime? revoked_at { get; set; }
    }
}
