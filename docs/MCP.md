# Guia de uso: servidores MCP

Os agentes do ChatBot podem usar **ferramentas de servidores MCP** (Model Context Protocol): APIs internas, bancos,
sistemas de arquivos etc. O modelo decide quando chamar a ferramenta; a Api executa a chamada no servidor MCP e
devolve o resultado ao modelo. Vale para **todos os canais** (web, Telegram, REST).

Implementação: [SDK C# oficial do MCP](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.Core` 2.2),
integrado ao Microsoft Agent Framework por um `AIContextProvider`.

---

## 1. Passo a passo (servidor HTTP com headers)

### 1.1 Cadastrar o servidor

Em **`src/ChatBot.Api/appsettings.json`** (o da Api, não o do Web), seção `Mcp:Servers`.
O nome da chave (`XgenMcp-pr-3351`) é o nome do servidor:

```json
"Mcp": {
  "Servers": {
    "XgenMcp-pr-3351": {
      "type": "streamable-http",
      "url": "https://desenv.teste.com.br/v2/api/mcp",
      "headers": {
        "X-API-KEY": "",
        "X-TENANT-ID": "11"
      }
    }
  }
}
```

> 💡 O formato é o mesmo do VS Code / Claude / Kiro. Se você já tem um `mcp.json`, copie cada servidor para dentro
> de `"Mcp": { "Servers": { ... } }` — **não** use a chave raiz `servers`/`mcpServers` desses arquivos.

### 1.2 Guardar os segredos fora do arquivo

Deixe o valor sensível vazio no `appsettings.json` e configure com **user-secrets** (desenvolvimento):

```bash
cd src/ChatBot.Api
dotnet user-secrets set "Mcp:Servers:XgenMcp-pr-3351:Headers:X-API-KEY" "xxxxxxx"
dotnet user-secrets list   # confere
```

Em produção, use variável de ambiente (`:` vira `__`):

```
Mcp__Servers__XgenMcp-pr-3351__Headers__X-API-KEY=xxxxxxx
```

O user-secrets/variável **sobrescreve** o valor do `appsettings.json`. Se você esquecer de configurar, o header vai
**vazio** — a Api avisa no log:

```
warn: ChatBot.Api.Mcp.McpToolProvider[0] MCP XgenMcp-pr-3351: o header 'X-API-KEY' está VAZIO e será enviado assim. ...
```

### 1.3 Dizer quais agentes usam o servidor

Em cada agente de `AI:Agents`, o campo `McpServers`:

```jsonc
"Agents": [
  { "Id": "assistant", "Model": "...", "McpServers": [ "XgenMcp-pr-3351" ] },  // só esse servidor
  { "Id": "expert",    "Model": "..." },                                       // omitido = TODOS os servidores
  { "Id": "tradutor",  "Model": "...", "McpServers": [] }                      // nenhum
]
```

`["*"]` também significa todos. `"EnableTools": false` desliga **todas** as ferramentas do agente (internas e MCP).

> ⚠️ O **modelo** precisa suportar *tool calling*. No OpenRouter, filtre por "Tools" na lista de modelos.

### 1.4 Rodar e conferir

```bash
dotnet run --project src/ChatBot.Api
```

Log esperado:

```
info: ChatBot.Api.Mcp.McpToolProvider[0] MCP XgenMcp-pr-3351: conectado (NomeDoServidor), 5 ferramenta(s): buscar_cliente, ...
```

Em Development, consulte o status (headers aparecem **mascarados**):

```bash
curl http://localhost:5100/api/mcp/servers
```

```json
[{
  "name": "XgenMcp-pr-3351", "type": "streamable-http", "status": "connected",
  "url": "https://desenv.teste.com.br/v2/api/mcp",
  "headers": { "X-API-KEY": "xx***(7 chars)", "X-TENANT-ID": "11" },
  "tools": [ "buscar_cliente", "..." ], "error": null
}]
```

`status`: `connecting` (ainda tentando), `connected`, `error` (veja `error`) ou `disabled`.

Depois é só perguntar no chat algo que precise da ferramenta. No chat web a chamada aparece como 🔧 `nome_da_ferramenta`.

---

## 2. Servidor local (stdio)

Para servidores que rodam como processo (`npx`, `uvx`, `dotnet`, `docker`...):

```json
"filesystem": {
  "type": "stdio",
  "command": "npx",
  "args": [ "-y", "@modelcontextprotocol/server-filesystem", "C:/dados" ],
  "env": { "LOG_LEVEL": "info" }
}
```

O comando precisa existir **na máquina onde a Api roda** (no servidor/container também).

---

## 3. Todas as opções

### `Mcp`

| Opção | Padrão | Descrição |
|---|---|---|
| `ConnectTimeoutSeconds` | `30` | Tempo máximo para conectar e listar ferramentas de cada servidor |
| `RefreshIntervalSeconds` | `300` | A cada quanto tempo tenta de novo os que falharam e recarrega a lista de ferramentas (mínimo 30) |
| `Servers` | `{}` | Nome → configuração do servidor |

### `Mcp:Servers:<nome>`

| Opção | Padrão | Para | Descrição |
|---|---|---|---|
| `type` | `http` | todos | `streamable-http`, `sse`, `http` (tenta Streamable HTTP e, se falhar, SSE) ou `stdio` |
| `url` | — | http/sse | Endpoint completo do servidor MCP |
| `headers` | `{}` | http/sse | Enviados em **todas** as requisições (POST, GET e DELETE) |
| `command` | — | stdio | Executável |
| `args` | `[]` | stdio | Argumentos |
| `env` | `{}` | stdio | Variáveis de ambiente do processo |
| `enabled` | `true` | todos | `false` desliga sem apagar |
| `allowedTools` | `[]` | todos | Só essas ferramentas ficam disponíveis (vazio = todas) |
| `toolPrefix` | — | todos | Prefixo nos nomes (`xgen` → `xgen_buscar_cliente`) |

**Qual `type` usar?**

| O servidor... | `type` |
|---|---|
| é MCP moderno (um único endpoint, POST) — o caso mais comum | `streamable-http` |
| é MCP antigo com SSE (`GET /sse` + POST de mensagens) | `sse` |
| você não sabe | `http` (detecta, mas gera logs confusos quando dá erro — ver seção 5) |

---

## 4. Como funciona

1. Na inicialização, o `McpToolProvider` (serviço em segundo plano) conecta em cada servidor habilitado.
   **A Api não espera** por isso: se um MCP estiver fora, ela sobe normalmente.
2. Conectado, ele lista as ferramentas (`tools/list`) e guarda.
3. A cada `RefreshIntervalSeconds`, tenta de novo os que falharam e recarrega as ferramentas dos que estão ok.
4. A cada mensagem, o `McpContextProvider` entrega ao agente as ferramentas dos servidores permitidos
   (`McpServers`) e as instruções que o servidor MCP publica. Um servidor que conectou depois passa a valer
   sem reiniciar.
5. Quando o modelo pede uma ferramenta, o Agent Framework chama o servidor MCP (`tools/call`) e devolve o resultado.
   A chamada e o resultado ficam na tabela `Messages`.

Nomes de ferramentas são ajustados para o padrão aceito pelos LLMs (`a-z A-Z 0-9 _ -`, até 64 caracteres). Se dois
servidores tiverem ferramentas com o mesmo nome, a segunda ganha o nome do servidor como prefixo.

---

## 5. Problemas comuns

### Ver exatamente o que está sendo enviado

No `appsettings.json` da Api:

```json
"Logging": { "LogLevel": { "ChatBot.Api.Mcp": "Debug" } }
```

Cada requisição ao MCP aparece com método, URL, status e headers (segredos mascarados):

```
dbug: ChatBot.Api.Mcp.McpHttpDiagnosticsHandler[0] MCP HTTP POST https://.../v2/api/mcp -> 200. Headers enviados: X-API-KEY=xx***(7 chars); X-TENANT-ID=11
```

Requisições com erro aparecem como `warn` mesmo sem o Debug ligado.

### `401 Unauthorized` + `405 Method Not Allowed` juntos

Com `type: "http"` o SDK faz:

1. `POST` (Streamable HTTP) → o servidor respondeu **401** ← **esta é a causa**
2. como falhou, tenta SSE: `GET` → **405** (o servidor não tem SSE) ← só consequência

Os headers **são enviados** nas duas requisições. O 401 significa que o servidor recusou as credenciais:

- o header está vazio (veja o aviso no log e `GET /api/mcp/servers`);
- o valor está errado, ou o user-secrets foi gravado com outro caminho (confira com `dotnet user-secrets list`;
  o caminho tem que ser exatamente `Mcp:Servers:<nome>:Headers:<header>`);
- o nome do header não é o que o servidor espera (ex.: `Authorization: Bearer ...` em vez de `X-API-KEY`).

Use `type: "streamable-http"` para não ter a tentativa de SSE nem o 405 no log.

### `405` só no `GET`, mas conectou

Normal no Streamable HTTP: o `GET` abre um canal opcional de notificações, e muitos servidores não oferecem.

### Outros

| Sintoma | O que fazer |
|---|---|
| `404 Not Found` | URL incompleta — use o caminho exato do endpoint MCP |
| `timeout (30 s)` | Servidor lento ou inacessível da máquina da Api; aumente `ConnectTimeoutSeconds` ou verifique rede/proxy/VPN |
| Status `connected`, mas o modelo não usa as ferramentas | Agente sem o servidor em `McpServers`, `EnableTools: false`, ou modelo sem suporte a tools |
| `Type ... não suportado` | Use `streamable-http`, `sse`, `http` ou `stdio` |
| stdio: `command not found` | Instale o comando (ex.: Node.js para `npx`) na máquina da Api |

---

## 6. Segurança

- As ferramentas são executadas **automaticamente** quando o modelo pede, para **qualquer usuário de qualquer canal**
  (inclusive o Telegram, que é público se `AllowedUserIds` estiver vazio).
- Use credenciais com o **mínimo de permissão** e limite com `allowedTools`.
- Um servidor MCP pode tentar manipular o modelo pelas descrições ou resultados das ferramentas (*prompt injection*):
  cadastre só servidores confiáveis.
- Segredos sempre em user-secrets / variáveis de ambiente / Key Vault — nunca no `appsettings.json` commitado.
- `GET /api/mcp/servers` só existe em Development e mostra os headers mascarados.
