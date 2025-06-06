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
        private FileService _fileService;

        public ChatHub(DefaultDbContext context)
        {
            _context = context;
        }

        // Отправка сообщения с поддержкой файлов
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

            // Загружаем связанный файл
            await _context.Entry(newMessage).Reference(m => m.File).LoadAsync();

            var recipients = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            // Формируем DTO с именем файла
            var messageDto = new MessageDto
            {
                MessageId = newMessage.MessageId,
                Content = newMessage.Content,
                UserID = userId,
                CreatedAt = (DateTime)newMessage.CreatedAt,
                FileId = newMessage.FileId,
                FileName = newMessage.File?.FileName ?? string.Empty, // Важно!
                FileType = newMessage.File?.FileType ?? string.Empty
            };

            // Обновляем статусы и отправляем сообщение
            foreach (var recipientId in recipients)
            {
                await UpdateMessageStatus(newMessage.MessageId, recipientId, (int)MessageStatusType.Delivered);
            }

            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", messageDto);
        }

        // Отправка сообщения с файлом
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

        // Пометка сообщений как прочитанных
        public async Task MarkMessagesAsRead(int chatId, int userId)
        {
            // 1) Смотрим, какие сообщения ещё «не прочитаны»
            var unreadMessages = await _context.Messages
                .Include(m => m.MessageStatuses)
                .Where(m => m.ChatId == chatId
                    && m.SenderId != userId
                    && (m.MessageStatuses.All(ms => ms.UserId != userId)
                        || m.MessageStatuses.Any(ms => ms.UserId == userId && ms.Status < (int)MessageStatusType.Read)))
                .ToListAsync();

            if (!unreadMessages.Any())
                return;

            var now = DateTime.UtcNow;
            var readStatus = (int)MessageStatusType.Read;
            var messageIds = unreadMessages.Select(m => m.MessageId).ToList();

            // 2) Подгружаем уже существующие статусы из БД
            var existingStatuses = await _context.MessageStatuses
                .Where(ms => ms.UserId == userId && messageIds.Contains(ms.MessageId))
                .ToListAsync();

            var statusDtos = new List<StatusDto>();

            foreach (var msg in unreadMessages)
            {
                var exist = existingStatuses.FirstOrDefault(ms => ms.MessageId == msg.MessageId);
                if (exist != null)
                {
                    // 3a) Обновляем запись
                    exist.Status = readStatus;
                    exist.UpdatedAt = now;
                }
                else
                {
                    // 3b) Создаем новую
                    var ms = new MessageStatus
                    {
                        MessageId = msg.MessageId,
                        UserId = userId,
                        Status = readStatus,
                        UpdatedAt = now
                    };
                    _context.MessageStatuses.Add(ms);
                    existingStatuses.Add(ms); // чтобы в будущем не дублировать
                }

                statusDtos.Add(new StatusDto
                {
                    MessageId = msg.MessageId,
                    Status = readStatus
                });
            }

            // 4) Сохраняем все изменения одним SaveChanges
            await _context.SaveChangesAsync();

            // 5) Пушим единым пакетом
            await Clients.Group($"chat_{chatId}")
                .SendAsync("BatchUpdateStatuses", statusDtos);
        }



        // Обновление статуса сообщения
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
        }

        // Вход в группу чата
        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
            Console.WriteLine($"Пользователь вошёл в чат: chat_{chatId}");
        }

        public async Task RegisterUser(int userId)
        {
            var groupName = $"user_{userId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            Console.WriteLine($"Connection {Context.ConnectionId} joined group {groupName}");
        }
    }
}

        public enum MessageStatusType
        {
            Sent = 0,       // Отправлено
            Delivered = 1,  // Доставлено
            Read = 2        // Прочитано
        }