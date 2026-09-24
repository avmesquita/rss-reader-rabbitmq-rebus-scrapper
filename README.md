# RSS Reader

[![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://github.com/codespaces/new?hide_repo_select=true&ref=main)
[![CI](https://github.com/avmesquita/rss/actions/workflows/ci.yml/badge.svg)](https://github.com/avmesquita/rss/actions/workflows/ci.yml)
[![Pages](https://github.com/avmesquita/rss/actions/workflows/pages.yml/badge.svg)](https://github.com/avmesquita/rss/actions/workflows/pages.yml)
[![Docker Images](https://github.com/avmesquita/rss/actions/workflows/docker.yml/badge.svg)](https://github.com/avmesquita/rss/actions/workflows/docker.yml)

Arquitetura modular para coletar artigos RSS com Rebus/RabbitMQ, extrair conteúdo pelo `amerkurev/scrapper`, persistir no PostgreSQL e consultar pelo Angular.

## Executar

```bash
docker compose up --build
```

- Frontend: http://localhost:6660
- API/Swagger: http://localhost:6661/swagger
- Scrapper: http://localhost:6662
- RabbitMQ: http://localhost:6664 (`rssreader` / `rssreader`)
- pgAdmin: http://localhost:7771 (`admin@rssreader.com` / `rssreader`)
- Adminer: http://localhost:7772

Para o pgAdmin ou Adminer, conecte ao servidor PostgreSQL usando host `rss_postgres`, porta `5432`, banco `rssreader`, usuário `rssreader` e a senha definida em `POSTGRES_PASSWORD` no `.env`. O host `localhost` só deve ser usado ao conectar a partir da máquina local, não de dentro dos containers.

As portas, nomes de serviço e credenciais de desenvolvimento ficam no arquivo `.env` na raiz. O Compose injeta esses valores na API e no worker; os arquivos `appsettings.json` não contêm parâmetros de infraestrutura.

A API cadastra um concentrador em `POST /api/feeds`. O comando é publicado no RabbitMQ; o worker lê cada item do RSS, chama `/api/article?url=...&cache=false` e grava o resultado.

A tabela `Articles` usa somente chave técnica e índice de data. Não há índice único por URL, título, GUID ou conteúdo: execuções repetidas preservam todas as ocorrências.

A consulta de notícias usa paginação no servidor para evitar carregar todas as imagens e conteúdos de uma vez. A página inicial exibe 10 itens; também estão disponíveis 25, 50 e 100 itens por página. Busca, categoria, fonte e favoritos são aplicados antes da paginação.

O worker agenda uma nova leitura das fontes ativas imediatamente ao iniciar e depois, por padrão, a cada 2 horas. Cada fonte também respeita `NextScheduledAt`, evitando leituras duplicadas. O intervalo pode ser alterado com `INGESTION_INTERVAL_HOURS` no `.env`. O consumo do scraper começa com um worker e paralelismo 1; ajuste `INGESTION_WORKERS` e `SCRAPPER_MAX_PARALLELISM` somente se a instância suportar mais carga. O dashboard permite forçar uma leitura por fonte, com intervalo mínimo de 30 minutos.

A consulta de notícias permite ordenar pela data de publicação ou de coleta e limitar o período às últimas 24 horas, 7 dias ou 30 dias.

Para habilitar o painel operacional no frontend, defina `DEBUG_ENABLED=true`. A API passa a expor `/api/debug`, que consulta as filas de erro/dead-letter no endpoint de gerenciamento do RabbitMQ e mantém os 50 erros mais recentes da API em memória. Deixe essa opção desativada em ambientes públicos. A API publica em `RABBITMQ_WORKER_QUEUE` e o Worker é o único consumidor dessa fila; mensagens antigas na fila `error` precisam ser reprocessadas ou removidas pelo RabbitMQ Management após a atualização.

## Fluxos

### Arquitetura

```mermaid
flowchart LR
	Browser["Navegador"] --> Frontend["Angular / Nginx"]
	Frontend --> API["RssReader.Api"]
	API --> DB[("PostgreSQL")]
	API --> Rabbit["RabbitMQ"]
	Rabbit --> Worker["RssReader.Worker"]
	Worker --> RSS["Fontes RSS / Atom"]
	Worker --> Scrapper["amerkurev/scrapper"]
	Scrapper --> Sites["Páginas das fontes"]
	Worker --> DB
	API --> RabbitAdmin["RabbitMQ Management API"]
	RabbitAdmin -. debug .-> Frontend
```

### Ingestão

```mermaid
sequenceDiagram
	participant U as Usuário ou scheduler
	participant A as API
	participant R as RabbitMQ
	participant W as Worker
	participant F as Feed RSS/Atom
	participant S as Scrapper
	participant D as PostgreSQL

	U->>A: Cadastra fonte
	A->>D: Persiste Feed
	A->>R: Publica IngestFeedCommand
	R->>W: Entrega comando
	W->>F: GET com User-Agent
	F-->>W: Itens RSS/Atom
	loop Cada item com link
		W->>S: Extrai artigo e Open Graph
		S-->>W: Título, texto, imagem e data
		alt Página de verificação humana
			W-->>W: Descarta item
		else Conteúdo válido
			W->>D: Persiste artigo extraído
		end
	end
```

### Consulta e leitura

```mermaid
flowchart TD
	Load["Frontend solicita /api/articles"] --> Query["API filtra e ordena"]
	Query --> DB[("PostgreSQL")]
	DB --> Cards["Cards paginados"]
	Cards --> Filters["Busca, categoria, fonte e favoritos"]
	Cards --> Modal["Modal com conteúdo extraído"]
	Modal --> Source["Abrir fonte original"]
	Cards --> Favorite["Favoritar / desfavoritar"]
	Favorite --> API["PUT /api/articles/{id}/favorite"]
	API --> DB
```

## Desenvolvimento

O repositório inclui uma configuração de GitHub Codespaces em `.devcontainer`. Depois de abrir o Codespace, o ambiente restaura a solução .NET e instala as dependências do Angular automaticamente. Para subir a infraestrutura local, use `docker compose up --build`.

O workflow `CI` valida o build Release da solução .NET, o type-check/build do Angular e a configuração do Docker Compose. O workflow `Deploy frontend to GitHub Pages` publica apenas o frontend estático na branch principal. A API, o PostgreSQL, o RabbitMQ e o scrapper continuam precisando ser hospedados separadamente; sem uma API pública configurada, a página publicada serve apenas o shell do frontend.

O workflow `Docker Images` constrói e publica as imagens no Docker Hub, na conta `avmesquita`, somente em push para a branch padrão. Configure no repositório os secrets `DOCKERHUB_USERNAME` e `DOCKERHUB_TOKEN`, usando um Access Token do Docker Hub com permissão de `Read & Write`.

Imagens publicadas:

- `avmesquita/rss-reader-api`
- `avmesquita/rss-reader-worker`
- `avmesquita/rss-reader-frontend`

Para publicar uma versão, crie e envie uma tag semântica no formato `vMAJOR.MINOR.PATCH`:

```bash
git tag v1.0.0
git push origin v1.0.0
```

O workflow publica as três imagens com as tags `1.0.0`, `1.0`, `1` e uma tag baseada no commit. A tag `latest` continua sendo atualizada somente pela branch padrão.
