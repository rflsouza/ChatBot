using ChatBot.Api.Agents;
using ChatBot.Api.Channels.Rest;
using ChatBot.Api.Channels.Web;
using ChatBot.Api.Conversations;
using ChatBot.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Persistência (EF Core + SQLite): conversas, mensagens e sessões dos agentes.
builder.Services.AddDbContextFactory<ChatBotDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ChatBot") ?? "Data Source=chatbot.db"));

// Núcleo: agentes (Microsoft Agent Framework) + conversas independentes de canal.
builder.AddChatAgents();
builder.Services.AddSingleton<IConversationService, ConversationService>();

// Canais
builder.Services.AddWebChannel();
// builder.Services.AddTelegramChannel(builder.Configuration);   // próxima etapa

var app = builder.Build();

// Cria o banco/tabelas se não existirem. Quando o modelo começar a evoluir, troque por migrations
// (dotnet ef migrations add Inicial && db.Database.MigrateAsync()).
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ChatBotDbContext>>().CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

app.MapGet("/health", () => Results.Ok("ok")).ExcludeFromDescription();

app.MapWebChannel();   // AG-UI  -> Blazor
app.MapRestChannel();  // REST   -> integrações/testes
app.MapConversationEndpoints(app.Environment.IsDevelopment());
// app.MapTelegramChannel();                                     // próxima etapa

await app.RunAsync();
