using MessengerServer.Hubs;
using MessengerServer.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Net;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowAnyOrigin();
    });
});


builder.Services.AddDbContext<MessengerDataBaseContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"),
    ServerVersion.Parse("8.0")));

List<User> users = MessengerDataBaseContext.GetContext().Users.ToList();

builder.Services.AddSignalR();

var app = builder.Build();

app.MapHub<ChatHub>("chatHub");
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{ 
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.UseCors("AllowAll");

app.MapControllers();

app.Run();


// ѕроблемы приложени€: при отправке сообщений сообщени€ не сохран€ютс€ в бд дл€ конкретного пользовател€, но 
// в одном из чатов все отправленные сообщени€ дл€ всех пользователей в этом чате сохран€ютс€
// также при отправке сообщений возникала ошибка not set on reference или топа того
// смайлики отправл€ютс€ нормально, поиск работает