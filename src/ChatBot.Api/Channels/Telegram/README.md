# Canal Telegram (próxima etapa)

O núcleo já está pronto: o canal Telegram será só um adaptador fino sobre `IConversationService`.

Plano:

1. Pacote `Telegram.Bot` e configuração `Channels:Telegram:BotToken` / `SecretToken`.
2. `TelegramChannelEndpoints.MapTelegramChannel()` com `POST /channels/telegram/webhook`
   (validar o header `X-Telegram-Bot-Api-Secret-Token`).
3. Para cada `Update` de texto:
   ```csharp
   var key = new ConversationKey("telegram", update.Message.Chat.Id.ToString(), AgentId: "");
   var reply = await conversations.SendAsync(key, update.Message.Text, ct);
   await bot.SendMessage(update.Message.Chat.Id, reply, cancellationToken: ct);
   ```
4. Comandos: `/novo` → `ResetAsync`, `/modelo <id>` → guardar o agente escolhido por chat.
5. Trocar `InMemoryConversationStore` por um store persistente (Redis/PostgreSQL/Cosmos DB).
6. Registrar no `Program.cs`: `app.MapTelegramChannel();`
