using MessengerServer.Hubs;
using MessengerServer.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<MessengerDataBaseContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection"),
    ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))));

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

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();


// �������� ����������: ��� �������� ��������� ��������� �� ����������� � �� ��� ����������� ������������, �� 
// � ����� �� ����� ��� ������������ ��������� ��� ���� ������������� � ���� ���� �����������
// ����� ��� �������� ��������� ��������� ������ not set on reference ��� ���� ����
// �������� ������������ ���������, ����� ��������