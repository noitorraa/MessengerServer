using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using static MessengerServer.Controllers.UsersController;
using MessengerServer.Services;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly DefaultDbContext _context;
        private readonly FileService _fileService;

        public ChatHub(DefaultDbContext context, FileService fileService)
        {
            _context = context;
            _fileService = fileService;
        }

        public async Task SendMessage(int userId, string message, int chatId, int? fileId = null)
        {
            var newMessage = new Message
            {
                Content = message,
                SenderId = userId,
                ChatId = chatId,
                CreatedAt = DateTime.UtcNow,
                FileId = fileId
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            await _context.Entry(newMessage).Reference(m => m.File).LoadAsync();

            var recipients = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            var messageDto = new MessageDto
            {
                MessageId = newMessage.MessageId,
                Content = newMessage.Content,
                UserID = userId,
                CreatedAt = (DateTime)newMessage.CreatedAt,
                FileId = newMessage.FileId,
                FileName = newMessage.File?.FileName ?? string.Empty,
                FileType = newMessage.File?.FileType ?? string.Empty
            };

            foreach (var recipientId in recipients)
            {
                await UpdateMessageStatus(newMessage.MessageId, recipientId, (int)MessageStatusType.Delivered);
            }

            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", messageDto);
        }

        public async Task SendFileMessage(int userId, int fileId, int chatId)
        {
            var file = await _context.Files.FindAsync(fileId);
            if (file == null)
            {
                await Clients.Caller.SendAsync("Error", "Файл не найден");
                return;
            }

            var fileUrl = _fileService.GetFileUrl(fileId);

            var newMessage = new Message
            {
                Content = "Файл",
                SenderId = userId,
                ChatId = chatId,
                CreatedAt = DateTime.UtcNow,
                FileId = fileId
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            var messageDto = new MessageDto
            {
                MessageId = newMessage.MessageId,
                Content = newMessage.Content,
                UserID = userId,
                CreatedAt = (DateTime)newMessage.CreatedAt,
                FileId = fileId,
                FileName = file.FileName,
                FileType = file.FileType,
                FileUrl = fileUrl
            };

            var recipients = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            foreach (var recipientId in recipients)
            {
                await UpdateMessageStatus(newMessage.MessageId, recipientId, (int)MessageStatusType.Delivered);
            }

            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", messageDto);
        }

        public async Task MarkMessagesAsRead(int chatId, int userId)
        {
            // Исправленный запрос для непрочитанных сообщений
            var unreadMessages = await _context.Messages
                .Include(m => m.MessageStatuses)
                .Where(m => m.ChatId == chatId && m.SenderId != userId)
                .Where(m => m.MessageStatuses.All(ms => ms.UserId != userId) || 
                           m.MessageStatuses.Any(ms => ms.UserId == userId && ms.Status < (int)MessageStatusType.Read))
                .ToListAsync();

            if (unreadMessages.Count == 0)
                return;

            var now = DateTime.UtcNow;
            var readStatus = (int)MessageStatusType.Read;
            var messageIds = unreadMessages.Select(m => m.MessageId).ToList();

            var existingStatuses = await _context.MessageStatuses
                .Where(ms => ms.UserId == userId && messageIds.Contains(ms.MessageId))
                .ToListAsync();

            var statusDtos = new List<StatusDto>();

            foreach (var msg in unreadMessages)
            {
                var exist = existingStatuses.FirstOrDefault(ms => ms.MessageId == msg.MessageId);
                if (exist != null)
                {
                    exist.Status = readStatus;
                    exist.UpdatedAt = now;
                }
                else
                {
                    var ms = new MessageStatus
                    {
                        MessageId = msg.MessageId,
                        UserId = userId,
                        Status = readStatus,
                        UpdatedAt = now
                    };
                    _context.MessageStatuses.Add(ms);
                    existingStatuses.Add(ms);
                }

                statusDtos.Add(new StatusDto
                {
                    MessageId = msg.MessageId,
                    Status = readStatus
                });
            }

            await _context.SaveChangesAsync();
            await Clients.Group($"user_{userId}").SendAsync("BatchUpdateStatuses", statusDtos);
        }

        private async Task UpdateMessageStatus(int messageId, int userId, int status)
        {
            var statusEntry = await _context.MessageStatuses
                .FirstOrDefaultAsync(ms => ms.MessageId == messageId && ms.UserId == userId);

            if (statusEntry == null)
            {
                statusEntry = new MessageStatus
                {
                    MessageId = messageId,
                    UserId = userId,
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
            
            var statusDto = new StatusDto
            {
                MessageId = messageId,
                Status = status
            };
            
            await Clients.Group($"user_{userId}").SendAsync("UpdateMessageStatus", statusDto);
        }

        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        }

        public async Task RegisterUser(int userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
    }
    
    public enum MessageStatusType
    {
        Sent = 0,
        Delivered = 1,
        Read = 2
    }
}