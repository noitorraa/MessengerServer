using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace MessengerServer.Services
{
    public class SmsaeroService : ISmsService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _from;

        public SmsaeroService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Smsaero:ApiKey"] ?? throw new InvalidOperationException("Smsaero API key is not configured.");
            _from = configuration["Smsaero:From"] ?? throw new InvalidOperationException("Smsaero 'From' value is not configured.");

            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_apiKey}:"));
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
        }

        public async Task<bool> SendSmsAsync(string phoneNumber, string message)
        {
            var payload = new JObject
            {
                ["to"] = phoneNumber,
                ["text"] = message,
                ["from"] = _from,
                ["translit"] = true,
                ["sign"] = _from,
                ["channel"] = "sms"
            };

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://gate.smsaero.ru/v2/sms/send", content);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseContent);
            return json["success"]?.Value<bool>() == true;
        }
}

        public interface ISmsService
    {
        Task<bool> SendSmsAsync(string phoneNumber, string message);
    }
}
