using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Models;
using Org.BouncyCastle.Crypto.Generators;
using Microsoft.Extensions.DependencyInjection;
using MessengerServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNet.SignalR;
using Org.BouncyCastle.Asn1.Ocsp;
using Newtonsoft.Json;

namespace MessengerServer.Controllers
{
    [ApiController] // тут получаем данные из базы данных 
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly DefaultDbContext _context;
        private ChatHub _chatHub;
        private readonly IServiceProvider _serviceProvider;

        public UsersController(DefaultDbContext context, IServiceProvider serviceProvider, IWebHostEnvironment env)
        {
            _env = env;
            _context = context;
            _serviceProvider = serviceProvider;
        }

        [HttpGet("authorization")]
        public async Task<ActionResult<User>> GetUserByLoginAndPassword(string login, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == login);

            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                return NotFound(new { Message = "Пользователь не найден" });
            }

            return Ok(user);
        }

        [HttpGet("chats/{userId}")]
        public async Task<ActionResult<List<ChatDto>>> GetUserChats(int userId)
        {
            var chats = await _context.Chats
                .Where(c => c.ChatMembers.Any(cm => cm.UserId == userId))
                .Include(c => c.ChatMembers)
                .ThenInclude(cm => cm.User)
                .Select(c => new ChatDto
                {
                    ChatId = c.ChatId,
                    Members = c.ChatMembers.Select(cm => new UserDto
                    {
                        UserId = cm.User.UserId,
                        Username = cm.User.Username
                    }).ToList(),
                    CreatedAt = (DateTime)c.CreatedAt
                })
                .ToListAsync();

            return Ok(chats);
        }

        [HttpGet("chats/{chatId}/{_userId}/messages")]
        public async Task<IActionResult> GetMessages(int userId, int chatId)
        {
            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId)
                .Select(m => new MessageDto
                {
                    MessageId = m.MessageId,
                    Content = m.Content,
                    UserID = (int)m.SenderId,
                    CreatedAt = (DateTime)m.CreatedAt,
                    IsRead = _context.MessageStatuses
                        .Any(ms => ms.MessageId == m.MessageId && ms.UserId == userId && ms.Status),
                    FileId = m.FileId,
                    FileType = m.File != null ? m.File.FileType : null,
                    FileUrl = m.File != null ? m.File.FileUrl : null
                })
                .ToListAsync();

            return Ok(messages);
        }

        }

        [HttpDelete("chats/{chatId}")]
        public async Task<IActionResult> DeleteChat(int chatId)
        {
            var chat = await _context.Chats
                .Include(c => c.ChatMembers)
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.ChatId == chatId);

            if (chat == null)
                return NotFound(new { Message = "Чат не найден" });

            _context.Messages.RemoveRange(chat.Messages);

            _context.ChatMembers.RemoveRange(chat.ChatMembers);

            _context.Chats.Remove(chat);

            await _context.SaveChangesAsync();

            var hubContext = _serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<ChatHub>>();
            await hubContext.Clients.Users(chat.ChatMembers.Select(cm => cm.UserId.ToString()).ToList()).SendAsync("NotifyUpdateChatList");

            return Ok(new { Message = "Чат успешно удален" });
        }

        [HttpPost("registration")]
        public async Task<IActionResult> Registration([FromBody] User user)
        {
            if (await _context.Users.AnyAsync(u => u.Username == user.Username))
            {
                return Conflict(new { Message = "Username already exists." });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Хешируем пароль перед сохранением
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "User registered successfully." });
        }

        [HttpGet("search")]
        public async Task<ActionResult<List<User>>> SearchUsersByLogin(string login)
        {
            // Ищем пользователей, у которых логин содержит переданную строку
            var users = await _context.Users
                                      .Where(u => u.Username.Contains(login))
                                      .ToListAsync();

            if (users == null || !users.Any())
            {
                return NotFound(new { Message = "Пользователи не найдены" });
            }

            return Ok(users);
        }

        [HttpPost("chats")]
        public async Task<ActionResult<Chat>> CreateChat([FromBody] ChatCreationRequest request)
        {
            // Проверяем существующий чат между пользователями (только для чатов с двумя участниками)
            if (request.UserIds.Count == 2)
            {
                var existingChat = await _context.Chats
                    .Include(c => c.ChatMembers)
                    .FirstOrDefaultAsync(c =>
                        c.ChatMembers.All(cm => request.UserIds.Contains(cm.UserId)) &&
                        c.ChatMembers.Count == 2
                    );

                if (existingChat != null)
                {
                    return Ok(existingChat); // Возвращаем существующий чат
                }
            }

            if (request.UserIds.Count > 2 && string.IsNullOrEmpty(request.ChatName))
            {
                request.ChatName = "New chat";
            }

            // Создаём новый чат, если дубликата нет
            var chat = new Chat
            {
                ChatName = "", // Поле больше не используется для личных чатов
                CreatedAt = DateTime.UtcNow
            };

            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            // Добавляем участников
            foreach (var userId in request.UserIds)
            {
                _context.ChatMembers.Add(new ChatMember
                {
                    ChatId = chat.ChatId,
                    UserId = userId
                });
            }

            await _context.SaveChangesAsync();

            // Отправляем уведомление через SignalR
            var hubContext = _serviceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<ChatHub>>();
            await hubContext.Clients.Users(request.UserIds.Select(u => u.ToString()).ToList())
                .SendAsync("NotifyUpdateChatList");

            return Ok(chat);
        }

        //[HttpGet("chats/{chatId}")]
        //public async Task<ActionResult<Chat>> GetChatById(int chatId)
        //{
        //    var chat = await _context.Chats
        //        .Include(c => c.ChatMembers)
        //        .FirstOrDefaultAsync(c => c.ChatId == chatId);

        //    return chat != null ? Ok(chat) : NotFound();
        //}

        [HttpGet("chats/existing")]
        public async Task<ActionResult<Chat>> GetExistingChat(int user1Id, int user2Id)
        {
            var chat = await _context.Chats
                .Include(c => c.ChatMembers)
                .FirstOrDefaultAsync(c =>
                    c.ChatMembers.Any(cm => cm.UserId == user1Id) &&
                    c.ChatMembers.Any(cm => cm.UserId == user2Id) &&
                    c.ChatMembers.Count == 2
                );

            return chat != null ? Ok(chat) : NotFound();
        }

        [HttpPost("upload/{chatId}/{userId}")]
        public async Task<IActionResult> UploadFile(int chatId, int userId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Файл не выбран");

            // Генерация уникального имени
            var fileId = Guid.NewGuid().ToString("N");
            var extension = Path.GetExtension(file.FileName);
            var fileName = $"{fileId}{extension}";
            var filePath = Path.Combine(_env.ContentRootPath, "uploads", $"chat_{chatId}", fileName);

            // Создание папки, если не существует
            var dir = Path.GetDirectoryName(filePath);
            Directory.CreateDirectory(dir);

            // Сохранение файла
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Сохранение в БД
            var dbFile = new Models.File
            {
                FileUrl = $"/uploads/chat_{chatId}/{fileName}",
                FileType = file.ContentType,
                Size = file.Length
            };

            _context.Files.Add(dbFile);
            await _context.SaveChangesAsync();

            return Ok(new { fileId = dbFile.FileId, url = dbFile.FileUrl });
        }

        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(int fileId)
        {
            var file = await _context.Files.FindAsync(fileId);
            if (file == null) return NotFound();

            var filePath = Path.Combine(_env.ContentRootPath, file.FileUrl.TrimStart('/'));
            return PhysicalFile(filePath, file.FileType);
        }

        // В UsersController.cs
        public class MessageDto
        {
            [JsonProperty("messageId")]
            public int MessageId { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }

            [JsonProperty("userID")]
            public int UserID { get; set; }

            [JsonProperty("createdAt")]
            public DateTime CreatedAt { get; set; }

            [JsonProperty("isRead")]
            public bool IsRead { get; set; }

            // Новые поля для файлов
            [JsonProperty("fileId")]
            public int? FileId { get; set; }

            [JsonProperty("fileType")]
            public string FileType { get; set; }

            [JsonProperty("fileUrl")]
            public string FileUrl { get; set; }
        }

        public class ChatDto
        {
            public int ChatId { get; set; }
            public List<UserDto> Members { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class UserDto
        {
            public int UserId { get; set; }
            public string Username { get; set; }
        }

        public class ChatCreationRequest
        {
            public string ChatName { get; set; }
            public List<int> UserIds { get; set; }
        }
    }
}