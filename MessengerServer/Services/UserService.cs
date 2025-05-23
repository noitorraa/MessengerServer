using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using MessengerServer.Model;
using BCrypt.Net;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MessengerServer.Services;

namespace MessengerServer.Services
{
    public class UserService : IUserService // Почекать хэширование пароля
    {
        private readonly DefaultDbContext _context;
        private static readonly ConcurrentDictionary<string, (string Code, DateTime Expires)> _smsResetCodes = new();
        private static readonly ConcurrentDictionary<string, int> _smsResetRetryCount = new();
        private static readonly ConcurrentDictionary<string, bool> _verifiedPhones = new();

        public UserService(DefaultDbContext context)
        {
            _context = context;
        }

        public async Task<ActionResult<User>> GetUserByLoginAndPassword(string login, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == login);
            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                return new NotFoundObjectResult(new { Message = "Пользователь не найден" });
            }
            return new OkObjectResult(user);
        }

        public async Task<IActionResult> Registration([FromBody] User user)
        {
            user.PhoneNumber = Regex.Replace(user.PhoneNumber ?? "", @"[^\d]", "");
            if (!_verifiedPhones.TryGetValue(user.PhoneNumber, out var verified) || !verified)
            {
                return new BadRequestObjectResult("Номер телефона не подтверждён");
            }
            _verifiedPhones.TryRemove(user.PhoneNumber, out _);

            if (await _context.Users.AnyAsync(u => u.Username == user.Username))
            {
                return new ConflictObjectResult("Username already exists.");
            }

            if (user.PhoneNumber.Length < 10 || user.PhoneNumber.Length > 15)
            {
                return new BadRequestObjectResult("Неверный формат телефона");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return new OkObjectResult(new { Message = "User registered successfully." });
        }

        public IActionResult SendResetCode(string phone)
        {
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");
            var user = _context.Users.FirstOrDefault(u => u.PhoneNumber == phone);
            if (user == null)
            {
                return new NotFoundObjectResult("Пользователь не найден");
            }

            int retry = _smsResetRetryCount.AddOrUpdate(phone, 1, (k, v) => v + 1);
            if (retry > 5)
            {
                return new BadRequestObjectResult("Слишком много попыток. Попробуйте позже.");
            }

            var code = new Random().Next(100000, 999999).ToString();
            var expiration = DateTime.UtcNow.AddMinutes(5);
            _smsResetCodes[phone] = (code, expiration);
            Console.WriteLine($"Reset Code: {code}, phone: {phone}");

            return new OkObjectResult("Код отправлен");
        }

        public async Task<IActionResult> ResetPassword(ResetModel model)
        {
            string phone = Regex.Replace(model.Phone ?? "", @"[^\d]", "");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone);
            if (user == null)
            {
                return new NotFoundObjectResult("Пользователь не найден");
            }

            if (!_smsResetCodes.TryGetValue(phone, out var codeInfo))
            {
                return new BadRequestObjectResult("Код не найден или просрочен");
            }

            if (codeInfo.Expires < DateTime.UtcNow)
            {
                _smsResetCodes.TryRemove(phone, out _);
                return new BadRequestObjectResult("Срок действия кода истек");
            }

            if (codeInfo.Code != model.Code)
            {
                return new BadRequestObjectResult("Неверный код подтверждения");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            _smsResetCodes.TryRemove(phone, out _);
            await _context.SaveChangesAsync();
            return new OkObjectResult("Пароль успешно изменен");
        }

        public async Task<ActionResult<List<User>>> SearchUsersByLogin(string login)
        {
            var users = await _context.Users
                .Where(u => u.Username.Contains(login))
                .ToListAsync();

            if (users == null || !users.Any())
            {
                return new NotFoundObjectResult(new { Message = "Пользователи не найдены" });
            }
            return new OkObjectResult(users);
        }

        public async Task<ActionResult<Chat>> GetExistingChat(int user1Id, int user2Id)
        {
            var chat = await _context.Chats
                .Include(c => c.ChatMembers)
                .FirstOrDefaultAsync(c =>
                    c.ChatMembers.Any(cm => cm.UserId == user1Id) &&
                    c.ChatMembers.Any(cm => cm.UserId == user2Id) &&
                    c.ChatMembers.Count == 2
                );
            return chat != null ? new OkObjectResult(chat) : new NotFoundResult();
        }

        Task<IActionResult> IUserService.SendResetCode(string phone) // потом доделать
        {
            throw new NotImplementedException();
        }
    }
    public interface IUserService
    {
        Task<ActionResult<User>> GetUserByLoginAndPassword(string login, string password);
        Task<IActionResult> Registration([FromBody] User user);
        Task<IActionResult> SendResetCode(string phone);
        Task<IActionResult> ResetPassword(ResetModel model);
        Task<ActionResult<List<User>>> SearchUsersByLogin(string login);
        Task<ActionResult<Chat>> GetExistingChat(int user1Id, int user2Id);
    }

}