using ChatBot.Api.Agents;
using ChatBot.Api.Channels.Rest;
using ChatBot.Api.Channels.Telegram;
using ChatBot.Api.Channels.Web;
using ChatBot.Api.Conversations;
using ChatBot.Api.Data;
using ChatBot.Api.Mcp;
using ChatBot.Api.Presence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Logs em uma linha, com horário (ex.: "14:03:21 warn: ChatBot.Api.Agents.ResponseDiagnosticsChatClient[0] LLM ...").
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Persistência (EF Core + SQLite): conversas, mensagens e sessões dos agentes.
builder.Services.AddDbContextFactory<ChatBotDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ChatBot") ?? "Data Source=chatbot.db"));

// Núcleo: agentes (Microsoft Agent Framework) + conversas independentes de canal.
builder.AddMcpServers();   // catálogo de servidores MCP (seção "Mcp")
builder.AddChatAgents();
builder.Services.AddSingleton<IConversationService, ConversationService>();

// Quem está com o chat web aberto agora (informado pelo ChatBot.Web).
builder.Services.AddSingleton<WebPresenceTracker>();

// Canais
builder.Services.AddWebChannel();
builder.AddTelegramChannel();   // só liga se Channels:Telegram:Enabled = true

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
app.MapPresenceEndpoints(app.Environment.IsDevelopment());
app.MapMcpEndpoints(app.Environment.IsDevelopment());
app.MapTelegramChannel();       // webhook (só no modo Webhook)

await app.RunAsync();
