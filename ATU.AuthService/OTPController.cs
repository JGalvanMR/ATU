using System.Security.Cryptography;
using System.Text;
using ATU.AuditService;
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

    // ═══════════════════════════════════════════════════════════════════════
    // ENDPOINT EXISTENTE — generate (flujo normal sin folio)
    // ═══════════════════════════════════════════════════════════════════════

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
        var expires = DateTimeOffset.Now.AddSeconds(ATUCore.TtlSeconds);

        var record = new OtpRecord
        {
            Otp = otp,
            BatchId = request.BatchId,
            SupervisorId = request.SupervisorId,
            OperatorId = request.SupervisorId,
            GeneratedAt = DateTimeOffset.Now,
            ExpiresAt = expires,
            IsUsed = false
        };
        await _otps.SaveAsync(record);

        await EmitirAuditEvent("blue", "🔑 OTP Generado", request.BatchId,
            request.SupervisorId, request.SupervisorId,
            $"OTP generado para lote {request.BatchId}");

        return Ok(new
        {
            success = true,
            message = "OTP generado correctamente",
            data = new
            {
                code = otp,
                generatedAt = DateTime.Now,
                expiresAt = expires.DateTime,
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
        if (stored == null) return Ok(new { success = false, status = "Red", message = "No hay OTP pendiente.", isAuthorized = false });
        if (stored.IsUsed) return Ok(new { success = false, status = "Red", message = "OTP ya usado.", isAuthorized = false });
        if (stored.ExpiresAt < DateTimeOffset.Now) return Ok(new { success = false, status = "Yellow", message = "OTP expirado. Solicite uno nuevo.", isAuthorized = false });
        if (!ATUCore.CryptographicEquals(stored.Otp, request.Code)) return Ok(new { success = false, status = "Red", message = "Código incorrecto.", isAuthorized = false });

        stored.IsUsed = true; stored.UsedAt = DateTimeOffset.Now;
        await _otps.UpdateAsync(stored);

        await EmitirAuditEvent("green", "✅ OTP Validado", request.BatchId,
            request.SupervisorId, request.SupervisorId, $"Autorización válida para {request.BatchId}");

        return Ok(new { success = true, status = "Green", message = "Autorización válida.", isAuthorized = true, supervisorId = stored.SupervisorId });
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] string batchId)
    {
        var p = await _otps.GetPendingByBatchAsync(batchId);
        if (p == null) return Ok(new { success = false, message = "Sin OTP pendiente.", supervisorId = (string?)null });
        return Ok(new { success = true, supervisorId = p.SupervisorId, expiresAt = p.ExpiresAt.DateTime });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NUEVOS ENDPOINTS — flujo con folio adelantado
    // ═══════════════════════════════════════════════════════════════════════

    [HttpPost("request")]
    public async Task<IActionResult> CreateRequest([FromBody] AtuRequestRequest req)
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, message = "SQL Server no configurado." });

        await using var conn = new SqlConnection(_connStr);

        var existe = await conn.ExecuteScalarAsync<int>(@"
        SELECT COUNT(1) FROM tb_det_folio_adelantado WITH (UPDLOCK, HOLDLOCK)
        WHERE LTRIM(RTRIM(emb_folio)) = @f
          AND LTRIM(RTRIM(recibo_cap)) = @rc
          AND LTRIM(RTRIM(prod_clave)) = @pc
          AND LTRIM(RTRIM(tarimacap)) = @tc
          AND otp_status = 'PENDING'",
        new
        {
            f = req.EmbFolio.Trim(),
            rc = req.ReciboCap.Trim(),
            pc = req.ProdClave.Trim(),
            tc = req.TarimaCap.Trim()
        });

        if (existe > 0)
        {
            return Conflict(new
            {
                success = false,
                message = "Ya existe una solicitud de autorización PENDIENTE para este pallet (mismo recibo, producto y tarima) en este embarque. " +
                          "Espera a que sea procesada o cancélala antes de intentarlo de nuevo."
            });
        }

        // Insertar en tb_det_folio_adelantado con otp_status = PENDING
        await conn.ExecuteAsync(@"
            INSERT INTO tb_det_folio_adelantado
                (responsable, fecha, emb_folio, recibo_cap, fecreccap,
                 recibo_sug, fecrecsug, prod_clave, producto, cantidad,
                 tarimacap, tarimasug, imei, motivo, fechareal, otp_status)
            VALUES
                (@responsable,
                 CONVERT(varchar,GETDATE(),103)+' '+CONVERT(varchar,GETDATE(),108),
                 @embFolio, @reciboCap, @fechaRecCap,
                 @reciboSug, @fechaRecSug, @prodClave, @producto, @cantidad,
                 @tarimaCap, @tarimaSug, @imei, @motivo, GETDATE(), 'PENDING')",
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

        // Notificar a la app ATU.CamaraFria y al dashboard
        var notif = new
        {
            embFolio = req.EmbFolio,
            prodClave = req.ProdClave,
            producto = req.Producto,
            reciboSug = req.ReciboSug,
            tarimaSug = req.TarimaSug,
            responsable = req.Responsable,
            motivo = req.Motivo
        };

        await _auditHub.Clients.Group("audit-feed").SendAsync("NuevoFolioAdelantado", notif);

        await EmitirAuditEvent("blue", "🔔 Folio Adelantado Solicitado",
            $"{req.ProdClave}-{req.ReciboSug}-{req.TarimaSug}",
            req.Responsable, req.Imei,
            $"CargaEmbarques solicita autorización. Motivo: {req.Motivo}");

        return Ok(new { success = true, message = "Solicitud registrada. Supervisores notificados." });
    }

    [HttpGet("solicitudes-pendientes")]
    public async Task<IActionResult> GetSolicitudesPendientes()
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, data = Array.Empty<object>() });

        await using var conn = new SqlConnection(_connStr);

        var rows = await conn.QueryAsync<dynamic>(@"
    SELECT TOP 50
        LTRIM(RTRIM(emb_folio))  AS EmbFolio,
        LTRIM(RTRIM(prod_clave)) AS ProdClave,
        LTRIM(RTRIM(producto))   AS Producto,
        LTRIM(RTRIM(recibo_cap)) AS ReciboCap,
        CAST(tarimacap AS VARCHAR) AS TarimaCap,
        LTRIM(RTRIM(recibo_sug)) AS ReciboSug,   -- ← NUEVO
        CAST(tarimasug AS VARCHAR) AS TarimaSug, -- ← NUEVO
        LTRIM(RTRIM(responsable)) AS Responsable,
        LTRIM(RTRIM(motivo))     AS Motivo,
        fechareal                AS FechaCreacion
    FROM tb_det_folio_adelantado
    WHERE otp_status = 'PENDING'
      AND fechareal >= DATEADD(HOUR, -8, GETDATE())
    ORDER BY fechareal DESC");



        return Ok(new { success = true, data = rows });
    }

    [HttpPost("generate-folio")]
    public async Task<IActionResult> GenerateForFolio([FromBody] GenerateOtpFolioRequest req)
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, message = "SQL Server no configurado." });

        await using var conn = new SqlConnection(_connStr);

        // Verificar supervisor autorizado en Tb_Autoriza_OdeP
        var esAutorizado = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM Tb_Autoriza_OdeP
            WHERE LTRIM(RTRIM(usuario)) = @u AND LTRIM(RTRIM(clave)) = 'EM'",
            new { u = req.SupervisorId });

        if (esAutorizado == 0)
            return Ok(new { success = false, message = "Supervisor no autorizado en el sistema." });

        // Leer el folio pendiente
        var folio = await conn.QueryFirstOrDefaultAsync<FolioRecord>(@"
            SELECT prod_clave AS ProdClave,
                   recibo_cap AS ReciboCap,
                   tarimacap  AS TarimaCap,
                   recibo_sug AS ReciboSug, 
                   tarimasug  AS TarimaSug, 
                   otp_status AS OtpStatus
            FROM tb_det_folio_adelantado
            WHERE LTRIM(RTRIM(emb_folio)) = @f
              AND LTRIM(RTRIM(recibo_cap)) = @rc
              AND LTRIM(RTRIM(prod_clave)) = @pc
              AND LTRIM(RTRIM(tarimacap)) = @tc
              AND otp_status = 'PENDING'",
            new
            {
                f = req.EmbFolio.Trim(),
                rc = req.ReciboCap.Trim(),
                pc = req.ProdClave.Trim(),
                tc = req.TarimaCap.Trim()
            });

        if (folio == null)
            return Ok(new { success = false, message = "Folio no encontrado o ya procesado." });

        // Verificar dispositivo enrolado
        var device = await _devices.GetByIdAsync(req.SupervisorId);
        if (device == null)
            return Ok(new { success = false, message = $"Supervisor '{req.SupervisorId}' no enrolado en ATU. Inicia sesión en la app.", errors = new[] { "NOT_ENROLLED" } });

        var secret = _encryption.Decrypt(device.EncryptedSecret);
        var batchId = ATUCore.BuildBatchId(folio.ReciboCap, folio.ProdClave, folio.TarimaCap.ToString());
        var otp = ATUCore.GenerateOTP(secret, batchId, req.SupervisorId);
        var expires = DateTimeOffset.Now.AddSeconds(ATUCore.TtlSeconds);

        // Guardar hash del OTP (nunca el OTP en claro)
        var otpHash = ComputeOtpHash(otp, req.EmbFolio);

        var rowsAffected = await conn.ExecuteAsync(@"
            UPDATE tb_det_folio_adelantado SET
                otp_hash         = @hash,
                otp_generado_por = @sup,
                otp_generado_at  = GETDATE(),
                otp_expires_at   = @exp,
                otp_device_fp    = @fp,
                autorizo         = @sup
            WHERE LTRIM(RTRIM(emb_folio)) = @f
              AND LTRIM(RTRIM(recibo_cap)) = @rc
              AND LTRIM(RTRIM(prod_clave)) = @pc
              AND LTRIM(RTRIM(tarimacap)) = @tc
              AND otp_status = 'PENDING'",
            new
            {
                hash = otpHash,
                sup = req.SupervisorId,
                exp = expires.DateTime,
                fp = req.DeviceFingerprint,
                f = req.EmbFolio.Trim(),
                rc = req.ReciboCap.Trim(),
                pc = req.ProdClave.Trim(),
                tc = req.TarimaCap.Trim()
            });

        // ✅ AGREGAR ESTO:
        if (rowsAffected == 0) // Nota: asegúrate de capturar el retorno del ExecuteAsync en una variable si no lo tienes
        {
            return Ok(new { success = false, message = "Error crítico: No se pudo vincular el OTP al folio. Los datos del escaneo del encargado no coinciden exactamente con la solicitud original." });
        }

        // Guardar en repositorio en memoria para validación rápida
        await _otps.SaveAsync(new OtpRecord
        {
            Otp = otp,
            BatchId = batchId,
            SupervisorId = req.SupervisorId,
            OperatorId = req.SupervisorId,
            GeneratedAt = DateTimeOffset.Now,
            ExpiresAt = expires,
            IsUsed = false
        });

        await EmitirAuditEvent("blue", "🔑 OTP Folio Adelantado", batchId,
            req.SupervisorId, req.EmbFolio,
            $"OTP generado para folio {req.EmbFolio}. Expira en {ATUCore.TtlSeconds}s.");

        await InsertAudit(conn, req.EmbFolio, "OTP_GENERATED", req.SupervisorId,
            req.DeviceFingerprint, folio.ProdClave, folio.ReciboCap, folio.TarimaCap.ToString());

        return Ok(new
        {
            success = true,
            message = "OTP generado correctamente",
            data = new
            {
                code = otp,
                generatedAt = DateTime.Now,
                expiresAt = expires.DateTime,
                secondsRemaining = ATUCore.TtlSeconds,
                batchId,
                transactionId = Guid.NewGuid().ToString()
            },
            errors = new List<string>()
        });
    }

    [HttpPost("validate-folios")]
    public async Task<IActionResult> ValidateForFolios([FromBody] ValidateOtpFolioRequest req)
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, status = "Red", message = "SQL Server no configurado." });

        await using var conn = new SqlConnection(_connStr);
        var folio = await conn.QueryFirstOrDefaultAsync<FolioRecord>(@"
                    SELECT prod_clave       AS ProdClave,
                           recibo_cap       AS ReciboCap,
                           tarimacap        AS TarimaCap,
                           otp_status       AS OtpStatus,
                           otp_expires_at   AS OtpExpiresAt,
                           otp_generado_por AS OtpGeneradoPor,
                           otp_intentos     AS OtpIntentos
                    FROM tb_det_folio_adelantado
                    WHERE LTRIM(RTRIM(emb_folio)) = @f
                      AND LTRIM(RTRIM(recibo_cap)) = @actualRecibo   -- ← Cambio clave
                      AND LTRIM(RTRIM(prod_clave)) = @actualProdClave -- ← Cambio clave
                      AND LTRIM(RTRIM(tarimacap)) = @actualTarima    -- ← Cambio clave
                      AND otp_status = 'PENDING'",
                    new
                    {
                        f = req.EmbFolio.Trim(),
                        actualRecibo = req.ActualRecibo.Trim(),
                        actualProdClave = req.ActualProdClave.Trim(),
                        actualTarima = req.ActualTarima.ToString() // Ajusta tipo según tu BD
                    });

        if (folio == null)
            return Ok(new { success = false, status = "Red", message = "Folio no encontrado.", isAuthorized = false });
        if (folio.OtpStatus == "AUTHORIZED")
            return Ok(new { success = false, status = "Red", message = "Folio ya autorizado (posible replay).", isAuthorized = false });
        if (folio.OtpStatus != "PENDING")
            return Ok(new { success = false, status = "Red", message = $"Estado inválido: {folio.OtpStatus}.", isAuthorized = false });
        if (folio.OtpExpiresAt.HasValue && DateTime.Now > folio.OtpExpiresAt.Value)
        {
            await conn.ExecuteAsync("UPDATE tb_det_folio_adelantado SET otp_status='EXPIRED' WHERE LTRIM(RTRIM(emb_folio))=@f", new { f = req.EmbFolio.Trim() });
            await EmitirAuditEvent("yellow", "⏱ OTP Expirado",
                $"{folio.ReciboCap}-{folio.ProdClave}-{folio.TarimaCap}",
                folio.OtpGeneradoPor, req.EmbFolio, "Expiró antes de ser validado.");
            return Ok(new { success = false, status = "Yellow", message = "OTP expirado. Solicite un nuevo código.", isAuthorized = false });
        }
        if (folio.OtpIntentos >= 3)
            return Ok(new { success = false, status = "Red", message = "Demasiados intentos. Solicite nuevo OTP.", isAuthorized = false });

        // Construir batchIds
        var claimedBatchId = ATUCore.BuildBatchId(folio.ReciboCap, folio.ProdClave, folio.TarimaCap.ToString());
        var actualBatchId = ATUCore.BuildBatchId(req.ActualRecibo, req.ActualProdClave, req.ActualTarima.ToString());

        // Obtener secret del supervisor que generó el OTP (sin necesitar supervisorId del cliente)
        var supId = folio.OtpGeneradoPor;
        var device = await _devices.GetByIdAsync(supId);
        if (device == null)
            return Ok(new { success = false, status = "Red", message = "No se pudo identificar al supervisor que generó el OTP.", isAuthorized = false });

        var secret = _encryption.Decrypt(device.EncryptedSecret);
        var result = ATUCore.ValidateFIFO(req.Code, secret, claimedBatchId, actualBatchId, supId);

        var newStatus = result.Status switch
        {
            ATUStatus.Green => "AUTHORIZED",
            ATUStatus.Yellow => "EXPIRED",
            ATUStatus.Red when result.Message.Contains("FRAUDE") => "FRAUD",
            _ => "INVALID"
        };

        var rowsAffected = await conn.ExecuteAsync(@"
            UPDATE tb_det_folio_adelantado SET 
                otp_status   = @status,
                otp_usado_at = CASE WHEN @status='AUTHORIZED'
                                    THEN GETDATE()
                                    ELSE otp_usado_at
                               END
             WHERE LTRIM(RTRIM(emb_folio)) = @f
               AND otp_status = 'PENDING'",
        new
        {
            status = newStatus,
            f = req.EmbFolio.Trim()
        });

        if (rowsAffected == 0)
        {
            return Ok(new
            {
                success = false,
                status = "Red",
                message = "El folio ya fue procesado por otro usuario.",
                isAuthorized = false
            });
        }

        // Marcar OTP en memoria como usado
        var stored = await _otps.GetByBatchAndSupervisorAsync(claimedBatchId, supId);
        if (stored != null) { stored.IsUsed = true; stored.UsedAt = DateTimeOffset.Now; await _otps.UpdateAsync(stored); }

        var eventoColor = newStatus == "AUTHORIZED" ? "green" : newStatus == "FRAUD" ? "red" : "yellow";
        var eventoTitle = newStatus == "AUTHORIZED" ? "✅ Folio Adelantado Autorizado"
                        : newStatus == "FRAUD" ? "🚨 FRAUDE DETECTADO"
                        : "❌ OTP Inválido";

        await EmitirAuditEvent(eventoColor, eventoTitle, actualBatchId, supId, req.EmbFolio,
            result.Message, newStatus == "FRAUD");

        await InsertAudit(conn, req.EmbFolio,
            newStatus == "AUTHORIZED" ? "OTP_VALIDATED" : newStatus,
            supId, null, req.ActualProdClave, req.ActualRecibo, req.ActualTarima.ToString(),
            result.Message);

        return Ok(new
        {
            success = result.IsAuthorized,
            status = result.Status.ToString(),
            message = result.Message,
            isAuthorized = result.IsAuthorized,
            supervisorId = supId   // ← el servidor resuelve el supervisorId
        });
    }

    [HttpPost("validate-folio")]
    public async Task<IActionResult> ValidateForFolio([FromBody] ValidateOtpFolioRequest req)
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, status = "Red", message = "SQL Server no configurado." });

        await using var conn = new SqlConnection(_connStr);

        var folio = await conn.QueryFirstOrDefaultAsync<FolioRecord>(@"
                SELECT prod_clave       AS ProdClave,
                       recibo_cap       AS ReciboCap,
                       tarimacap        AS TarimaCap,
                       otp_status       AS OtpStatus,
                       otp_expires_at   AS OtpExpiresAt,
                       otp_generado_por AS OtpGeneradoPor,
                       otp_intentos     AS OtpIntentos
                FROM tb_det_folio_adelantado
                WHERE LTRIM(RTRIM(emb_folio)) = @f
                  AND LTRIM(RTRIM(recibo_cap)) = @actualRecibo
                  AND LTRIM(RTRIM(prod_clave)) = @actualProdClave
                  AND LTRIM(RTRIM(tarimacap)) = @actualTarima
                  AND otp_status = 'PENDING'",
                        new
                        {
                            f = req.EmbFolio.Trim(),
                            actualRecibo = req.ActualRecibo.Trim(),
                            actualProdClave = req.ActualProdClave.Trim(),
                            actualTarima = req.ActualTarima.ToString()
                        });

        if (folio == null)
            return Ok(new { success = false, status = "Red", message = "Folio no encontrado.", isAuthorized = false });
        if (folio.OtpStatus == "AUTHORIZED")
            return Ok(new { success = false, status = "Red", message = "Folio ya autorizado (posible replay).", isAuthorized = false });
        if (folio.OtpStatus != "PENDING")
            return Ok(new { success = false, status = "Red", message = $"Estado inválido: {folio.OtpStatus}.", isAuthorized = false });

        if (folio.OtpExpiresAt.HasValue && DateTime.Now > folio.OtpExpiresAt.Value)
        {
            await conn.ExecuteAsync("UPDATE tb_det_folio_adelantado SET otp_status='EXPIRED' WHERE LTRIM(RTRIM(emb_folio))=@f", new { f = req.EmbFolio.Trim() });
            await EmitirAuditEvent("yellow", "⏱ OTP Expirado",
                $"{folio.ReciboCap}-{folio.ProdClave}-{folio.TarimaCap}",
                folio.OtpGeneradoPor ?? "DESCONOCIDO", req.EmbFolio, "Expiró antes de ser validado.");
            return Ok(new { success = false, status = "Yellow", message = "OTP expirado. Solicite un nuevo código.", isAuthorized = false });
        }

        if (folio.OtpIntentos >= 3)
            return Ok(new { success = false, status = "Red", message = "Demasiados intentos. Solicite nuevo OTP.", isAuthorized = false });

        var claimedBatchId = ATUCore.BuildBatchId(folio.ReciboCap, folio.ProdClave, folio.TarimaCap.ToString());
        var actualBatchId = ATUCore.BuildBatchId(req.ActualRecibo, req.ActualProdClave, req.ActualTarima.ToString());

        var supId = folio.OtpGeneradoPor;

        // ✅ NUEVA VALIDACIÓN: Evita que explote si generate-folio falló silenciosamente
        if (string.IsNullOrWhiteSpace(supId))
        {
            return Ok(new
            {
                success = false,
                status = "Red",
                message = "Error de integridad: El OTP fue generado, pero no se registró quién lo autorizó. La app de Cámaras Frías debe volver a generar el código.",
                isAuthorized = false
            });
        }

        var device = await _devices.GetByIdAsync(supId);
        if (device == null)
            return Ok(new { success = false, status = "Red", message = $"Supervisor '{supId}' no encontrado en dispositivos enrolados.", isAuthorized = false });

        var secret = _encryption.Decrypt(device.EncryptedSecret);
        var result = ATUCore.ValidateFIFO(req.Code, secret, claimedBatchId, actualBatchId, supId);

        // ═══════════════════════════════════════════════════════════════
        // LÓGICA DE REINTENTOS Y ESTADO
        // ═══════════════════════════════════════════════════════════════
        string? newDbStatus = null;
        bool isTerminalEvent = false;

        if (result.IsAuthorized)
        {
            newDbStatus = "AUTHORIZED";
            isTerminalEvent = true;
        }
        else if (result.Message.Contains("FRAUDE", StringComparison.OrdinalIgnoreCase))
        {
            newDbStatus = "FRAUD";
            isTerminalEvent = true;
        }
        else if (result.Status == ATUStatus.Yellow)
        {
            newDbStatus = "EXPIRED";
            isTerminalEvent = true;
        }

        var currentAttempts = folio.OtpIntentos;
        var newAttempts = currentAttempts + 1;

        if (newAttempts >= 3 && !isTerminalEvent)
        {
            newDbStatus = "INVALID";
            isTerminalEvent = true;
        }

        var rowsAffected = await conn.ExecuteAsync(@"
        UPDATE tb_det_folio_adelantado SET 
            otp_intentos = @intentos,
            otp_status   = CASE WHEN @nuevoEstado IS NOT NULL THEN @nuevoEstado ELSE otp_status END,
            otp_usado_at = CASE WHEN @nuevoEstado = 'AUTHORIZED' THEN GETDATE() ELSE otp_usado_at END
         WHERE LTRIM(RTRIM(emb_folio)) = @f 
           AND otp_status = 'PENDING'",
                new
                {
                    intentos = newAttempts,
                    nuevoEstado = newDbStatus,
                    f = req.EmbFolio.Trim()
                });

        if (rowsAffected == 0 && isTerminalEvent)
        {
            return Ok(new { success = false, status = "Red", message = "El folio ya fue procesado por otro usuario.", isAuthorized = false });
        }

        string userMessage = result.Message;
        if (newDbStatus == null && newAttempts < 3)
        {
            userMessage = $"Código incorrecto. Intento {newAttempts} de 3. Verifica el código e intenta de nuevo.";
        }
        else if (newDbStatus == "INVALID" && newAttempts >= 3)
        {
            userMessage = "Se excedió el máximo de 3 intentos. Debe cancelarse y generar una nueva solicitud.";
        }
        // ═══════════════════════════════════════════════════════════════

        if (result.IsAuthorized)
        {
            var stored = await _otps.GetByBatchAndSupervisorAsync(claimedBatchId, supId);
            if (stored != null) { stored.IsUsed = true; stored.UsedAt = DateTimeOffset.Now; await _otps.UpdateAsync(stored); }
        }

        var eventoColor = newDbStatus == "AUTHORIZED" ? "green" : newDbStatus == "FRAUD" ? "red" : "yellow";
        var eventoTitle = newDbStatus switch
        {
            "AUTHORIZED" => "✅ Folio Adelantado Autorizado",
            "FRAUD" => "🚨 FRAUDE DETECTADO",
            "EXPIRED" => "⏱ OTP Expirado al validar",
            "INVALID" => "❌ Máximo de intentos alcanzado",
            _ => "⚠️ Intento fallido de OTP"
        };

        await EmitirAuditEvent(eventoColor, eventoTitle, actualBatchId, supId, req.EmbFolio,
            userMessage, newDbStatus == "FRAUD");

        await InsertAudit(conn, req.EmbFolio,
            newDbStatus == "AUTHORIZED" ? "OTP_VALIDATED" : (newDbStatus ?? "FAILED_ATTEMPT"),
            supId, null, req.ActualProdClave, req.ActualRecibo, req.ActualTarima.ToString(),
            userMessage);

        return Ok(new
        {
            success = result.IsAuthorized,
            status = result.Status.ToString(),
            message = userMessage,
            isAuthorized = result.IsAuthorized,
            supervisorId = supId
        });
    }

    [HttpPost("authorize-folio")]
    public async Task<IActionResult> AuthorizeFolio([FromBody] AuthorizeFolioRequest request)
    {
        if (string.IsNullOrWhiteSpace(_connStr))
            return Ok(new { success = false, message = "SQL Server no configurado." });

        await using var conn = new SqlConnection(_connStr);

        try
        {
            // 1. Validar que el supervisor esté autorizado en Tb_Autoriza_OdeP
            var esAutorizado = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM Tb_Autoriza_OdeP
            WHERE LTRIM(RTRIM(usuario)) = @u AND LTRIM(RTRIM(clave)) = 'EM'",
                new { u = request.SupervisorId.Trim() });

            if (esAutorizado == 0)
                return Ok(new { success = false, message = "Supervisor no autorizado en el sistema." });

            // 2. Obtener el folio pendiente
            var folio = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT emb_folio, prod_clave, recibo_cap, tarimacap, recibo_sug, tarimasug, 
                   producto, otp_status
            FROM tb_det_folio_adelantado
            WHERE LTRIM(RTRIM(emb_folio)) = @f AND otp_status = 'PENDING'",
                new { f = request.EmbFolio.Trim() });

            if (folio == null)
                return Ok(new { success = false, message = "Folio no encontrado o ya procesado." });

            // 3. Actualizar estado a AUTHORIZED (sin OTP)
            var rowsAffected = await conn.ExecuteAsync(@"
            UPDATE tb_det_folio_adelantado SET 
                otp_status = 'AUTHORIZED',
                autorizo = @sup,
                otp_generado_por = @sup,
                otp_generado_at = GETDATE(),
                otp_usado_at = GETDATE()
            WHERE LTRIM(RTRIM(emb_folio)) = @f AND otp_status = 'PENDING'",
                new { sup = request.SupervisorId.Trim(), f = request.EmbFolio.Trim() });

            if (rowsAffected == 0)
                return Ok(new { success = false, message = "El folio ya fue procesado por otro usuario." });

            // 4. Construir BatchId para auditoría
            var batchId = ATUCore.BuildBatchId(
                (string)folio.recibo_cap,
                (string)folio.prod_clave,
                ((int)folio.tarimacap).ToString());

            // 5. Emitir evento SignalR (FolioAutorizado)
            await _auditHub.Clients.All.SendAsync("FolioAutorizado", new
            {
                EmbFolio = request.EmbFolio.Trim(),
                BatchId = batchId,
                SupervisorId = request.SupervisorId.Trim(),
                AuthorizedAt = DateTime.Now,
                Message = $"Lote {batchId} autorizado remotamente por {request.SupervisorId}",
                Comments = request.Comments ?? "Autorización remota sin OTP"
            });

            // 6. Emitir evento al feed de auditoría
            await EmitirAuditEvent("green", "✅ Autorización remota", batchId,
                request.SupervisorId, "Sistema",
                $"Folio {request.EmbFolio} autorizado sin OTP por {request.SupervisorId}");

            // 7. Guardar en tabla de auditoría (si existe)
            await InsertAudit(conn, request.EmbFolio, "AUTHORIZED_REMOTE",
                request.SupervisorId, request.DeviceFingerprint,
                folio.prod_clave, folio.recibo_cap, folio.tarimacap.ToString(),
                $"Autorización remota - {request.Comments}");

            return Ok(new AuthorizeFolioResponse
            {
                Success = true,
                Message = "Folio autorizado exitosamente",
                AuthorizationId = Guid.NewGuid().ToString(),
                AuthorizedAt = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = $"Error interno: {ex.Message}" });
        }
    }


    // ═══════════════════════════════════════════════════════════════════════
    // Helpers privados
    // ═══════════════════════════════════════════════════════════════════════

    private async Task EmitirAuditEvents(string status, string title, string batchId,
        string supervisorId, string operatorId, string message, bool isFraud = false)
    {
        try
        {
            await _auditHub.Clients.All.SendAsync("AuditEvent", new
            {
                status,
                title,
                batchId,
                supervisorId,
                operatorId,
                message,
                isFraud,
                timestamp = DateTime.Now,
                eventId = Guid.NewGuid()
            });
        }
        catch { /* No interrumpir el flujo si SignalR falla */ }
    }
    private async Task EmitirAuditEvent(string status, string title, string batchId,
    string supervisorId, string operatorId, string message, bool isFraud = false)
    {
        try
        {
            await _auditHub.Clients.All.SendAsync("AuditEvent", new
            {
                status,
                title,
                batchId,
                supervisorId,
                operatorId,
                message,
                isFraud,
                timestamp = DateTime.Now,
                eventId = Guid.NewGuid()
            });
        }
        catch (Exception ex)
        {
            // ✅ NUEVO: Imprimir el error en la consola del AuthService
            Console.WriteLine($"⚠️ ERROR DE SIGNALR: {ex.Message}");
        }
    }

    private static async Task InsertAudit(SqlConnection conn, string embFolio, string evento,
        string supervisorId, string? deviceFp, string prodClave, string recibo, string tarima,
        string mensaje = "")
    {
        try
        {
            await conn.ExecuteAsync(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='tb_atu_audit')
                INSERT INTO tb_atu_audit
                    (emb_folio,evento,supervisor_id,device_fp,prod_clave,recibo,tarima,mensaje,created_at)
                VALUES (@embFolio,@evento,@supervisorId,@deviceFp,@prodClave,@recibo,@tarima,@mensaje,GETDATE())",
                new { embFolio, evento, supervisorId, deviceFp, prodClave, recibo, tarima, mensaje });
        }
        catch { /* Auditoría no debe interrumpir flujo */ }
    }

    private static string ComputeOtpHash(string otp, string folio)
    {
        using var sha = SHA256.Create();
        var input = Encoding.UTF8.GetBytes($"{otp}:{folio}:ATU-SALT-2024");
        return Convert.ToHexString(sha.ComputeHash(input));
    }

    private static string Truncate(string s, int max)
        => s.Length > max ? s[..max] : s;
}
