using MessengerServer.Controllers;
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
            var userId = Context.GetHttpContext().Request.Headers["UserId"];
            if (!string.IsNullOrEmpty(userId))
            {
                // Добавляем в группу пользователя (для личных сообщений)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
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
                var groupName = $"chat_{chatId}";
                await Clients.Group(groupName).SendAsync("ReceiveNewMessage", new MessageDto
                {
                    Id = newMessage.MessageId,
                    Content = newMessage.Content,
                    CreatedAt = (DateTime)newMessage.CreatedAt,
                    SenderId = (int)newMessage.SenderId,
                    ChatId = (int)newMessage.ChatId,
                    SenderName = _context.Users.Find(userId)?.Username
                });
                var group = Clients.Group(groupName);
                Console.WriteLine($"Количество получателей в группе: {((IReadOnlyList<string>)group).Count}");
                Console.WriteLine($"Отправлено в группу {chatId} через метод ReceiveNewMessage");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка в хабе при отправке сообщения: " + ex.ToString());
                throw new HubException("Ошибка при отправке сообщения", ex);
            }
        }

        public async Task JoinChat(string chatId)
        {
            var groupName = $"chat_{chatId}"; // Добавляем префикс
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            Console.WriteLine($"Пользователь {Context.UserIdentifier} вошел в чат {groupName}");
        }

        public async Task NotifyNewChat(int userId, Chat chat)
        {
            await Clients.User(userId.ToString()).SendAsync("ReceiveNewChat", chat);
        }

        public async Task Ping(string message) // тест
        {
            await Clients.Caller.SendAsync("Pong", message);
        }
    }
}