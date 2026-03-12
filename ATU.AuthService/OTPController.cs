using ATU.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ATU.AuthService;

public static class OTPController
{
    public static IEndpointRouteBuilder MapOtpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/otp/generate", GenerateOtp);
        app.MapPost("/otp/validate", ValidateOtp);
        return app;
    }

    private static async Task<IResult> GenerateOtp(
        [FromBody] GenerateOtpRequest request,
        DeviceEnrollmentService enrollmentService,
        IOtpRepository otpRepository)
    {
        var device = await enrollmentService.ValidateDevice(request.OperatorId, request.HardwareId, request.UserAgent, request.Platform);
        if (!device.IsValid || string.IsNullOrWhiteSpace(device.Secret))
        {
            return Results.Unauthorized();
        }

        var otp = ATUCore.GenerateOTP(device.Secret, request.BatchId, request.SupervisorId);
        var record = new OtpRecord
        {
            Otp = otp,
            BatchId = request.BatchId,
            SupervisorId = request.SupervisorId,
            OperatorId = request.OperatorId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ATUCore.TtlSeconds)
        };

        await otpRepository.Save(record);
        return Results.Ok(new GenerateOtpResponse(otp, record.ExpiresAt));
    }

    private static async Task<IResult> ValidateOtp(
        [FromBody] ValidateOtpRequest request,
        DeviceEnrollmentService enrollmentService,
        IOtpRepository otpRepository,
        IGeofenceService geofenceService)
    {
        var device = await enrollmentService.ValidateDevice(request.OperatorId, request.HardwareId, request.UserAgent, request.Platform);
        if (!device.IsValid || string.IsNullOrWhiteSpace(device.Secret))
        {
            return Results.Unauthorized();
        }

        var geofence = geofenceService.ValidateProximity(request.OperatorCoordinate, request.SupervisorCoordinate);
        if (!geofence.IsAllowed)
        {
            return Results.BadRequest(new { geofence.Message, geofence.DistanceMeters });
        }

        var stored = await otpRepository.GetLatest(request.BatchId, request.SupervisorId);
        if (stored is not null && stored.IsUsed)
        {
            return Results.Ok(new ValidateOtpResponse(false, OtpValidationStatus.ReplayAttack, "OTP ya utilizado."));
        }

        var result = ATUCore.ValidateOTP(request.Otp, device.Secret, request.ClaimedBatchId, request.BatchId, request.SupervisorId);
        if (result.IsValid && stored is not null)
        {
            stored.IsUsed = true;
            stored.UsedAt = DateTimeOffset.UtcNow;
            await otpRepository.Update(stored);
        }

        return Results.Ok(new ValidateOtpResponse(result.IsValid, result.Status, result.Message));
    }
}

public sealed record GenerateOtpRequest(string OperatorId, string SupervisorId, string BatchId, string HardwareId, string UserAgent, string Platform);
public sealed record GenerateOtpResponse(string Otp, DateTimeOffset ExpiresAt);

public sealed record ValidateOtpRequest(
    string Otp,
    string OperatorId,
    string SupervisorId,
    string BatchId,
    string ClaimedBatchId,
    string HardwareId,
    string UserAgent,
    string Platform,
    Coordinate OperatorCoordinate,
    Coordinate SupervisorCoordinate);

public sealed record ValidateOtpResponse(bool IsValid, OtpValidationStatus Status, string Message);

public sealed class OtpRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Otp { get; init; }
    public required string BatchId { get; init; }
    public required string SupervisorId { get; init; }
    public required string OperatorId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsUsed { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public interface IOtpRepository
{
    Task Save(OtpRecord record);
    Task<OtpRecord?> GetLatest(string batchId, string supervisorId);
    Task Update(OtpRecord record);
}
