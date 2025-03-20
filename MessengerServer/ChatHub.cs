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
        private DefaultDbContext _context;

        public ChatHub(DefaultDbContext context)
        {
            _context = context;
        }

        // Отправка сообщения в группу чата
        public async Task SendMessage(int userId, string message, int chatId, int? fileid = null)
        {
            // Сохраняем сообщение
            var newMessage = new Message
            {
                Content = message,
                SenderId = userId,
                ChatId = chatId,
                CreatedAt = DateTime.UtcNow,
                FileId = fileid
            };
            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            var chatUserIds = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId) // Исключаем отправителя
                .Select(cm => cm.UserId)
                .ToListAsync();

            Console.WriteLine($"chatuserids = {chatUserIds.ToList()}");
            
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
                newMessage.MessageId,
                newMessage.FileId,
                newMessage.File?.FileType,
                newMessage.File?.FileUrl); // Передаем MessageId
        }

        public async Task SendFileMessage(int userId, int fileId, int chatId)
        {
            var file = await _context.Files
                .FirstOrDefaultAsync(f => f.FileId == fileId); // Явная загрузка файла

            if (file == null) return;

            var message = new Message
            {
                SenderId = userId,
                ChatId = chatId,
                FileId = fileId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Добавлена передача контента для унификации
            await Clients.Group($"chat_{chatId}")
                .SendAsync("ReceiveMessage",
                    "", // Пустой текст
                    userId,
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
            var statuses = await _context.MessageStatuses
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
            await _context.SaveChangesAsync();

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
