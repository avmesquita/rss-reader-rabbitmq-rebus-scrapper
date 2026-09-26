# Guia para agentes

## Visão geral

RSS Reader é uma aplicação composta por frontend Angular, API ASP.NET Core, worker de ingestão e processamento e PostgreSQL. A API recebe operações do frontend, persiste e consulta dados e publica `IngestFeedCommand` via Rebus/RabbitMQ. O worker lê feeds RSS/Atom, envia `ProcessArticleCommand` para a fila de artigos, usa o serviço externo `scrapper` para extrair conteúdo e grava artigos e informações de execução. Os contratos compartilhados ficam em `src/RssReader.Contracts`.

## Estrutura

- `src/RssReader.Api`: endpoints HTTP, acesso a dados e publicação de comandos.
- `src/RssReader.Worker`: handlers das filas, agendamento, leitura de feeds e extração de artigos.
- `src/RssReader.Contracts`: mensagens compartilhadas entre API e worker.
- `frontend/src/app`: aplicação Angular e componentes da interface.
- `docker-compose.yml`: stack local com frontend, API, workers, PostgreSQL, RabbitMQ e serviços administrativos.

Leia o `README.md` antes de alterar arquitetura, configuração local ou operação da stack. Não duplique contratos de mensagens: mudanças em tipos de mensagem devem ser coordenadas com produtores e consumidores.

## Convenções de implementação

- Preserve a separação entre API, contratos e processamento assíncrono; não mova extração de artigos para requisições síncronas do frontend/API.
- Use `RssReader.Contracts` para comandos compartilhados por processos. Considere compatibilidade de mensagens ao alterar nomes ou campos, pois mensagens podem permanecer nas filas.
- Mantenha consultas e gravações PostgreSQL coerentes nos modelos/contextos usados tanto pela API quanto pelo worker.
- Preserve deduplicação de artigos e o tratamento de erros de ingestão ao modificar o fluxo.
- No Angular, siga os padrões existentes em `frontend/src/app` e mantenha tipos de payload alinhados aos endpoints da API.
- Leia configurações por `IConfiguration`/variáveis de ambiente; não inclua segredos em código ou arquivos versionados. `.env.example` documenta a configuração da stack.
- Faça mudanças focadas e atualize o `README.md` quando mudar comandos de desenvolvimento, configuração, endpoints ou arquitetura.

## Comandos úteis

Na raiz:

```bash
docker compose up --build
dotnet build RssReader.slnx -c Release
docker compose config
```

No diretório `frontend`:

```bash
npm ci
npm start
npm run build
npm test
```

`npm test` executa os testes Angular configurados pelo CLI. A CI também valida o build Release .NET, o build/type-check do frontend e a configuração do Compose. Execute verificações pertinentes às áreas alteradas; não assuma que existe cobertura automatizada além dos testes presentes no repositório.

## Segurança e operação

- O endpoint/painel de diagnóstico é controlado por `DEBUG_ENABLED`; mantenha-o desativado em ambientes públicos.
- Não exponha credenciais do PostgreSQL, RabbitMQ, pgAdmin ou tokens em logs, documentação, exemplos ou commits.
- Ao mexer em filas, variáveis de ambiente, portas ou serviços, verifique também `docker-compose.yml` e `.env.example`.
- Alterações no schema precisam considerar que a API atualmente aplica `EnsureCreated` e ajustes SQL de compatibilidade no startup; não suponha que há migrations EF Core configuradas.
