﻿using System;
using System.Collections.Generic;

namespace MessengerServer.Model;

public partial class User
{
    public int UserId { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Email { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<ChatMember> ChatMembers { get; set; } = new List<ChatMember>();

    public virtual ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();

    public virtual ICollection<MessageStatus> MessageStatuses { get; set; } = new List<MessageStatus>();

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
