using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace MessengerServer.Services
{
    public class VerificationService : IVerificationService
    {
        private static readonly ConcurrentDictionary<string, (string Code, DateTime Expires)> _pendingVerifications = new();
        private static readonly ConcurrentDictionary<string, int> _verificationRetryCount = new();
        private static readonly ConcurrentDictionary<string, bool> _verifiedPhones = new();
        private readonly ISmsService _smsService;

        public VerificationService(ISmsService smsService)
        {
            _smsService = smsService;
        }

        public async Task<IActionResult> SendVerificationCode(string phone)
        {
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");
            if (string.IsNullOrEmpty(phone))
            {
                return new BadRequestObjectResult("Неверный формат телефона");
            }

            var retry = _verificationRetryCount.AddOrUpdate(phone, 1, (k, v) => v + 1);
            if (retry > 5)
            {
                return new BadRequestObjectResult("Слишком много попыток. Попробуйте позже.");
            }

            var code = new Random().Next(100000, 999999).ToString();
            var expiration = DateTime.UtcNow.AddMinutes(5);
            _pendingVerifications[phone] = (code, expiration);

            // Отправка SMS через Email-to-SMS
            bool isSent = await _smsService.SendSmsAsync(phone, $"Ваш код подтверждения: {code}");
            if (!isSent)
                return new ObjectResult("Не удалось отправить SMS") { StatusCode = 500 };

            return new OkObjectResult("Код отправлен");
        }

        public IActionResult VerifyCode(string phone, string code)
        {
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");
            if (!_pendingVerifications.TryGetValue(phone, out var codeInfo))
            {
                return new BadRequestObjectResult("Код не найден или просрочен");
            }

            if (codeInfo.Expires < DateTime.UtcNow)
            {
                _pendingVerifications.TryRemove(phone, out _);
                return new BadRequestObjectResult("Срок действия кода истёк");
            }

            if (codeInfo.Code != code)
            {
                return new BadRequestObjectResult("Неверный код подтверждения");
            }

            _pendingVerifications.TryRemove(phone, out _);
            _verifiedPhones[phone] = true;
            return new OkObjectResult("Код подтверждён");
        }

        public bool IsPhoneVerified(string phone)
        {
            return _verifiedPhones.TryGetValue(phone, out var verified) && verified;
        }
    }

    public interface IVerificationService
    {
        Task<IActionResult> SendVerificationCode(string phone);
        IActionResult VerifyCode(string phone, string code);
        bool IsPhoneVerified(string phone);
    }
}
