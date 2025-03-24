using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Models;

public partial class User
{
    public int UserId { get; set; }

    [StringLength(20, MinimumLength = 3, ErrorMessage = "Логин должен быть от 3 до 20 символов")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Только латиница, цифры и _")]
    public string Username { get; set; } = null!;

    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,30}$",
        ErrorMessage = "Пароль должен содержать 8-30 символов, заглавные/строчные буквы, цифры и спецсимволы")]
    public string PasswordHash { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }
    [RegularExpression(@"^[\+]?[(]?[0-9]{3}[)]?[-\s\.]?[0-9]{3}[-\s\.]?[0-9]{4,15}$",
        ErrorMessage = "Неверный формат телефона")]
    [StringLength(19, MinimumLength = 10, ErrorMessage = "Телефон: 10-19 цифр")]
    public string PhoneNumber { get; set; }
    public DateTime? SmsCodeExpires { get; set; } 

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    public virtual ICollection<MessageStatus> MessageStatuses { get; set; } = new List<MessageStatus>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
