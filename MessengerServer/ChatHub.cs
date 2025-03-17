using MessengerServer.Controllers;
using MessengerServer.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using static MessengerServer.Controllers.UsersController;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        //private MessengerDataBaseContext _context;

        public ChatHub(DefaultDbContext context)
        {
            context = DefaultDbContext.GetContext();
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
            DefaultDbContext.GetContext().Messages.Add(newMessage);
            await DefaultDbContext.GetContext().SaveChangesAsync();

            var chatUserIds = await DefaultDbContext.GetContext().ChatMembers
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

                DefaultDbContext.GetContext().MessageStatuses.AddRange(statuses);
                await DefaultDbContext.GetContext().SaveChangesAsync();
            }

            // Отправляем сообщение через SignalR
            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage",
                newMessage.Content,
                newMessage.SenderId,
                newMessage.MessageId); // Передаем MessageId
        }

        public async Task SendFileMessage(int userId, int fileId, int chatId)
        {
            var file = await DefaultDbContext.GetContext().Files.FindAsync(fileId);
            if (file == null) throw new Exception("Файл не найден");

            var message = new Message
            {
                SenderId = userId,
                ChatId = chatId,
                FileId = fileId, // Связь с файлом
                CreatedAt = DateTime.UtcNow
            };

            DefaultDbContext.GetContext().Messages.Add(message);
            await DefaultDbContext.GetContext().SaveChangesAsync();

            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveFileMessage",
                message.SenderId,
                message.MessageId,
                fileId,
                file.FileType,
                file.FileUrl);
        }

        public async Task UpdateMessageStatusBatch(List<int> messageIds, int userId)
        {
            // Проверка на null и пустой список
            if (messageIds == null || !messageIds.Any())
            {
                Console.WriteLine("Ошибка: messageIds пустой или null.");
                return;
            }

            // Получаем статусы для переданных messageIds
            var statuses = await DefaultDbContext.GetContext().MessageStatuses
            .Include(ms => ms.Message)
            .Where(ms => messageIds.Contains((int)ms.MessageId) && ms.UserId == userId && !ms.Status)
            .ToListAsync();

            // Проверка на null или пустой список
            if (statuses == null || !statuses.Any())
            {
                Console.WriteLine("Ошибка: Не найдены статусы сообщений для обновления.");
                return;
            }

            foreach (var status in statuses)
            {
                status.Status = true; // Обновляем статус на "прочитано"
                status.UpdatedAt = DateTime.UtcNow;
            }

            // Сохраняем изменения в базе данных
            await DefaultDbContext.GetContext().SaveChangesAsync();

            // Логирование успешного обновления
            Console.WriteLine($"Статус сообщений обновлен для пользователя {userId}");

            // Отправляем обновления через SignalR
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
