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
using System.Text.Json.Serialization;
using Amazon.S3.Model;
using Amazon.S3;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNet.SignalR.Hubs;
using Amazon.DynamoDBv2.Model;
using System.Text.RegularExpressions;

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
        private static readonly Dictionary<string, string> _smsCodes = new();
        private static readonly Random _rnd = new();

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

        [HttpGet("chats/{chatId}/{userId}/messages")]
        public async Task<IActionResult> GetMessages(int userId, int chatId)
        {
            // Получаем ID второго участника чата
            var otherUserId = await _context.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != userId)
                .Select(cm => cm.UserId)
                .FirstOrDefaultAsync();

            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    Message = m,
                    StatusForCurrentUser = m.MessageStatuses
                        .Where(ms => ms.UserId == userId)
                        .Select(ms => ms.Status)
                        .FirstOrDefault(),
                    StatusForRecipient = m.MessageStatuses
                        .Where(ms => ms.UserId == otherUserId)
                        .Select(ms => ms.Status)
                        .FirstOrDefault()
                })
                .Select(x => new MessageDto
                {
                    MessageId = x.Message.MessageId,
                    Content = x.Message.Content,
                    UserID = (int)x.Message.SenderId,
                    CreatedAt = (DateTime)x.Message.CreatedAt,
                    FileId = x.Message.FileId,
                    FileType = x.Message.File != null ? x.Message.File.FileType : null,
                    FileUrl = x.Message.File != null ? x.Message.File.FileUrl : null,
                    Status = x.Message.SenderId == userId
                        ? x.StatusForRecipient // Для своих сообщений берем статус получателя
                        : x.StatusForCurrentUser // Для чужих сообщений берем свой статус
                })
                .ToListAsync();

            return Ok(messages);
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
                return BadRequest(ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
            }

            user.PhoneNumber = Regex.Replace(user.PhoneNumber ?? "", @"[^\d]", ""); // [[5]][[6]]

            // Проверка длины после очистки
            if (user.PhoneNumber.Length < 10 || user.PhoneNumber.Length > 15)
            {
                return BadRequest("Неверный формат телефона");
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

        [HttpPost("send-reset-code")]
        public ActionResult SendResetCode(string phone)
        {
            var user = _context.Users.FirstOrDefault(u => u.PhoneNumber == phone);
            if (user == null) return NotFound();

            var code = _rnd.Next(1000, 9999).ToString();
            _smsCodes[phone] = code;

            // Отправка SMS через API (пример с Twilio)
            // await _smsService.SendAsync(phone, $"Your code: {code}");

            user.SmsCodeExpires = DateTime.UtcNow.AddMinutes(5);
            _context.SaveChanges();

            return Ok();
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult> ResetPassword(ResetModel model)
        {
            if (!_smsCodes.TryGetValue(model.Phone, out var storedCode) ||
                storedCode != model.Code)
                return BadRequest("Invalid code");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == model.Phone);
            if (user == null || user.SmsCodeExpires < DateTime.UtcNow)
                return BadRequest("Code expired");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            _smsCodes.Remove(model.Phone);
            await _context.SaveChangesAsync();

            return Ok();
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
        public async Task<IActionResult> UploadFile(
    int chatId,
    int userId,
    [ValidateFile(allowedExtensions: new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".docx", ".mp4", ".mp3" },
                 maxSize: 50 * 1024 * 1024)] IFormFile file,
    [FromServices] IAmazonS3 s3Client,
    [FromServices] IConfiguration config,
    [FromServices] ILogger<UsersController> logger)
        {
            using var scope = logger.BeginScope("Upload:{ChatId}:{UserId}", chatId, userId);
            try
            {
                var (bucketName, publicUrl) = (config["SwiftConfig:BucketName"], config["SwiftConfig:PublicUrl"]);
                var objectKey = $"chat_{chatId}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                var fileUrl = $"{publicUrl.TrimEnd('/')}/{bucketName}/{objectKey}";

                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0; // Сброс позиции

                var putRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    InputStream = memoryStream,
                    ContentType = file.ContentType,
                    AutoCloseStream = false
                };

                // Однократная загрузка
                await s3Client.PutObjectAsync(putRequest);

                // Сохранение в БД
                var dbFile = new Models.File
                {
                    FileUrl = fileUrl,
                    FileType = file.ContentType,
                    Size = file.Length
                };

                await using var transaction = await _context.Database.BeginTransactionAsync();
                _context.Files.Add(dbFile);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                logger.LogInformation("File uploaded successfully. ID: "+dbFile.FileId);
                return Ok(new { dbFile.FileId, dbFile.FileUrl, dbFile.FileType });
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogError("S3 Error: "+ ex.ErrorCode + " - "+ex.Message+"");
                return StatusCode(500, "Storage error");
            }
            catch (Exception ex)
            {
                logger.LogError("Critical error: "+ex.Message);
                return StatusCode(500, "Internal server error");
            }
        }


        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(
        int fileId,
        [FromServices] IAmazonS3 s3Client,
        [FromServices] ILogger<UsersController> logger)
        {
            try
            {
                var file = await _context.Files
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.FileId == fileId);

                return file == null
                    ? NotFound()
                    : RedirectPreserveMethod(file.FileUrl);
            }
            catch (Exception ex)
            {
                logger.LogError("File error: {Message}", ex.Message);
                return StatusCode(500);
            }
        }

        public class ValidateFileAttribute : Attribute, IParameterModelConvention
        {
            private readonly string[] _allowedExtensions;
            private readonly long _maxSize;

            public ValidateFileAttribute(string[] allowedExtensions, long maxSize)
            {
                _allowedExtensions = allowedExtensions;
                _maxSize = maxSize;
            }

            public void Apply(ParameterModel parameter)
            {
                if (parameter.ParameterType == typeof(IFormFile))
                {
                    parameter.Action.Filters.Add(new ValidateFileFilter(_allowedExtensions, _maxSize));
                }
            }
        }

        public class ValidateFileFilter : IAsyncActionFilter
        {
            private readonly string[] _allowedExtensions;
            private readonly long _maxSize;

            public ValidateFileFilter(string[] allowedExtensions, long maxSize)
            {
                _allowedExtensions = allowedExtensions;
                _maxSize = maxSize;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                if (!context.ActionArguments.TryGetValue("file", out var fileObj) ||
                    !(fileObj is IFormFile file))
                {
                    context.Result = new BadRequestObjectResult("File required");
                    return;
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_allowedExtensions.Contains(extension))
                {
                    context.Result = new BadRequestObjectResult("Invalid file type");
                    return;
                }

                if (file.Length > _maxSize)
                {
                    context.Result = new BadRequestObjectResult("File too large");
                    return;
                }

                await next();
            }

            
        }

        public class ResetModel
        {
            public string Phone { get; set; }
            public string Code { get; set; }
            public string NewPassword { get; set; }
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

            // Новые поля для файлов
            [JsonProperty("fileId")]
            public int? FileId { get; set; }

            [JsonProperty("fileType")]
            public string? FileType { get; set; }

            [JsonProperty("fileUrl")]
            public string? FileUrl { get; set; }
            [JsonProperty("status")]
            public int Status { get; set; }
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

        public enum MessageStatusType
        {
            Sent = 0,       // Отправлено
            Delivered = 1,  // Доставлено
            Read = 2        // Прочитано
        }
    }
}