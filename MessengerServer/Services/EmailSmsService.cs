using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace MessengerServer.Services
{
    public class EmailSmsService : ISmsService
    {
        private readonly SmtpClient _smtpClient;
        private readonly string _fromEmail;

        public EmailSmsService(IConfiguration configuration)
        {
            var smtpSettings = configuration.GetSection("SmtpSettings");
            _fromEmail = smtpSettings["FromEmail"];

            _smtpClient = new SmtpClient(smtpSettings["Host"], int.Parse(smtpSettings["Port"]))
            {
                Credentials = new NetworkCredential(
                    smtpSettings["Username"],
                    smtpSettings["Password"]
                ),
                EnableSsl = true
            };
        }

        public async Task<bool> SendSmsAsync(string phoneNumber, string message)
        {
            try
            {
                // Определите домен оператора по префиксу номера
                string operatorDomain = GetOperatorDomain(phoneNumber);
                if (string.IsNullOrEmpty(operatorDomain))
                    return false;

                var recipient = $"{phoneNumber}@{operatorDomain}";
                var mailMessage = new MailMessage(_fromEmail, recipient, "SMS", message);

                await _smtpClient.SendMailAsync(mailMessage);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки SMS: {ex.Message}");
                return false;
            }
        }

        private string GetOperatorDomain(string phoneNumber)
        {
            // Примеры доменов для российских операторов
            var normalizedPhone = Regex.Replace(phoneNumber, @"[^\d]", "");

            if (string.IsNullOrEmpty(normalizedPhone))
                return null;

            // Проверка префиксов номеров
            if (normalizedPhone.StartsWith("7912") || normalizedPhone.StartsWith("7922"))
                return "sms.mts.ru"; // МТС
            if (normalizedPhone.StartsWith("7902") || normalizedPhone.StartsWith("7903"))
                return "sms.beemail.ru"; // Beeline
            if (normalizedPhone.StartsWith("7925") || normalizedPhone.StartsWith("7933"))
                return "pager.megafon.ru"; // МегаФон
            if (normalizedPhone.StartsWith("7952") || normalizedPhone.StartsWith("7962"))
                return "smsmail.utel.ru"; // Utel
            if (normalizedPhone.StartsWith("7910") || normalizedPhone.StartsWith("7911"))
                return "sms.tele2.ru"; // Tele2

            return null; // Неизвестный оператор
        }
    }

    public interface ISmsService
    {
        Task<bool> SendSmsAsync(string phoneNumber, string message);
    }
}