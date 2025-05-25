using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessengerServer.Model;
using Microsoft.AspNetCore.Mvc;

namespace MessengerServer.Services
{
    public class VerificationService : IVerificationService
    {
        private readonly ISmsService _sms;
        private static readonly ConcurrentDictionary<string,(string Code, DateTime Exp)> _codes = new();
        private static readonly ConcurrentDictionary<string,bool> _verified = new();

        public VerificationService(ISmsService smsService) => _sms = smsService;

        public async Task<IActionResult> SendVerificationCodeAsync(string phone)
        {
            try
            {
                phone = Regex.Replace(phone ?? "", @"[^\d]", "");
                var code = new Random().Next(100000,999999).ToString();
                _codes[phone] = (code, DateTime.UtcNow.AddMinutes(5));
                //if (!await _sms.SendSmsAsync(phone, $"Ваш код: {code}"))
                //    return new ObjectResult("SMS send failed") { StatusCode = 500 };
                Console.WriteLine($"Phone = {phone}, code = {code}");

                return new OkResult();
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.Message) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> VerifyCodeAsync(string phone, string code)
        {
            try
            {
                phone = Regex.Replace(phone ?? "", @"[^\d]", "");
                if (!_codes.TryGetValue(phone, out var info) || info.Exp < DateTime.UtcNow)
                    return new ObjectResult("Код не найден или просрочен") { StatusCode = 400 };

                if (info.Code != code)
                    return new ObjectResult("Неверный код") { StatusCode = 400 };

                _codes.TryRemove(phone, out _);
                _verified[phone] = true;

                return new OkResult();
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.Message) { StatusCode = 500 };
            }
        }

        public void ClearVerified(string phone)
        {
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");
            _verified.TryRemove(phone, out _);
        }

        public bool IsPhoneVerified(string phone)
        {
            phone = Regex.Replace(phone ?? "", @"[^\d]", "");
            return _verified.TryGetValue(phone, out var ok) && ok;
        }
    }
    public interface IVerificationService
    {
        Task<IActionResult> SendVerificationCodeAsync(string phone);
        Task<IActionResult> VerifyCodeAsync(string phone, string code);
        bool IsPhoneVerified(string phone);
        void ClearVerified(string phone);
    }
}
