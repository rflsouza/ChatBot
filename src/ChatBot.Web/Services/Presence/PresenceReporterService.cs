using System.Net.Http.Json;

namespace ChatBot.Web.Services.Presence;

/// <summary>
/// Envia para a Api a lista de navegadores conectados nesta instância: logo após cada mudança
/// (agrupando mudanças em 500 ms) e, no mínimo, a cada 30 s (heartbeat). Se o Web cair, a Api expira a lista em 90 s.
/// </summary>
public sealed class PresenceReporterService(
    WebPresenceRegistry registry,
    IHttpClientFactory httpClientFactory,
    ILogger<PresenceReporterService> logger) : BackgroundService
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    // Identifica esta instância do Web (podem existir várias atrás de um balanceador).
    private readonly string _instanceId = Uri.EscapeDataString($"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..8]}");
    private bool _lastFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var http = httpClientFactory.CreateClient(ChatBotApiClient.HttpClientName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(RequestTimeout);

                using var response = await http.PutAsJsonAsync($"api/presence/web/{_instanceId}", registry.Snapshot(), timeout.Token);
                response.EnsureSuccessStatusCode();

                if (_lastFailed)
                {
                    logger.LogInformation("Presença: Api voltou a responder.");
                    _lastFailed = false;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Avisa uma vez; não enche o log enquanto a Api estiver fora.
                if (!_lastFailed)
                {
                    logger.LogWarning("Presença: não foi possível avisar a Api ({Message}). Tentando de novo.", ex.Message);
                    _lastFailed = true;
                }
            }

            try
            {
                if (await registry.WaitForChangeAsync(Heartbeat, stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken); // agrupa mudanças seguidas
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // Desligando: avisa a Api para não esperar os 90 s de expiração.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var http = httpClientFactory.CreateClient(ChatBotApiClient.HttpClientName);
            await http.DeleteAsync($"api/presence/web/{_instanceId}", timeout.Token);
        }
        catch
        {
            // a Api expira sozinha
        }
    }
}
