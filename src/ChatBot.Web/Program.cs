using ChatBot.Web.Components;
using ChatBot.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiUrl = builder.Configuration["ChatBotApi:BaseUrl"] ?? "http://localhost:5100";

builder.Services.AddHttpClient(ChatBotApiClient.HttpClientName, http =>
{
    http.BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromMinutes(5); // respostas longas em streaming
});
builder.Services.AddScoped<ChatBotApiClient>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
