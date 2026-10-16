using ChatBot.Api.Agents;
using ChatBot.Api.Channels.Rest;
using ChatBot.Api.Channels.Web;
using ChatBot.Api.Conversations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Núcleo: agentes (Microsoft Agent Framework) + conversas independentes de canal.
builder.AddChatAgents();
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddSingleton<IConversationService, ConversationService>();

// Canais
builder.Services.AddWebChannel();
// builder.Services.AddTelegramChannel(builder.Configuration);   // próxima etapa

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

app.MapGet("/health", () => Results.Ok("ok")).ExcludeFromDescription();

app.MapWebChannel();   // AG-UI  -> Blazor
app.MapRestChannel();  // REST   -> integrações/testes
// app.MapTelegramChannel();                                     // próxima etapa

app.Run();
