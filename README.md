# Leitor do Mercado Livre

Detecta produtos com demanda crescendo no Mercado Livre Brasil, mede preço de mercado e concorrência, diz **quanto vale a pena pagar** por cada um e calcula a margem de importar e revender. O brief completo está em [PROMPT.md](PROMPT.md).

## Como rodar

Dê um duplo clique em **Leitor do Mercado Livre** na área de trabalho — ou em `Iniciar.cmd`, na pasta do projeto.

O atalho sobe tudo, cada serviço na sua janela: banco (abrindo o Docker Desktop se preciso), Worker, API e painel. No fim, o navegador abre em http://localhost:5173. Para parar, feche as janelas; o banco continua no Docker.

Na primeira vez ele pede a **chave secreta** do app (DevCenter → seu app). Ela é guardada no cofre de segredos do .NET, fora do repositório, e não é pedida de novo.

Se a API já estiver rodando de uma execução anterior, o atalho identifica o processo e pergunta se pode encerrá-lo. Para não perguntar, use `.\Iniciar.ps1 -Forcar`.

Se o Worker avisar que a renovação do token foi recusada, refaça a autorização:

```powershell
.\Iniciar.ps1 -Autorizar
```

Abre o navegador, você autoriza, cola a URL de retorno, e o refresh token novo fica guardado. Depois é só usar o atalho de novo.

<details>
<summary>Subir cada serviço à mão</summary>

```powershell
docker compose up -d
dotnet run --project src/LeitorMercadoLivre.Worker      # coleta; precisa da credencial
dotnet run --project src/LeitorMercadoLivre.Api --launch-profile http
cd frontend; npm run dev
```

Pré-requisitos: .NET SDK 10, Node 24, Docker Desktop. Painel do Hangfire: http://localhost:5078/hangfire.

</details>

Para proteger a área do administrador fora desta máquina, defina `$env:Admin__ApiKey` na API e informe a mesma chave no painel.

## Quem está trabalhando

Ao abrir, o sistema pergunta **"Estou trabalhando com quem?"** e você escolhe o seu nome. Não há senha individual: a empresa tem uma conta só, e isso serve para a auditoria registrar quem mudou o quê. Dá para trocar a qualquer momento pelo link no topo.

- Fechar o navegador exige escolher de novo — ninguém herda o nome de quem usou a máquina antes.
- "José", "JOSE" e "josé" são a mesma pessoa; o sistema não cria operador duplicado.
- **Isto não é login.** Quem protege as escritas é a chave de administrador (`Admin__ApiKey`); a escolha do nome só atribui autoria.

## O painel

Quatro seções, cada uma com endereço próprio:

| Endereço | O que faz |
|---|---|
| `/oportunidades` | O quadro do último ciclo, na ordem do índice |
| `/explorar` | Barra de pesquisa + busca por filtros sobre a última análise de cada produto |
| `/administracao` | Edita a configuração de negócio |
| `/diagnostico` | O que a API do Mercado Livre responde hoje |

A barra de pesquisa em **Explorar** procura primeiro no que já foi coletado (de graça, enquanto você digita) e só vai ao Mercado Livre quando você clica — com o custo em chamadas escrito ao lado do botão. Colar link de anúncio não funciona (o ML não libera anúncio de terceiro): cole o **título** do produto.

Em **Explorar**, o filtro inteiro vai na URL: o link é compartilhável, sobrevive ao F5 e o botão voltar desfaz a última escolha. Filtros com nome ficam guardados neste navegador.

## Configuração de negócio

Tributos, parâmetros de venda, categorias e calibração ficam em [config/](config/), em JSON, validados por [config/configuracoes.schema.json](config/configuracoes.schema.json). A aba **Administração** do painel edita essa pasta; também é possível soltar arquivos `.json` nela (por um agente, por exemplo).

- Mudança é sempre uma versão nova com `vigenteDesde`; nada é sobrescrito, e cada análise grava a versão que usou.
- Arquivo com qualquer erro é ignorado por inteiro e aparece na área do administrador.
- Alíquotas são fração: `0.18` = 18%.

Os valores marcados como provisórios em `config/configuracoes.json` precisam ser confirmados: ICMS do seu estado, imposto do seu regime, frete absorvido e os pisos de demanda.

## Testes

```powershell
.\Testar.ps1          # para os serviços, sobe o Postgres e roda a suíte
cd frontend; npm test
```

Use o `Testar.ps1` em vez do `dotnet test` direto: com a API rodando, o build falha porque o Windows não deixa sobrescrever uma DLL em uso; e sem o Postgres, ~53 testes de integração são **pulados em silêncio**, o que dá uma suíte verde enganosa.

272 testes no back: domínio (171), infraestrutura (71, incluindo o ciclo completo e a busca filtrada contra Postgres) e API (30, rodando em cultura pt-BR). Os testes de integração são pulados se o Postgres não estiver de pé. No painel, 56 testes cobrem a ida e volta do filtro pela URL, o armazenamento dos filtros salvos e o teto de compra no card.

## Estrutura

- `src/LeitorMercadoLivre.Domain` — cálculos puros: preço de mercado, custo de importação, margem, demanda, índice.
- `src/LeitorMercadoLivre.Infrastructure` — banco, cliente do Mercado Livre, PTAX, configuração, pipeline.
- `src/LeitorMercadoLivre.Worker` — Hangfire: o ciclo de coleta.
- `src/LeitorMercadoLivre.Api` — cards, fornecedores, busca filtrada, administração, diagnóstico.
- `frontend` — painel React.
