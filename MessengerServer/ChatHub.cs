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
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            var statuses = chatUserIds.Select(uid => new MessageStatus
            {
                MessageId = newMessage.MessageId,
                UserId = uid,
                Status = false,
                UpdatedAt = DateTime.UtcNow
            }).ToList();

            _context.MessageStatuses.AddRange(statuses);
            await _context.SaveChangesAsync();

            // Формируем DTO с корректным IsRead
            var messageDto = new MessageDto
            {
                MessageId = newMessage.MessageId,
                Content = newMessage.Content,
                UserID = (int)newMessage.SenderId,
                CreatedAt = (DateTime)newMessage.CreatedAt,
                IsRead = false, // Для новых сообщений IsRead = false
                FileId = newMessage.FileId,
                FileType = newMessage.File?.FileType,
                FileUrl = newMessage.File?.FileUrl
            };

            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", messageDto);
        }

        public async Task SendFileMessage(int userId, int fileId, int chatId) // нужно поменять, DTO отправлять
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
            if (messageIds == null || !messageIds.Any())
            {
                return; // Игнорируем пустые запросы
            }

            var statuses = await _context.MessageStatuses
                .Include(ms => ms.Message) // Убедитесь, что связь настроена
                .Where(ms =>
                    messageIds.Contains((int)ms.MessageId) &&
                    ms.UserId == userId &&
                    !ms.Status)
                .ToListAsync();

            if (!statuses.Any())
            {
                return; // Нечего обновлять
            }

            foreach (var status in statuses)
            {
                status.Status = true;
                status.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            // Получаем chatId из первого сообщения
            var firstMessage = statuses.FirstOrDefault()?.Message;
            if (firstMessage?.ChatId == null)
            {
                Console.WriteLine("ошибка определения id чата");
                return;
            }

            await Clients.Group($"chat_{firstMessage.ChatId}")
                .SendAsync("ReceiveMessageStatusUpdate", messageIds, userId);
        }


        // Вход в группу чата
        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
            Console.WriteLine($"Пользователь вошел в чат: chat_{chatId}");
        }
    }
}
