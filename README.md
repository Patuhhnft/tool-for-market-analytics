# Leitor do Mercado Livre

Ferramenta de análise de oportunidades de importação no Mercado Livre Brasil.

Detecta produtos com demanda crescendo, mede preço de mercado e nível de concorrência, e responde **quanto vale a pagar** por cada produto antes de fechar com o fornecedor. O número que ela mostra é o número com que você vai gastar dinheiro.

## O que faz

- **Ciclo de coleta automático** — a cada 6 horas o Worker coleta visitas, preços e listings dos produtos monitorados via API do ML
- **Índice de oportunidade** — pontuação que combina demanda, margem e concorrência em ordem de prioridade
- **Teto de compra seguro** — custo máximo unitário que ainda entrega a margem-alvo, com o preço que aperta o limite
- **Runway de demanda** — estimativa de quantas semanas o produto vai continuar vendendo, cruzada com o prazo de entrega
- **Análise de kit** — comparação de margem avulsa vs. kit (a tarifa fixa do ML come margem em produto barato)
- **Riscos** — certificações exigidas (Inmetro, Anvisa, Anatel), presença de loja oficial, breakage e embalagem
- **Busca filtrada** — pesquisa por texto ou filtros (margem, runway, índice) com URL compartilhável

## Stack

**Backend:** .NET 10, ASP.NET Core, EF Core + Npgsql, PostgreSQL 17, Hangfire  
**Frontend:** React 19 + TypeScript + Vite, react-router-dom 7  
**Testes:** NUnit 4, Vitest + Testing Library — 272 no back, 56 no front

## Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 24](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Conta de desenvolvedor no [Mercado Livre DevCenter](https://developers.mercadolivre.com.br/) com um app criado

## Como rodar

Dê duplo clique em `Iniciar.cmd` (ou `Iniciar.ps1`) na pasta do projeto.

O script sobe tudo em sequência: banco (Docker), Worker, API e painel. No fim, o navegador abre em `http://localhost:5173`.

**Na primeira vez**, ele pede a chave secreta do app (DevCenter → seu app). Ela fica guardada no cofre de segredos do .NET, fora do repositório, e não é pedida de novo.

<details>
<summary>Subir cada serviço à mão</summary>

```powershell
docker compose up -d
dotnet run --project src/LeitorMercadoLivre.Worker
dotnet run --project src/LeitorMercadoLivre.Api --launch-profile http
cd frontend; npm run dev
```

Painel do Hangfire: `http://localhost:5078/hangfire`

</details>

### Autorização expirada

Se o Worker avisar que o refresh token foi recusado:

```powershell
.\Iniciar.ps1 -Autorizar
```

Abre o navegador, você autoriza, cola a URL de retorno, e o token novo fica guardado.

### Forçar reinício

Se a API já estiver rodando de uma execução anterior, o script identifica o processo e pergunta. Para não perguntar:

```powershell
.\Iniciar.ps1 -Forcar
```

## Configuração de negócio

Tributos, parâmetros de venda e calibração ficam em [`config/`](config/), em JSON, validados por [`config/configuracoes.schema.json`](config/configuracoes.schema.json).

- Mudança é sempre uma versão nova com `vigenteDesde` — nada é sobrescrito
- Cada análise grava a versão de configuração que usou (auditável)
- Alíquotas são fração: `0.18` = 18%
- A aba **Administração** do painel edita essa pasta

Os valores marcados como provisórios em `config/configuracoes.json` precisam ser confirmados para o seu negócio: ICMS do estado, imposto do regime, frete absorvido e pisos de demanda.

## Testes

```powershell
.\Testar.ps1          # para os serviços, sobe o Postgres e roda a suíte
cd frontend; npm test
```

> Use `Testar.ps1` em vez de `dotnet test` diretamente: com a API rodando o Windows não deixa sobrescrever a DLL em uso; sem o Postgres, ~53 testes de integração são **pulados em silêncio**, dando verde enganoso.

Cobertura: domínio (171), infraestrutura (71, inclui ciclo completo e busca filtrada contra Postgres), API (30, em cultura pt-BR), painel (56).

## Estrutura

```
src/
  LeitorMercadoLivre.Domain/         cálculos puros: preço, margem, demanda, índice
  LeitorMercadoLivre.Infrastructure/ banco, cliente ML, PTAX, pipeline de coleta
  LeitorMercadoLivre.Worker/         Hangfire: ciclo de coleta a cada 6h
  LeitorMercadoLivre.Api/            cards, busca, administração, diagnóstico
frontend/                            painel React
config/                              configuração de negócio (JSON)
tests/
```

## Segurança

- A **chave secreta** do app ML fica em `dotnet user-secrets`, fora do repositório
- Os **tokens de acesso** ficam no Postgres, nunca em arquivo
- A senha `leitor_dev` no `docker-compose.yml` é do container local (`127.0.0.1:5432`) — não use em produção
- Para proteger a área de administração fora desta máquina: defina `Admin__ApiKey` na API e informe a mesma chave no painel

## Operadores

Ao abrir, o sistema pergunta **"Estou trabalhando com quem?"**. Não há senha individual — serve para auditoria registrar quem mudou o quê.

- Fechar o navegador exige escolher de novo
- "José", "JOSE" e "josé" são a mesma pessoa — sem duplicata
- **Não é login**: quem protege as escritas é o `Admin__ApiKey`

## Especificação completa

[PROMPT.md](PROMPT.md)
