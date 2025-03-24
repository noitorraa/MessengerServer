using MessengerServer.Hubs;
using MessengerServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Net;
using System.Text.Json.Serialization;
using MessengerServer;
using Amazon.S3;
using Amazon.Runtime;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IAmazonS3>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Инициализация S3 клиента...");
        return new AmazonS3Client(
            new BasicAWSCredentials(
                config["SwiftConfig:AccessKey"],
                config["SwiftConfig:SecretKey"]),
            new AmazonS3Config
            {
                ServiceURL = config["SwiftConfig:ServiceURL"],
                ForcePathStyle = true,
                Timeout = TimeSpan.FromSeconds(30),
                MaxErrorRetry = 2
            });
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Ошибка инициализации S3 клиента");
        throw;
    }
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowAnyOrigin();
    });
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
