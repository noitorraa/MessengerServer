using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Model;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MessengerServer.Services;

namespace MessengerServer.Services
{
    public class UserService : IUserService
    {
        private readonly DefaultDbContext _context;
        private readonly ISmsService _smsService;
        private readonly IVerificationService _verification;
        private readonly IResetCodeService _resetCodeService;
        private readonly IEncryptionService _encryptionService;
        private static readonly ConcurrentDictionary<string, int> _smsResetRetryCount = new();

        public UserService(
            DefaultDbContext context,
            ISmsService smsService,
            IVerificationService verificationService,
            IResetCodeService resetCodeService,
            IEncryptionService encryptionService)
        {
            _context = context;
            _smsService = smsService;
            _verification = verificationService;
            _resetCodeService = resetCodeService;
            _encryptionService = encryptionService;
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
            var phone = Regex.Replace(user.PhoneNumber ?? "", @"[^\d]", "");

            if (!_verification.IsPhoneVerified(phone))
                return new BadRequestObjectResult("Номер телефона не подтверждён");

            _verification.ClearVerified(phone);

            if (string.IsNullOrWhiteSpace(user.Username))
                return new BadRequestObjectResult("Username не может быть пустым");

            if (await _context.Users.AnyAsync(u => u.Username == user.Username))
                return new ConflictObjectResult("Username already exists.");

            if (phone.Length < 10 || phone.Length > 15)
                return new BadRequestObjectResult("Неверный формат телефона");

            // хешируем пароль
            var rawPassword = user.PasswordHash;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(rawPassword);


            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return new OkObjectResult(new { Message = "User registered successfully." });
        }

        public async Task<IActionResult> SendResetCode(string phone)
        {
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");

            var cipherPhone = _encryptionService.EncryptDeterministic(phone);
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == cipherPhone);

            if (user == null)
                return new NotFoundObjectResult("Пользователь не найден");

            int retry = _smsResetRetryCount.AddOrUpdate(phone, 1, (k, v) => v + 1);
            if (retry > 5)
                return new BadRequestObjectResult("Слишком много попыток. Попробуйте позже.");

            return await _resetCodeService.SendResetCodeAsync(phone);
        }

        public async Task<IActionResult> ResetPassword(ResetModel model)
        {
            var phone = Regex.Replace(model.Phone ?? "", @"[^\d]", "");
            var cipherPhone = _encryptionService.EncryptDeterministic(phone);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == cipherPhone);

            if (user == null)
                return new NotFoundObjectResult("Пользователь не найден");

            if (model.Code == null)
            {
                return new BadRequestObjectResult("Код для сброса пароля не может быть null.");
            }
            var verifyResult = await _resetCodeService.VerifyResetCodeAsync(phone, model.Code);
            if (verifyResult is not OkResult)
                return verifyResult;

            if (string.IsNullOrWhiteSpace(model.NewPassword) || model.NewPassword.Length < 8)
                return new BadRequestObjectResult("Пароль должен быть не менее 8 символов");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            await _context.SaveChangesAsync();
            return new OkObjectResult("Пароль успешно изменён");
        }

        public async Task<IActionResult> ChangeLogin(ChangeLoginRequest request)
        {
            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return new NotFoundObjectResult(new { Message = "Пользователь не найден" });
            }

            user.Username = request.NewLogin;
            await _context.SaveChangesAsync();
            return new OkObjectResult(new { Message = "Логин успешно изменён" });
        }

        public async Task<IActionResult> ChangeAvatar(ChangeAvatarRequest request)
        {
            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return new NotFoundObjectResult(new { Message = "Пользователь не найден" });
            }

            user.Avatar = request.NewAvatar;
            await _context.SaveChangesAsync();
            return new OkObjectResult(new { Message = "Аватар успешно изменён" });
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
    }

    public interface IUserService
    {
        Task<ActionResult<User>> GetUserByLoginAndPassword(string login, string password);
        Task<IActionResult> Registration([FromBody] User user);
        Task<IActionResult> SendResetCode(string phone);
        Task<IActionResult> ResetPassword(ResetModel model);
        Task<ActionResult<List<User>>> SearchUsersByLogin(string login);
        Task<ActionResult<Chat>> GetExistingChat(int user1Id, int user2Id);
        Task<IActionResult> ChangeLogin(ChangeLoginRequest request);
        Task<IActionResult> ChangeAvatar(ChangeAvatarRequest request);
    }
}
