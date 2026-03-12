using Microsoft.AspNetCore.SignalR;

namespace ATU.AuditService;

public class AuditHub : Hub
{
    public Task SubscribeToAuditFeed() => Groups.AddToGroupAsync(Context.ConnectionId, "audit-feed");

    public async Task EmitOtpGenerated(object payload)
        => await Clients.Group("audit-feed").SendAsync("OTPGenerated", payload);

    public async Task EmitOtpValidated(object payload)
        => await Clients.Group("audit-feed").SendAsync("OTPValidated", payload);

    public async Task EmitFraudDetected(object payload)
        => await Clients.Group("audit-feed").SendAsync("FraudDetected", payload);
}
