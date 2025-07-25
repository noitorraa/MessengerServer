using Microsoft.AspNetCore.SignalR;
using MessengerServer.Hubs;

namespace MessengerServer.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IHubContext<ChatHub> _hubContext;

        public NotificationService(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task NotifyChatListUpdate(List<int> userIds)
        {
            foreach (var id in userIds)
            {
                await _hubContext.Clients
                    .Group($"user_{id}")
                    .SendAsync("NotifyUpdateChatList");
            }
        }
    }

    public interface INotificationService
    {
        Task NotifyChatListUpdate(List<int> userIds);
    }
}