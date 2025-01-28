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

                // Отправляем сообщение всем клиентам
                await Clients.All.SendAsync("ReceiveMessage", userId, message);

                // Уведомляем пользователей чата о новом сообщении
                var chatMembers = await _context.ChatMembers
                    .Where(cm => cm.ChatId == chatId)
                    .Select(cm => cm.UserId)
                    .ToListAsync();
                foreach (var memberId in chatMembers)
                {
                    await Clients.User(memberId.ToString()).SendAsync("ReceiveNewMessage", newMessage);
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