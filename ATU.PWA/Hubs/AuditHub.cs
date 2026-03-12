using Microsoft.AspNetCore.SignalR;

namespace ATU.PWA.Hubs
{
    public class AuditHub : Hub
    {
        public async Task JoinAsManager()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "managers");
        }
    }
}