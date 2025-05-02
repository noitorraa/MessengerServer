using MessengerServer.Hubs;
using MessengerServer.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Net;
using System.Text.Json.Serialization;
using MessengerServer;
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
    options.AddPolicy("AllowAll", p => p
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin());
});



builder.Services.AddDbContext<DefaultDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"),
   ServerVersion.Parse("8.0")));

List<User> users = DefaultDbContext.GetContext().Users.ToList();

builder.Services.AddSignalR(hubOptions => {
    hubOptions.EnableDetailedErrors = true;
});

var app = builder.Build();

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

app.MapControllers();

app.Run();
