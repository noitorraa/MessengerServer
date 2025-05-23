using MessengerServer.Hubs;
using MessengerServer.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Net;
using System.Text.Json.Serialization;
using MessengerServer;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// 1) Регистрируем все сервисы
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(opts =>
    opts.AddPolicy("AllowAll", p => p
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin()));

// AES‑шифрование
builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();

// DbContext с MySQL
builder.Services.AddDbContext<DefaultDbContext>(opts =>
    opts.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.Parse("8.0")
    )
);

builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.EnableDetailedErrors = true;
});

var app = builder.Build();

List<User> users;
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DefaultDbContext>();
    users = db.Users.ToList();
}

// 3) Настраиваем middleware
app.UseCors("AllowAll");
app.MapHub<ChatHub>("/chatHub");
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
app.Run();