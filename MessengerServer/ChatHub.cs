using MessengerServer.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly MessengerDataBaseContext _context;
        public ChatHub(MessengerDataBaseContext context)
        {
            _context = context;
        }

        public async Task SendMessage(int userId, string message)
        {
            try
            {
                Chat chat = _context.Chats.Where(ch => ch.ChatMembers.FirstOrDefault().UserId == userId).FirstOrDefault();

                var connection = _context.Database.GetDbConnection();
                connection.Open();
                connection.Close(); //test

                var newMessage = new Message
                {
                    Content = message,
                    CreatedAt = DateTime.Now,
                    SenderId = userId,
                    ChatId = chat.ChatId// Укажите правильный ChatId
                };

                _context.Messages.Add(newMessage);
                await _context.SaveChangesAsync();

                // Отправляем сообщение всем клиентам
                await Clients.All.SendAsync("ReceiveMessage", userId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка в хабе при отправке сообщения " + ex.ToString());
                throw new HubException("Ошибка при отправке сообщения", ex);
            }
        }
    }
}