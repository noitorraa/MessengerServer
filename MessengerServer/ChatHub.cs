using MessengerServer.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly MessengerDataBaseContext _context;
        public ChatHub(MessengerDataBaseContext context)
        {
            _context = context;
        }

        public override async Task OnConnectedAsync()
        {
            // Связываем подключение с пользователем
            var userId = Context.GetHttpContext().Request.Headers["UserId"];
            if (!string.IsNullOrEmpty(userId))
            {
                Context.Items["UserId"] = userId;
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            }
            await base.OnConnectedAsync();
        }

        public async Task SendMessage(int userId, string message, int chatId)
        {
            try
            {
                var newMessage = new Message
                {
                    Content = message,
                    CreatedAt = DateTime.Now,
                    SenderId = userId,
                    ChatId = chatId
                };
                _context.Messages.Add(newMessage);
                await _context.SaveChangesAsync();

                // Уведомляем пользователей чата о новом сообщении
                var chatMembers = await _context.ChatMembers
                    .Where(cm => cm.ChatId == chatId)
                    .Select(cm => cm.UserId)
                    .ToListAsync();
                foreach (var memberId in chatMembers)
                {
                    await Clients.All.SendAsync("ReceiveNewMessage", newMessage);
                    //await Clients.Group(memberId.ToString()).SendAsync("ReceiveNewMessage", newMessage);
                    Console.WriteLine("Сообщение отправлено: " + memberId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка в хабе при отправке сообщения: " + ex.ToString());
                throw new HubException("Ошибка при отправке сообщения", ex);
            }
        }

        public async Task NotifyNewChat(int userId, Chat chat)
        {
            await Clients.User(userId.ToString()).SendAsync("ReceiveNewChat", chat);
        }
    }
}