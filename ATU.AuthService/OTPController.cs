using System.Security.Cryptography;
using System.Text;
using ATU.AuditService;           // AuditHub
using ATU.AuthService.Models;
using ATU.Shared;
using ATU.Shared.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;

namespace ATU.AuthService.Controllers;

[ApiController]
[Route("api/otp")]
public class OTPController : ControllerBase
{
    private readonly IAuditEventPublisher _audit;
    private readonly IDeviceRepository _devices;
    private readonly IEncryptionService _encryption;
    private readonly IOtpRepository _otps;
    private readonly IHubContext<AuditHub> _auditHub;
    private readonly string _connStr;

    public OTPController(
        IAuditEventPublisher audit,
        IDeviceRepository devices,
        IEncryptionService encryption,
        IOtpRepository otps,
        IHubContext<AuditHub> auditHub,
        IConfiguration configuration)
    {
        _audit = audit;
        _devices = devices;
        _encryption = encryption;
        _otps = otps;
        _auditHub = auditHub;
        _connStr = configuration.GetConnectionString("SqlServer") ?? string.Empty;
    }

    // ════════════════════════════════════════════════════════════════════════
    // ENDPOINT EXISTENTE — generate (flujo original sin folio)
    // ════════════════════════════════════════════════════════════════════════

    public class MobileGenerateRequest
    {
        public string SupervisorId { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public string DeviceFingerprint { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class MobileValidateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public string SupervisorId { get; set; } = string.Empty;
        public string DeviceFingerprint { get; set; } = string.Empty;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] MobileGenerateRequest request)
    {
        var device = await _devices.GetByIdAsync(request.SupervisorId);
        if (device == null)
            return Ok(new { success = false, message = $"Supervisor '{request.SupervisorId}' no enrolado.", data = (object?)null, errors = new[] { "NOT_ENROLLED" } });

        var secret = _encryption.Decrypt(device.EncryptedSecret);
        var otp = ATUCore.GenerateOTP(secret, request.BatchId, request.SupervisorId);

        var record = new OtpRecord
        {
            Otp = otp,
            BatchId = request.BatchId,
            SupervisorId = request.SupervisorId,
            OperatorId = request.SupervisorId,
            GeneratedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ATUCore.TtlSeconds),
            IsUsed = false
        };
        await _otps.SaveAsync(record);

        await _audit.PublishAsync(new AuditEvent
        {
            Type = "OTP_GENERATED",
            SupervisorId = request.SupervisorId,
            BatchId = request.BatchId,
            Message = $"OTP generado para lote {request.BatchId}",
            Timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new
        {
            success = true,
            message = "OTP generado correctamente",
            data = new
            {
                code = otp,
                generatedAt = DateTime.UtcNow,
                expiresAt = DateTime.UtcNow.AddSeconds(ATUCore.TtlSeconds),
                secondsRemaining = ATUCore.TtlSeconds,
                batchId = request.BatchId,
                transactionId = Guid.NewGuid().ToString()
            },
            errors = new List<string>()
        });
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] MobileValidateRequest request)
    {
        var stored = await _otps.GetByBatchAndSupervisorAsync(request.BatchId, request.SupervisorId);
        if (stored == null)
            return Ok(new { success = false, status = "Red", message = "No hay OTP pendiente para este lote.", isAuthorized = false });

        if (stored.IsUsed)
            return Ok(new { success = false, status = "Red", message = "OTP ya fue utilizado.", isAuthorized = false });

        if (stored.ExpiresAt < DateTimeOffset.UtcNow)
            return Ok(new { success = false, status = "Yellow", message = "OTP expirado. Solicite uno nuevo.", isAuthorized = false });

        if (!ATUCore.CryptographicEquals(stored.Otp, request.Code))
            return Ok(new { success = false, status = "Red", message = "Código incorrecto.", isAuthorized = false });

        stored.IsUsed = true;
        stored.UsedAt = DateTimeOffset.UtcNow;
        await _otps.UpdateAsync(stored);

        await _audit.PublishAsync(new AuditEvent
        {
            Type = "OTP_VALIDATED",
            SupervisorId = request.SupervisorId,
            BatchId = request.BatchId,
            Message = $"Código validado para lote {request.BatchId}",
            Timestamp = DateTimeOffset.UtcNow
        });

        return Ok(new
        {
            success = true,
            status = "Green",
            message = "Autorización válida.",
            isAuthorized = true,
            supervisorId = stored.SupervisorId
        });
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] string batchId)
    {
        var pending = await _otps.GetPendingByBatchAsync(batchId);
        if (pending == null)
            return Ok(new { success = false, message = "Sin OTP pendiente.", supervisorId = (string?)null });

        return Ok(new
        {
            success = true,
            message = "OTP pendiente encontrado",
            supervisorId = pending.SupervisorId,
            expiresAt = pending.ExpiresAt.DateTime
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    // NUEVOS ENDPOINTS — flujo con folio adelantado de CargaEmbarques
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CargaEmbarques llama esto cuando detecta un folio adelantado.
    /// Registra en tb_det_folio_adelantado y notifica a ATU.CamaraFria via SignalR.
    /// </summary>
    [HttpPost("request")]
    public async Task<IActionResult> CreateRequest([FromBody] AtuRequestRequest req)
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, message = "Cadena de conexión SQL Server no configurada." });

        await using var conn = new SqlConnection(_connStr);

        await conn.ExecuteAsync(@"
            INSERT INTO tb_det_folio_adelantado
                (responsable, fecha, emb_folio, recibo_cap, fecreccap,
                 recibo_sug, fecrecsug, prod_clave, producto, cantidad,
                 tarimacap, tarimasug, imei, motivo, fechareal, otp_status)
            VALUES
                (@responsable, CONVERT(varchar,GETDATE(),103) + ' ' + CONVERT(varchar,GETDATE(),108),
                 @embFolio, @reciboCap, @fechaRecCap,
                 @reciboSug, @fechaRecSug, @prodClave, @producto, @cantidad,
                 @tarimaCap, @tarimaSug, @imei, @motivo, GETUTCDATE(), 'PENDING')",
            new
            {
                responsable = Truncate(req.Responsable, 25),
                embFolio = req.EmbFolio,
                reciboCap = req.ReciboCap,
                fechaRecCap = req.FechaRecCap,
                reciboSug = req.ReciboSug,
                fechaRecSug = req.FechaRecSug,
                prodClave = req.ProdClave,
                producto = req.Producto,
                cantidad = req.Cantidad,
                tarimaCap = req.TarimaCap,
                tarimaSug = req.TarimaSug,
                imei = req.Imei,
                motivo = req.Motivo
            });

        // Notificar a todos los supervisores de cámaras frías conectados
        await _auditHub.Clients.Group("audit-feed").SendAsync("NuevoFolioAdelantado", new
        {
            embFolio = req.EmbFolio,
            prodClave = req.ProdClave,
            producto = req.Producto,
            reciboSug = req.ReciboSug,
            tarimaSug = req.TarimaSug,
            responsable = req.Responsable,
            motivo = req.Motivo
        });

        // Registrar en auditoría SignalR dashboard
        await _auditHub.Clients.All.SendAsync("AuditEvent", new
        {
            status = "blue",
            title = "🔔 Solicitud de Folio Adelantado",
            batchId = $"{req.ProdClave}-{req.ReciboSug}-{req.TarimaSug}",
            supervisorId = req.Responsable,
            operatorId = req.Imei,
            message = $"CargaEmbarques solicitó autorización. Motivo: {req.Motivo}",
            timestamp = DateTime.UtcNow,
            eventId = Guid.NewGuid()
        });

        return Ok(new { success = true, message = "Solicitud registrada. Supervisores notificados." });
    }

    /// <summary>
    /// El supervisor de cámaras frías genera el OTP desde ATU.CamaraFria.
    /// </summary>
    [HttpPost("generate-folio")]
    public async Task<IActionResult> GenerateForFolio([FromBody] GenerateOtpFolioRequest req)
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, message = "Cadena de conexión SQL Server no configurada." });

        // 1. Verificar supervisor autorizado en Tb_Autoriza_OdeP
        await using var conn = new SqlConnection(_connStr);

        var esAutorizado = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM Tb_Autoriza_OdeP
            WHERE usuario = @u AND clave = 'EM'",
            new { u = req.SupervisorId });

        if (esAutorizado == 0)
            return Ok(new { success = false, message = "Supervisor no autorizado en el sistema.", errors = new[] { "NOT_AUTHORIZED" } });

        // 2. Leer el folio pendiente
        var folio = await conn.QueryFirstOrDefaultAsync<FolioRecord>(@"
            SELECT prod_clave   AS ProdClave,
                   recibo_sug   AS ReciboSug,
                   tarimasug    AS TarimaSug,
                   otp_status   AS OtpStatus
            FROM tb_det_folio_adelantado
            WHERE emb_folio  = @f
              AND otp_status = 'PENDING'",
            new { f = req.EmbFolio });

        if (folio == null)
            return Ok(new { success = false, message = "Folio no encontrado o ya procesado." });

        // 3. Verificar dispositivo enrolado
        var device = await _devices.GetByIdAsync(req.SupervisorId);
        if (device == null)
            return Ok(new { success = false, message = $"Supervisor '{req.SupervisorId}' no enrolado en ATU.", errors = new[] { "NOT_ENROLLED" } });

        var secret = _encryption.Decrypt(device.EncryptedSecret);
        var batchId = ATUCore.BuildBatchId(folio.ProdClave, folio.ReciboSug, folio.TarimaSug.ToString());
        var otp = ATUCore.GenerateOTP(secret, batchId, req.SupervisorId);
        var expires = DateTimeOffset.UtcNow.AddSeconds(ATUCore.TtlSeconds);

        // 4. Guardar hash del OTP en la tabla (nunca el OTP en claro)
        var otpHash = ComputeOtpHash(otp, req.EmbFolio);

        await conn.ExecuteAsync(@"
            UPDATE tb_det_folio_adelantado SET
                otp_hash         = @hash,
                otp_generado_por = @sup,
                otp_generado_at  = GETUTCDATE(),
                otp_expires_at   = @exp,
                otp_device_fp    = @fp,
                autorizo         = @sup
            WHERE emb_folio  = @f
              AND otp_status = 'PENDING'",
            new
            {
                hash = otpHash,
                sup = req.SupervisorId,
                exp = expires.UtcDateTime,
                fp = req.DeviceFingerprint,
                f = req.EmbFolio
            });

        // 5. Guardar en repositorio en memoria para validación rápida
        var record = new OtpRecord
        {
            Otp = otp,
            BatchId = batchId,
            SupervisorId = req.SupervisorId,
            OperatorId = req.SupervisorId,
            GeneratedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expires,
            IsUsed = false
        };
        await _otps.SaveAsync(record);

        // 6. Evento al dashboard
        await _auditHub.Clients.All.SendAsync("AuditEvent", new
        {
            status = "blue",
            title = "🔑 OTP Generado — Folio Adelantado",
            batchId = batchId,
            supervisorId = req.SupervisorId,
            operatorId = req.EmbFolio,
            message = $"OTP generado para folio {req.EmbFolio}. Expira en {ATUCore.TtlSeconds}s.",
            timestamp = DateTime.UtcNow,
            eventId = Guid.NewGuid()
        });

        await InsertAudit(conn, req.EmbFolio, "OTP_GENERATED", req.SupervisorId,
            req.DeviceFingerprint, folio.ProdClave, folio.ReciboSug, folio.TarimaSug.ToString());

        return Ok(new
        {
            success = true,
            message = "OTP generado correctamente",
            data = new
            {
                code = otp,
                generatedAt = DateTime.UtcNow,
                expiresAt = expires.UtcDateTime,
                secondsRemaining = ATUCore.TtlSeconds,
                batchId,
                transactionId = Guid.NewGuid().ToString()
            },
            errors = new List<string>()
        });
    }

    /// <summary>
    /// CargaEmbarques valida el OTP que dictó verbalmente el supervisor.
    /// </summary>
    [HttpPost("validate-folio")]
    public async Task<IActionResult> ValidateForFolio([FromBody] ValidateOtpFolioRequest req)
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, status = "Red", message = "Cadena de conexión SQL Server no configurada." });

        await using var conn = new SqlConnection(_connStr);

        // 1. Leer el folio
        var folio = await conn.QueryFirstOrDefaultAsync<FolioRecord>(@"
            SELECT prod_clave       AS ProdClave,
                   recibo_sug       AS ReciboSug,
                   tarimasug        AS TarimaSug,
                   otp_status       AS OtpStatus,
                   otp_expires_at   AS OtpExpiresAt,
                   otp_generado_por AS OtpGeneradoPor,
                   otp_intentos     AS OtpIntentos,
                   otp_device_fp    AS OtpDeviceFp
            FROM tb_det_folio_adelantado
            WHERE emb_folio = @f",
            new { f = req.EmbFolio });

        if (folio == null)
            return Ok(new { success = false, status = "Red", message = "Folio no encontrado." });

        if (folio.OtpStatus == "AUTHORIZED")
            return Ok(new { success = false, status = "Red", message = "Este folio ya fue autorizado (posible replay)." });

        if (folio.OtpStatus != "PENDING")
            return Ok(new { success = false, status = "Red", message = $"Estado inválido: {folio.OtpStatus}." });

        // 2. Verificar expiración
        if (folio.OtpExpiresAt.HasValue && DateTime.UtcNow > folio.OtpExpiresAt.Value)
        {
            await conn.ExecuteAsync(
                "UPDATE tb_det_folio_adelantado SET otp_status='EXPIRED' WHERE emb_folio=@f",
                new { f = req.EmbFolio });

            await _auditHub.Clients.All.SendAsync("AuditEvent", new
            {
                status = "yellow",
                title = "⏱️ OTP Expirado",
                batchId = $"{folio.ProdClave}-{folio.ReciboSug}-{folio.TarimaSug}",
                supervisorId = req.SupervisorId,
                operatorId = req.EmbFolio,
                message = "El código expiró antes de ser validado.",
                timestamp = DateTime.UtcNow,
                eventId = Guid.NewGuid()
            });

            return Ok(new { success = false, status = "Yellow", message = "OTP expirado. Solicite un nuevo código.", isAuthorized = false });
        }

        // 3. Rate limiting
        if (folio.OtpIntentos >= 3)
        {
            return Ok(new { success = false, status = "Red", message = "Demasiados intentos. Solicite un nuevo OTP al supervisor.", isAuthorized = false });
        }

        // 4. Construir batchIds para comparación FIFO
        var claimedBatchId = ATUCore.BuildBatchId(folio.ProdClave, folio.ReciboSug, folio.TarimaSug.ToString());
        var actualBatchId = ATUCore.BuildBatchId(req.ActualProdClave, req.ActualRecibo, req.ActualTarima.ToString());

        // 5. Obtener secret del supervisor que generó el OTP
        var device = await _devices.GetByIdAsync(folio.OtpGeneradoPor);
        if (device == null)
        {
            // Fallback: buscar por supervisorId del request
            device = await _devices.GetByIdAsync(req.SupervisorId);
            if (device == null)
                return Ok(new { success = false, status = "Red", message = "Supervisor no identificado en el sistema ATU." });
        }

        var secret = _encryption.Decrypt(device.EncryptedSecret);

        // 6. Validación FIFO + OTP
        var result = ATUCore.ValidateFIFO(req.Code, secret, claimedBatchId, actualBatchId, req.SupervisorId);

        var newStatus = result.Status switch
        {
            ATUStatus.Green => "AUTHORIZED",
            ATUStatus.Yellow => "EXPIRED",
            ATUStatus.Red when result.Message.Contains("FRAUDE") => "FRAUD",
            _ => "INVALID"
        };

        // 7. Actualizar tabla
        await conn.ExecuteAsync(@"
            UPDATE tb_det_folio_adelantado SET
                otp_status   = @status,
                otp_usado_at = CASE WHEN @status = 'AUTHORIZED' THEN GETUTCDATE() ELSE NULL END,
                otp_intentos = otp_intentos + 1
            WHERE emb_folio = @f",
            new { status = newStatus, f = req.EmbFolio });

        // 8. Marcar OTP en memoria como usado (anti-replay)
        var stored = await _otps.GetByBatchAndSupervisorAsync(claimedBatchId, req.SupervisorId);
        if (stored != null)
        {
            stored.IsUsed = true;
            stored.UsedAt = DateTimeOffset.UtcNow;
            await _otps.UpdateAsync(stored);
        }

        // 9. Evento SignalR al dashboard
        var eventoStatus = newStatus == "AUTHORIZED" ? "green"
                         : newStatus == "FRAUD" ? "red"
                         : "yellow";

        await _auditHub.Clients.All.SendAsync("AuditEvent", new
        {
            status = eventoStatus,
            title = newStatus == "AUTHORIZED" ? "✅ Folio Adelantado Autorizado"
                         : newStatus == "FRAUD" ? "🚨 FRAUDE DETECTADO"
                         : "❌ OTP Inválido",
            batchId = actualBatchId,
            supervisorId = req.SupervisorId,
            operatorId = req.EmbFolio,
            message = result.Message,
            isFraud = newStatus == "FRAUD",
            timestamp = DateTime.UtcNow,
            eventId = Guid.NewGuid()
        });

        await InsertAudit(conn, req.EmbFolio,
            newStatus == "AUTHORIZED" ? "OTP_VALIDATED" : newStatus,
            req.SupervisorId, null,
            req.ActualProdClave, req.ActualRecibo, req.ActualTarima.ToString(),
            result.Message);

        return Ok(new
        {
            success = result.IsAuthorized,
            status = result.Status.ToString(),
            message = result.Message,
            isAuthorized = result.IsAuthorized,
            supervisorId = folio.OtpGeneradoPor
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers privados
    // ════════════════════════════════════════════════════════════════════════

    private static string ComputeOtpHash(string otp, string folio)
    {
        using var sha = SHA256.Create();
        var input = Encoding.UTF8.GetBytes($"{otp}:{folio}:ATU-SALT-2024");
        return Convert.ToHexString(sha.ComputeHash(input));
    }

    private static async Task InsertAudit(
        SqlConnection conn,
        string embFolio,
        string evento,
        string supervisorId,
        string? deviceFp,
        string prodClave,
        string recibo,
        string tarima,
        string mensaje = "")
    {
        try
        {
            await conn.ExecuteAsync(@"
                INSERT INTO tb_atu_audit
                    (emb_folio, evento, supervisor_id, device_fp,
                     prod_clave, recibo, tarima, mensaje, created_at)
                VALUES
                    (@embFolio, @evento, @supervisorId, @deviceFp,
                     @prodClave, @recibo, @tarima, @mensaje, GETUTCDATE())",
                new
                {
                    embFolio,
                    evento,
                    supervisorId,
                    deviceFp,
                    prodClave,
                    recibo,
                    tarima,
                    mensaje
                });
        }
        catch
        {
            // La auditoría no debe interrumpir el flujo principal
        }
    }

    private static string Truncate(string s, int max)
        => s.Length > max ? s[..max] : s;
}