using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessengerServer.Model;
using Microsoft.AspNetCore.Mvc;

namespace MessengerServer.Services
{
    public class ResetCodeService : IResetCodeService
    {
        private readonly ISmsService _sms;
        private static readonly ConcurrentDictionary<string, (string Code, DateTime Exp)> _codes = new();

        public ResetCodeService(ISmsService smsService) => _sms = smsService;

        public async Task<IActionResult> SendResetCodeAsync(string phone)
        {
            try
            {
                phone = Regex.Replace(phone ?? "", @"[^\d]", "");
                var code = new Random().Next(100000, 999999).ToString();
                _codes[phone] = (code, DateTime.UtcNow.AddMinutes(5));

                //if (!await _sms.SendSmsAsync(phone, $"Ваш код сброса пароля: {code}"))
                //    return new ObjectResult("SMS send failed") { StatusCode = 500 };
                Console.WriteLine($"Код подтверждения {code}, номер телефона {phone}");
                return new OkResult();
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.Message) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> VerifyResetCodeAsync(string phone, string code)
        {
            try
            {
                phone = Regex.Replace(phone ?? "", @"[^\d]", "");
                if (!_codes.TryGetValue(phone, out var info) || info.Exp < DateTime.UtcNow)
                    return new ObjectResult("Код не найден или просрочен") { StatusCode = 400 };

                if (info.Code != code)
                    return new ObjectResult("Неверный код") { StatusCode = 400 };

                _codes.TryRemove(phone, out _);
                return new OkResult();
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.Message) { StatusCode = 500 };
            }
        }
    }

    public interface IResetCodeService
    {
        Task<IActionResult> SendResetCodeAsync(string phone);
        Task<IActionResult> VerifyResetCodeAsync(string phone, string code);
    }
}
