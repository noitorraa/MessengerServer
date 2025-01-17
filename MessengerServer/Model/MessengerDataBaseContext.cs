using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace MessengerServer.Model;

public partial class MessengerDataBaseContext : DbContext
{
    public MessengerDataBaseContext()
    {
    }

    public static MessengerDataBaseContext model;
    public static Model.MessengerDataBaseContext GetContext()
    {
        if (model == null)
        {
            model = new MessengerDataBaseContext();
        }
        return model;
    }

    public MessengerDataBaseContext(DbContextOptions<MessengerDataBaseContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Chat> Chats { get; set; }

    public virtual DbSet<ChatMember> ChatMembers { get; set; }

    public virtual DbSet<MediaFile> MediaFiles { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<MessageStatus> MessageStatuses { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseMySql(
                "Server=213.171.4.203;Port=3306;Database=default_db;User Id=gen_user;Password=qZf+X=zK}#Wr7h;",
                ServerVersion.AutoDetect("Server=213.171.4.203;Port=3306;Database=default_db;User Id=gen_user;Password=qZf+X=zK}#Wr7h;")
            );

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.ChatId).HasName("PK__chats__FD040B179084064F");

            entity.ToTable("chats");

            entity.Property(e => e.ChatId).HasColumnName("chat_id");
            entity.Property(e => e.ChatName)
                .HasMaxLength(100)
                .HasColumnName("chat_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
        });

        modelBuilder.Entity<ChatMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__chat_mem__169FE8671DDEAC77");

            entity.ToTable("chat_members");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChatId).HasColumnName("chat_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Chat).WithMany(p => p.ChatMembers)
                .HasForeignKey(d => d.ChatId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__chat_memb__chat___3F466844");

            entity.HasOne(d => d.User).WithMany(p => p.ChatMembers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_chat_members_users");
        });

        modelBuilder.Entity<MediaFile>(entity =>
        {
            entity.HasKey(e => e.FileId).HasName("PK__media_fi__07D884C617781517");

            entity.ToTable("media_files");

            entity.Property(e => e.FileId).HasColumnName("file_id");
            entity.Property(e => e.ChatId).HasColumnName("chat_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.FileType)
                .HasMaxLength(50)
                .HasColumnName("file_type");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(255)
                .HasColumnName("file_url");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");

            entity.HasOne(d => d.Chat).WithMany(p => p.MediaFiles)
                .HasForeignKey(d => d.ChatId)
                .HasConstraintName("FK__media_fil__chat___48CFD27E");

            entity.HasOne(d => d.Sender).WithMany(p => p.MediaFiles)
                .HasForeignKey(d => d.SenderId)
                .HasConstraintName("FK__media_fil__sende__47DBAE45");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("PK__messages__0BBF6EE651B86877");

            entity.ToTable("messages");

            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.ChatId).HasColumnName("chat_id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");

            entity.HasOne(d => d.Chat).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ChatId)
                .HasConstraintName("FK__messages__chat_i__4316F928");

            entity.HasOne(d => d.Sender).WithMany(p => p.Messages)
                .HasForeignKey(d => d.SenderId)
                .HasConstraintName("FK__messages__sender__440B1D61");
        });

        modelBuilder.Entity<MessageStatus>(entity =>
        {
            entity.HasKey(e => e.StatusId).HasName("PK__message___3683B531C7FBA2CA");

            entity.ToTable("message_statuses");

            entity.Property(e => e.StatusId).HasColumnName("status_id");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Message).WithMany(p => p.MessageStatuses)
                .HasForeignKey(d => d.MessageId)
                .HasConstraintName("FK__message_s__messa__4CA06362");

            entity.HasOne(d => d.User).WithMany(p => p.MessageStatuses)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__message_s__user___4D94879B");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__users__B9BE370FCB826D3A");

            entity.ToTable("users");

            entity.HasIndex(e => e.Username, "UQ__users__F3DBC57211916948").IsUnique();

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
