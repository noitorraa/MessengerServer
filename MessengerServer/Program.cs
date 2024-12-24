using MessengerServer.Hubs;
using MessengerServer.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<MessengerDataBaseContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

List<User> users = MessengerDataBaseContext.GetContext().Users.ToList();

builder.Services.AddSignalR();

var app = builder.Build();

app.Urls.Add("https://192.168.0.11:7243/"); // 192.168.0.11:7243 and my phone ip: 192.168.88.29

app.MapHub<ChatHub>("chatHub"); // 
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();


// ѕроблемы приложени€: при отправке сообщений сообщени€ не сохран€ютс€ в бд дл€ конкретного пользовател€, но 
// в одном из чатов все отправленные сообщени€ дл€ всех пользователей в этом чате сохран€ютс€
// также при отправке сообщений возникала ошибка not set on reference или топа того
// смайлики отправл€ютс€ нормально, поиск работает