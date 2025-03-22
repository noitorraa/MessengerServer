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

            if (chatUserIds.Any())
            {
                var statuses = chatUserIds.Select(uid => new MessageStatus
                {
                    MessageId = newMessage.MessageId,
                    UserId = uid,
                    Status = false, // Помечаем как непрочитанное
                    UpdatedAt = DateTime.UtcNow
                }).ToList();


                _context.MessageStatuses.AddRange(statuses);
                await _context.SaveChangesAsync();

                // Отправляем сообщение через SignalR
                await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage",
                    newMessage.Content,
                    newMessage.SenderId,
                    newMessage.MessageId,
                    newMessage.FileId,
                    newMessage.File?.FileType,
                    newMessage.File?.FileUrl); // Передаем MessageId
            }
            else
            {
                throw new Exception("нету id юзеров (пусто)");
            }    
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

            // Включаем связанную сущность Message и фильтруем статусы
            var statuses = await _context.MessageStatuses
                .Include(ms => ms.Message) // Загружаем связанное сообщение
                .Where(ms =>
                    messageIds.Contains((int)ms.MessageId) &&
                    ms.UserId == userId &&
                    !ms.Status
                )
                .ToListAsync();

            if (!statuses.Any())
            {
                Console.WriteLine("Нет статусов для обновления.");
                return;
            }

            // Обновляем статусы
            foreach (var status in statuses)
            {
                status.Status = true;
                status.UpdatedAt = DateTime.UtcNow;
            }

            // Сохраняем изменения
            await _context.SaveChangesAsync();

            // Получаем chatId из первого сообщения (предполагаем, что все messageIds из одного чата)
            var chatId = statuses.First().Message.ChatId;

            // Отправляем обновление в группу чата
            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessageStatusUpdate", messageIds, userId);
        }


        // Вход в группу чата
        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
            Console.WriteLine($"Пользователь вошел в чат: chat_{chatId}");
        }
    }
}
