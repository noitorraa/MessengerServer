using MessengerServer.Controllers;
using MessengerServer.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using static MessengerServer.Controllers.UsersController;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly MessengerDataBaseContext _context;

        public ChatHub(MessengerDataBaseContext context)
        {
            _context = context;
        }

        // Отправка сообщения в группу чата
        public async Task SendMessage(int userId, string message, int chatId)
        {
            // Сохраняем сообщение
            var newMessage = new Message
            {
                Content = message,
                SenderId = userId,
                ChatId = chatId,
                CreatedAt = DateTime.UtcNow
            };
            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            var chatUserIds = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId) // Исключаем отправителя
                .Select(cm => cm.UserId)
                .ToListAsync();

            if (chatUserIds.Any())
            {
                var statuses = chatUserIds.Select(uid => new MessageStatus
                {
                    MessageId = newMessage.MessageId,
                    UserId = uid,
                    Status = false, // Статус "не прочитано"
                    UpdatedAt = DateTime.UtcNow
                }).ToList();

                _context.MessageStatuses.AddRange(statuses);
                await _context.SaveChangesAsync();
            }

            // Отправляем сообщение через SignalR
            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage",
                newMessage.Content,
                newMessage.SenderId,
                newMessage.MessageId); // Передаем MessageId
        }

        public async Task UpdateMessageStatusBatch(List<int> messageIds, int userId)
        {
            if (messageIds == null || !messageIds.Any())
            {
                Console.WriteLine("Ошибка: messageIds пустой или null.");
                return;
            }

            var statuses = await _context.MessageStatuses
                .Where(ms => messageIds.Contains((int)ms.MessageId) && ms.UserId == userId && !ms.Status)
                .ToListAsync();

            if (statuses == null || !statuses.Any())
            {
                Console.WriteLine("Ошибка: Не найдены статусы сообщений.");
                return;
            }

            foreach (var status in statuses)
            {
                status.Status = true; // Обновляем статус на "прочитано"
                status.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            if (statuses.Any())
            {
                var chatId = statuses.First().Message.ChatId;
                await Clients.Group($"chat_{chatId}").SendAsync("UpdateMessageStatusBatch", messageIds, userId);
            }
            else
            {
                Console.WriteLine("Ошибка: Нет сообщений для обновления.");
            }
        }


        // Вход в группу чата
        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
            Console.WriteLine($"Пользователь вошел в чат: chat_{chatId}");
        }
    }
}
