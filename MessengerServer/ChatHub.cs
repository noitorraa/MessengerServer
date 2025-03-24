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

            var recipients = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            // Формируем DTO с корректным IsRead
            var messageDto = new MessageDto
            {
                MessageId = newMessage.MessageId,
                Content = newMessage.Content,
                UserID = (int)newMessage.SenderId,
                CreatedAt = (DateTime)newMessage.CreatedAt,
                FileId = newMessage.FileId,
                FileType = newMessage.File?.FileType,
                FileUrl = newMessage.File?.FileUrl
            };
            foreach (var recipientId in recipients)
            {
                await UpdateMessageStatus(newMessage.MessageId, recipientId, (int)MessageStatusType.Delivered);
            }
            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", messageDto);
        }

        public async Task MarkMessagesAsRead(int chatId, int userId)
        {
            // Исправленное условие выборки непрочитанных сообщений
            var unreadMessages = await _context.Messages
                .Include(m => m.MessageStatuses) // Убедиться, что статусы загружены
                .Where(m => m.ChatId == chatId &&
                           m.SenderId != userId && // Только чужие сообщения
                           (!m.MessageStatuses.Any(ms => ms.UserId == userId) || // Нет статуса
                            m.MessageStatuses.Any(ms => ms.UserId == userId && ms.Status < (int)MessageStatusType.Read))) // Статус < Read
                .ToListAsync();

            foreach (var message in unreadMessages)
            {
                await UpdateMessageStatus(message.MessageId, userId, (int)MessageStatusType.Read);
            }

            await Clients.Group($"chat_{chatId}").SendAsync("RefreshMessages");
        }

        public async Task MessageDelivered(int messageId, int userId)
        {
            Console.WriteLine($"для {messageId} статус изменен на Delivered");
            await UpdateMessageStatus(messageId, userId, (int)MessageStatusType.Delivered);
        }

        private async Task UpdateMessageStatus(int messageId, int recipientId, int status)
        {
            var statusEntry = await _context.MessageStatuses
                .FirstOrDefaultAsync(ms =>
                    ms.MessageId == messageId &&
                    ms.UserId == recipientId // Важно: получатель!
                );

            if (statusEntry == null)
            {
                statusEntry = new MessageStatus
                {
                    MessageId = messageId,
                    UserId = recipientId, // ID пользователя, для которого обновляется статус
                    Status = status,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.MessageStatuses.Add(statusEntry);
            }
            else
            {
                statusEntry.Status = status;
                statusEntry.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task SendFileMessage(int userId, int fileId, int chatId)
        {
            var file = await _context.Files
                .Include(f => f.Messages)
                .FirstOrDefaultAsync(f => f.FileId == fileId);

            if (file == null) return;

            var message = new Message
            {
                SenderId = userId,
                ChatId = chatId,
                FileId = fileId,
                CreatedAt = DateTime.UtcNow,
                Content = "[Файл]"
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Формируем DTO
            var messageDto = new MessageDto
            {
                MessageId = message.MessageId,
                UserID = userId,
                CreatedAt = (DateTime)message.CreatedAt,
                FileId = file.FileId,
                FileType = file.FileType,
                FileUrl = file.FileUrl,
                Status = (int)MessageStatusType.Sent
            };

            // Отправка через общий обработчик
            await Clients.Group($"chat_{chatId}")
                .SendAsync("ReceiveMessage", messageDto);

            // Обновление статусов
            var recipients = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            foreach (var recipientId in recipients)
            {
                await UpdateMessageStatus(message.MessageId, recipientId, (int)MessageStatusType.Delivered);
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
