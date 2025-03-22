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

        public async Task MessageDelivered(int messageId, int userId)
        {
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

        // Вход в группу чата
        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
            Console.WriteLine($"Пользователь вошел в чат: chat_{chatId}");
        }
    }
}
