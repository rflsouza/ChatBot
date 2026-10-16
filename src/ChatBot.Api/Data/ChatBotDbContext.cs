using Microsoft.EntityFrameworkCore;

namespace ChatBot.Api.Data;

public sealed class ChatBotDbContext(DbContextOptions<ChatBotDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("Conversations");
            e.HasKey(c => c.Id);
            e.Property(c => c.AgentId).HasMaxLength(100).IsRequired();
            e.Property(c => c.Channel).HasMaxLength(50).IsRequired();
            e.Property(c => c.SessionKey).HasMaxLength(300);
            e.Property(c => c.UserId).HasMaxLength(200);
            e.Property(c => c.Title).HasMaxLength(200);

            // Uma sessão ativa por (agente, chave). NULLs não colidem (conversas encerradas).
            e.HasIndex(c => new { c.AgentId, c.SessionKey }).IsUnique();
            e.HasIndex(c => new { c.Channel, c.UpdatedAt });
            e.HasIndex(c => c.UserId);

            e.HasMany(c => c.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationMessage>(e =>
        {
            e.ToTable("Messages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Author).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.AuthorName).HasMaxLength(100);
            e.Property(m => m.MessageId).HasMaxLength(100);
            e.Property(m => m.Payload).IsRequired();
            e.HasIndex(m => new { m.ConversationId, m.Id });
        });
    }
}
