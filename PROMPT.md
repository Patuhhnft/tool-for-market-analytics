# Leitor do Mercado Livre — brief de construção

Você vai construir comigo o **Leitor do Mercado Livre**: uma ferramenta que detecta
produtos com aceleração de demanda no Mercado Livre Brasil, mede a concorrência,
encontra fornecedores e estima quanto sobra de margem se eu comprar e revender.

O produto final é uma **ferramenta de decisão de compra**. O número que ela mostra é
o número com que eu vou gastar dinheiro. Precisão honesta vale mais que número
bonito: prefiro ver "não dá para saber" a ver uma margem de 41% que na prática é 12%.

---

## 1. Invariantes

Valem para todo o projeto. Uma entrega que viole qualquer uma delas está errada,
mesmo que compile e passe nos testes.

**I1 — Dinheiro é `decimal`.** Todo valor monetário, alíquota e percentual de cálculo
é `decimal` em C# e `numeric` no Postgres. `double`/`float` em cálculo de dinheiro é
bug, não estilo.

**I2 — Arredondamento sem viés.** Nenhuma etapa pode arredondar sistematicamente para
cima. Onde houver arredondamento, ele é para o valor mais próximo
(`MidpointRounding.AwayFromZero` só para o empate exato — o §5 explica por que não
meia-para-par). Truncar também é proibido: separaria R$ 19,99 de R$ 20,00.

**I3 — Arredondar só na borda.** Cálculo intermediário roda com precisão cheia.
Arredonda-se para exibir, ou quando a regra de negócio pede explicitamente (o
agrupamento em baldes de preço). Nunca arredondar, guardar arredondado e recalcular
em cima.

**I4 — Número nu não sai.** Todo preço e toda margem exibidos saem com tamanho de
amostra, faixa (piso e teto) e selo de confiança. Margem como número único esconde o
risco e é proibida na interface.

**I5 — Sem âncora de produto, sem preço.** Só entram na amostra de preço anúncios que
eu consiga afirmar que são do **mesmo produto** (mesmo `product_id` de catálogo, ou
equivalente comprovado pelo diagnóstico). Amostra montada por semelhança de título
produz uma moda sem significado. Sem âncora: status `sem_ancora_de_produto`, nada de
preço.

**I6 — Falta de dado vira aviso, nunca número inventado.** Sem série de visitas, com
amostra pequena, sem comissão real da API: o card diz o que falta, em vez de exibir
falsa precisão. E o card nunca chama visita de venda (§3).

**I7 — Tudo auditável.** Todo cálculo persiste entradas, parâmetros usados (com
versão), saídas intermediárias e um hash da amostra de preços. Eu tenho que conseguir
reproduzir qualquer número da tela meses depois.

**I8 — Zero constante no código.** Parâmetros em `appsettings`. Alíquotas e limiares
legais (ICMS, II, limiar de frete grátis, Remessa Conforme) ficam em **tabela
versionada por data de vigência** — a lei muda e o histórico não pode ser reescrito.

**I9 — Fator de ranking sempre em [0,1].** Nenhum fator do índice de oportunidade
pode sair do intervalo; todos passam por clamp explícito antes de compor.

**I10 — Job idempotente.** Reexecutar um ciclo não duplica snapshot nem distorce a
série histórica. Chave única por (alvo, janela de coleta) e upsert.

**I11 — Volume pesa mais que percentual.** Nenhum indicador de crescimento ou de
oportunidade pode ser dominado por base pequena. Toda taxa % anda acompanhada do
número absoluto que a gerou, na conta e na tela. De 2 para 20 visitas são +900% e não
são um mercado.

---

## 2. Stack e convenções

- Backend: .NET 10, ASP.NET Core, Worker Service com Hangfire (edição gratuita)
- Banco: PostgreSQL via EF Core / Npgsql, migrations versionadas
- Frontend: React + TypeScript
- Código modular, com comentário inline nas decisões não óbvias (não no óbvio)
- CSS em arquivos separados, em seções numeradas e comentadas
- Segredos só em variável de ambiente. Nada de token em `appsettings` ou no repo
- **Antes de escrever a primeira suíte de testes, me pergunte qual framework usar.**
  Depois disso, cubra caminho feliz e as bordas que importam

---

## 3. A API do Mercado Livre: medido em 24/09/2026

O diagnóstico rodou com token real (categoria MLB1051, produto MLB54982411, anúncio
MLB6716754230). Isto não é documentação, é o que a API respondeu:

| Endpoint | Resultado |
|---|---|
| `/highlights/MLB/category/{cat}` | **200** — ranking BEST_SELLER com posição e IDs de catálogo |
| `/products/{id}/items` | **200** — anúncios do mesmo produto, com `seller_id`, `price`, `condition`, `listing_type_id`, `shipping` |
| `/sites/MLB/listing_prices?price=X` | **200** — comissão real por tipo de anúncio (`price` é obrigatório) |
| `/visits/items?ids=` | **200** — visitas acumuladas por anúncio, em lote |
| `/items/{id}/visits/time_window?last=7&unit=day` | **200** — série diária de visitas |
| `/trends/MLB` e `/trends/MLB/{cat}` | **404** `Not found public trends` |
| `/items/{id}`, `/items/bulk`, `/items/{id}?attributes=` | **403** `access_denied` |

Três consequências que mandam no resto do documento:

**Não existe mais trends público.** A coleta começa pelo `/highlights` por categoria.
Nenhum endpoint entrega "crescimento" pronto: crescimento é sempre derivado dos nossos
próprios snapshots, o que torna o banco parte do cálculo, não um detalhe de persistência.

**`sold_quantity` é inalcançável.** As três formas de ler o item deram 403, e o
`/products/{id}/items` não traz o campo. Venda real não é mensurável, e qualquer
promessa de "vendas exponenciais" seria invenção.

**Visitas substituem vendas como sinal de demanda** (§7). É melhor que posição no
ranking — número contínuo, por anúncio, com série diária, e disponível para anúncio de
terceiro. Mas **visita não é venda**: a conversão varia por categoria e preço, e a tela
tem que dizer isso.

Outras restrições, ainda válidas:

- `/sites/{site}/search` sem `seller_id` está bloqueado — não existe busca livre
- `/highlights` devolve só `PRODUCT`/`USER_PRODUCT`; `buy_box_winner` vem nulo
- Todas as chamadas usam OAuth (DevCenter). O access token dura 6h, então o Worker
  precisa renovar sozinho pelo `refresh_token` — sem isso o ciclo da madrugada morre
- O `/items/bulk` responde **200 no envelope mesmo quando cada item falha**; o status
  real vem dentro de cada elemento. Qualquer leitor desse endpoint tem que olhar lá
  dentro, ou vai registrar sucesso onde houve 403

Perguntas ainda abertas, para o diagnóstico responder quando houver necessidade:

1. Qual o teto de requisições por minuto que o token aguenta na prática?
2. O 403 de `/items/{id}` é escopo da aplicação ou restrição geral a anúncio de
   terceiro? Só dá para separar testando com um anúncio próprio
3. A série de `/visits/time_window` aceita janela maior que 7 dias sem degradar?

---

## 4. Parâmetros (`appsettings`, com estes padrões)

| Parâmetro | Padrão |
|---|---|
| Intervalo de coleta | 6 horas |
| Finalistas por ciclo | 20 |
| Limite de balde inteiro | R$ 200 (acima disso, balde de R$ 5) |
| Corte de extremos | 15% de cada lado |
| Amostra mínima para corte | 7 vendedores |
| Força mínima da moda | 20% dos vendedores |
| Mínimo absoluto de vendedores no balde modal | 3 |
| Tolerância da seta de tendência | 1% |
| Margem-alvo | 30% |
| Spread cambial (IOF + spread do meio de pagamento) | 6% |
| Piso de demanda para crescimento relevante | 300 visitas no produto em 7 dias — **provisório, calibrar** |
| Meia-escala de demanda (`D`, §7) | 500 visitas — **provisório, calibrar** |
| Meia-escala da taxa de crescimento (`k`, §7) | 0,5 |
| Janela de persistência | 4 semanas |
| TTL: fornecedores / PTAX / comissão / highlights, visitas e preços | 7 dias / 1 dia / 1 dia / 6 horas |
| Top-N para enriquecimento por LLM | 5 |

Os dois valores marcados como provisórios são chute informado: um único anúncio fez 803
visitas em 7 dias na primeira medição. Só a distribuição real de alguns ciclos diz onde
o piso deve ficar, e até lá eles ficam explícitos como calibração pendente.

Por produto/regime: alíquota do regime tributário, ICMS do estado, NCM.

Em tabela versionada por vigência: II por NCM, IPI, PIS, Cofins, regra da Remessa
Conforme, limiar de frete grátis (hoje R$ 79). Nenhum desses valores fixo no código.

---

## 5. `PriceReferenceCalculator` — o preço de mercado

Classe isolada, sem I/O, testável em memória. Objetivo: **o preço em que mais
vendedores diferentes convergem**. Média aritmética, nunca.

**Etapa 0 — Normalizar a amostra**
- Só anúncios novos, ativos, com estoque, ancorados ao mesmo produto (I5)
- Kit e pacote convertidos para preço por unidade
- **Um anúncio por vendedor, o mais barato.** Regra obrigatória: é ela que impede um
  vendedor com vinte anúncios de fabricar uma moda sozinho
- Registrar quantos anúncios foram descartados e por qual motivo

**Etapa 1 — Ignorar centavos, sem viés**
- Até o limite de balde inteiro (R$ 200): arredondar para o **real inteiro mais
  próximo**. R$ 19,90 / R$ 19,99 / R$ 20,00 → R$ 20; R$ 23,40 → R$ 23;
  R$ 20,50 → R$ 21; R$ 20,49 → R$ 20
- Acima do limite: **múltiplo de R$ 5 mais próximo**. R$ 347 → R$ 345; R$ 349 → R$ 350
- Por que não "para cima": R$ 23,40 → R$ 24 empurra quase um real para cima em um
  número que alimenta lucro e margem. O agrupamento de 19,90/19,99/20,00 já é obtido
  pelo mais-próximo, que não enviesa (I2)
- Por que não meia-para-par (banker's), apesar de ser o padrão "mais neutro":
  aqui o arredondamento define **em que balde o vendedor cai**, e o empate
  R$ X,50 não é raro — preço terminado em ,50 é comum no varejo brasileiro.
  Com meia-para-par, o balde de valor par recolhe os dois midpoints vizinhos
  (19,50 e 20,50 → 20) e o balde ímpar não recolhe nenhum: baldes pares ficam
  sistematicamente mais populosos e passam a **ganhar a moda pela paridade**,
  que é um viés pior que o do midpoint. Meia-para-cima mantém todo balde com
  largura idêntica de R$ 1,00, em [X − 0,50, X + 0,50). O resíduo de viés fica
  restrito aos centavos exatos ,50 e está documentado aqui

**Etapa 2 — Cortar extremos**
- Ordenar e descartar `floor(0,15 × n)` de cada ponta, com `n` = vendedores distintos
- 7 ou mais vendedores: corta. 3 a 6: não corta e marca `amostra_pequena`.
  Menos de 3: não calcula, status `dados_insuficientes`

**Etapa 3 — Moda por vendedor, validada pela mediana**
- Contar **vendedores distintos** por balde na lista limpa; balde modal = o de maior
  contagem
- Empate: o balde mais próximo da mediana; persistindo, o mais baixo (conservador,
  protege a margem)
- Mediana da lista limpa (quantidade par: o central mais baixo)
- Usa-se a **moda** como `preco_mercado` só quando as três condições valem:
  (a) o balde modal atinge a força mínima (20% dos vendedores), **(b) o balde modal
  tem pelo menos o mínimo absoluto de vendedores (3)** e (c) está dentro da faixa
  limpa (p15–p85)
- A condição (b) existe porque percentual sozinho mente em amostra pequena: 20% de 7
  vendedores são 2 pessoas, e duas pessoas cobrando o mesmo valor não são um preço de
  mercado — são coincidência
- Falhando qualquer uma, **mediana** + flag `mercado_disperso`

**Saídas** (todas persistidas no snapshot)

`preco_mercado`, `fonte_preco` (moda|mediana), `moda`, `mediana`, `forca_moda` (% de
vendedores no balde modal), `contagem_balde_modal` (o número absoluto, que é o que a
condição (b) checa e o que o painel exibe ao lado do percentual), `faixa_limpa`
(p15–p85), `preco_vitrine_sugerido`
(`preco_mercado` − R$ 0,10), `indice_dispersao` ((p85 − p15) / mediana),
`tamanho_amostra`, `anuncios_descartados`, flags, variação semanal de `preco_mercado`
e de `forca_moda`, `hash_amostra`, e:

`selo_confianca_preco` — alta: ≥ 15 vendedores e dispersão < 0,25; baixa: < 7
vendedores ou dispersão > 0,50; média no resto. Limiares configuráveis.

---

## 6. `MarginCalculator` — quanto sobra

Também isolada e testável. Calcula a margem em **três pontos de preço** — piso (p15),
preço de mercado e teto (p85) — e, separadamente, a **pior margem de toda a faixa**
praticada (I4).

Os três pontos **não** são mínimo, esperado e máximo. O limiar de frete faz a margem
cair de degrau dentro da faixa: com piso em R$ 76 e mercado em R$ 85, o piso pode ter
margem *maior* que o mercado, porque abaixo do limiar quem paga o frete é o comprador.
E o pior ponto pode não ser nenhum dos três — no teste de referência ele fica
exatamente em R$ 79,00, com 14,7%, contra 29,6% no piso e 19,2% no mercado. Por isso a
pior margem é calculada à parte, e é ela que decide a compra.

### 6.1 Teto de compra — o número que existe sem fornecedor

`margem(P) ≥ alvo  ⇔  custo ≤ P × (1 − alvo) − custo_venda(P)`. Nada nessa conta depende
de quem me vende: só do preço que o mercado pratica, das tarifas do Mercado Livre e da
margem-alvo. Por isso o **teto de compra existe desde o primeiro ciclo**, e a margem só
existe depois de haver fornecedor cadastrado.

É a diferença entre duas perguntas:

| Pergunta | Precisa de fornecedor? | Onde aparece |
|---|---|---|
| "Quanto vale a pena pagar por isto?" | Não | Teto de compra, sempre |
| "Este fornecedor compensa?" | Sim | Margem |

O teto vem em dois números e um lugar:

- **No preço de mercado** — o otimista, válido só se eu vender no preço de mercado
- **Na faixa inteira** — o conservador, que aguenta qualquer preço da faixa praticada. **É
  este que se leva para a negociação**
- **Onde aperta** — o preço da faixa que define o teto. Costuma ser o piso, mas não sempre:
  no caso de referência é o limiar de frete grátis, R$ 79. Sem dizer onde aperta, o número
  parece arbitrário

Teto negativo significa que as tarifas e o imposto sozinhos já comem o alvo: **nenhum preço
de compra fecha**. Isso é exibido como aviso, nunca como dinheiro negativo — "não dá" é uma
resposta, "−R$ 34,74" não é.

O cálculo é exato, sem varrer centavos: entre dois pontos de quebra da tabela de
tarifas, lucro e margem são crescentes no preço, então o pior caso de cada trecho está
no começo dele e o equilíbrio de cada trecho tem fórmula fechada.

**Custo de aquisição**

```
custo_unitario = (preco_fornecedor + frete_intl/qtd) × cambio_efetivo
               + tributos_importacao
               + frete_nacional_ate_mim
```

- `cambio_efetivo = PTAX_venda × (1 + spread_cambial)`. **PTAX pura é proibida no
  custo**: o câmbio que eu realmente pago embute IOF e spread do meio de pagamento —
  são ~4 a 6% que somem da margem. PTAX via API do Banco Central, cache diário; em
  fim de semana ou feriado, usar o último dia útil disponível e registrar qual foi
- Dois modos: **amostra** (Remessa Conforme, poucas unidades) e **lote** (importação
  formal: II por NCM, IPI, PIS, Cofins, ICMS)
- **ICMS por dentro, com a fórmula explícita:**
  ```
  base_icms = valor_sem_icms / (1 − aliquota_icms)
  icms      = base_icms × aliquota_icms
  ```
  Multiplicar direto (`valor × alíquota`) subestima o imposto e é proibido. Cada
  tributo aplicado entra na memória de cálculo, linha a linha

**Custo de venda**, para cada um dos três preços `P`:

```
custo_venda(P) = P × comissao(listing_prices)
               + custo_fixo_por_unidade     (se P < limiar de frete grátis)
               + frete_absorvido            (se P ≥ limiar)
               + P × imposto_regime

lucro(P)  = P − custo_unitario − custo_venda(P)
margem(P) = lucro(P) / P
```

`custo_venda` é **função do preço**, e os três pontos passam cada um pela função
inteira. **É proibido calcular o custo no preço de mercado e reaproveitá-lo no piso e
no teto.** Dois termos viram com o preço: a comissão é percentual, e a regra do frete
troca de estrutura no limiar. Um item com preço de mercado R$ 85 e piso R$ 76 está de
um lado do limiar no piso e do outro no preço de mercado — reaproveitar o custo
erraria a margem do piso por mais de 20 pontos percentuais.

**Dois câmbios, não um.** O que é pago no exterior sai pelo câmbio efetivo (PTAX ×
(1 + spread)). A **base dos tributos** é o valor aduaneiro pelo câmbio fiscal. Aplicar o
câmbio efetivo na base inflaria o imposto; aplicar a PTAX no pagamento esconderia o
spread — e esse segundo erro é o perigoso, porque infla a margem.

Tudo que é da remessa (frete internacional, tributos, dedução do II, frete nacional) é
calculado no **total da remessa** e dividido por unidade no fim. A dedução do II na
Remessa Conforme é por remessa; dividir antes a aplicaria uma vez por peça.

**Saídas:**

- lucro e margem nos três pontos, cada um com o custo de venda decomposto
- **pior margem da faixa**, com o preço em que ela ocorre
- ROI no preço de mercado
- **preço mínimo de venda** — o menor preço a partir do qual há lucro
- **zonas de prejuízo** acima do equilíbrio: faixas onde vender um pouco mais caro dá
  prejuízo, porque o frete absorvido no limiar come o lucro
- **dois tetos de compra**: o que garante a margem-alvo no preço de mercado, e o
  **seguro**, que a garante em qualquer preço da faixa. O seguro é o que se recomenda
- **preço máximo no fornecedor**, em moeda estrangeira: o teto de custo convertido de
  volta pela cadeia inteira de câmbio e tributo — o número de "quanto posso pagar"
- memória de cálculo linha a linha

---

## 7. Demanda e índice de oportunidade

**A unidade de demanda é a visita, não a venda.** O §3 mostra por quê: `sold_quantity`
está bloqueado nas três formas de leitura. O que a API entrega é `/visits/items`
(acumulado por anúncio, em lote e barato) e `/items/{id}/visits/time_window` (série
diária). A demanda de um **produto** é a soma das visitas dos anúncios ancorados nele.

Isso é um proxy, e o documento inteiro o trata como tal: **visita não é venda**. A
conversão varia por categoria e por preço, e nenhum texto da tela pode dizer "vendeu".
O que a ferramenta afirma é que o interesse por aquele produto está crescendo.

Duas regras de higiene da série, descobertas na primeira leitura real e não negociáveis:

- **Dia sem visita é omitido pela resposta.** Pedimos 7 dias e vieram 4. O coletor
  preenche a janela inteira, com zero onde a API não mandou nada. Iterar só o que veio
  faz o delta e a segunda diferença mentirem
- **O dia corrente vem parcial.** As visitas de hoje são de algumas horas, não do dia.
  O dia de hoje fica **fora** de qualquer delta — senão todo produto parece em colapso
  toda manhã e volta a crescer toda noite

Com a série limpa e preenchida:

- `delta_abs` = visitas dos últimos 7 dias completos − as dos 7 dias anteriores
- `taxa` = `delta_abs / max(base_anterior, 1)`
- `v_norm = delta_abs / (delta_abs + D)` — componente de volume, saturante, com `D` a
  meia-escala de demanda (nela, `v_norm` = 0,5)
- `t_norm = log1p(taxa) / (log1p(taxa) + log1p(k))` — componente de taxa. O `log1p`
  comprime a explosão de base pequena; em `taxa = k`, `t_norm` = 0,5
- `g_demanda = sqrt(v_norm × t_norm)` — exige **as duas coisas**. Volume grande e
  estagnado dá nota baixa; foguete de base minúscula também
- Aceleração (segunda diferença da série) e persistência (semanas consecutivas subindo)
  saem da mesma série diária
- **Piso de demanda:** produto com `delta_abs` abaixo do piso recebe a flag
  `demanda_baixa` e vai para o **segundo bloco da ordenação** — abaixo de todos os que
  passaram do piso, por mais alto que seja seu índice. É barreira de ordenação, não
  penalidade no número, para não inventar um fator mágico
- Demanda caindo → `g_demanda` = 0

**Percentual sozinho continua proibido** (I11). De 2 para 20 visitas são +900%; de 5.000
para 8.000 são +60%. Ranquear por taxa põe o primeiro acima do segundo, e o primeiro não
é mercado, é ruído.

**Fallback: posição no `/highlights`.** Se a série de visitas falhar para um produto,
resta a variação de posição no ranking BEST_SELLER entre coletas. Posição é ordinal —
40→30 não é comparável a 10→1 — então entra com confiança menor e selo próprio no card.
Nunca como demanda medida.

**Índice de oportunidade** — média geométrica de cinco fatores, todos em [0,1]:

```
indice = (g_demanda × f_oferta × (1 − pressao_preco) × f_persistencia × f_margem) ^ (1/5)
```

A média geométrica preserva a propriedade que interessa — **um fator em zero zera o
índice** — sem esmagar o ranking inteiro perto de zero, como o produto puro faria.

- `g_demanda`: o valor definido acima. As duas meia-escalas (`D` e `k`) são fixas e
  configuradas. **Não usar min-max da coorte do ciclo**: isso torna o índice
  incomparável entre semanas, e o menos ruim de uma coorte fraca vira nota 1,0
- `f_oferta = 1 / (1 + crescimento_oferta)`, com `crescimento_oferta` clampado em ≥ 0
- `pressao_preco`: média ponderada da queda % do preço de mercado em 7 dias, do índice
  de dispersão e da queda da força da moda; pesos configuráveis somando 1; clamp em
  [0,1] — sem isso, `pressao > 1` inverteria o sinal e produziria índice negativo
- `f_persistencia`: semanas subindo / janela de persistência
- `f_margem = clamp(pior_margem_faixa / margem_alvo, 0, 1)`; 0 se `pior_margem_faixa ≤ 0`.
  Usa a pior margem da faixa, não a do mercado: o índice ordena oportunidades de compra,
  e quem decide a compra é o pior caso (§6)

Margem negativa ou preço em colapso zeram o índice **de propósito**. Documente isso no
código: é regra de negócio, não efeito colateral.

O índice é uma **ordem de prioridade, não um placar de lucro**. Com as escalas fixas
ele é comparável entre ciclos, mas 0,62 não significa "62% de alguma coisa" — quem
promete dinheiro é o bloco de margem. O card e os logs devem dizer isso; o número
sozinho convida à leitura errada.

Quadrantes ao final: janela aberta, corrida, saturando, fim de ciclo.

---

## 8. Pipeline (Hangfire)

1. Coletar `/highlights/MLB/category/{cat}` por categoria alvo, pulando as verticais que
   não são produto físico importável — Imóveis (MLB1459), Serviços (MLB1540), Ingressos
   (MLB218519) e Carros, Motos e Outros (MLB1743)
2. Gravar snapshot: produto de catálogo, categoria, posição no BEST_SELLER, data — e as
   visitas acumuladas dos anúncios, via `/visits/items?ids=` em lote
3. Calcular demanda e crescimento (§7) a partir da série diária
   (`/items/{id}/visits/time_window`), preenchendo com zero os dias que a API omitir e
   descartando o dia corrente. Rotular medido vs. proxy por ranking
4. Selecionar os finalistas do ciclo
5. Para cada finalista, coletar concorrência: nº de vendedores, entrantes da semana
   (tabela `seen_seller` com `first_seen_at`), reputação média, preços de todos os
   anúncios
6. Rodar o `PriceReferenceCalculator` e persistir todas as saídas
7. Buscar fornecedores (AliExpress Affiliate API e/ou busca web `site:alibaba.com`):
   nome, preço, pedido mínimo, contato, link `wa.me/55...` ou URL do site. Respeitar o
   TTL: produto já visto nos últimos 7 dias não é rebuscado
8. Rodar o `MarginCalculator` nos três pontos de preço
9. Calcular o índice de oportunidade e classificar em quadrantes
10. Enriquecer "por que vende agora" só para o **top-N** (§10)

Todo job respeita TTL de cache e rate limit. Chamada que não muda a resposta é
dinheiro jogado fora — fornecedores, câmbio e comissão mudam devagar; preço e
demanda, rápido.

Atenção ao orçamento de chamadas das visitas: `/visits/items?ids=` aceita vários
anúncios de uma vez e serve para varrer tudo barato, mas a série diária é **uma chamada
por anúncio**. Com 20 finalistas de ~20 anúncios cada, a série ingênua custaria 400
requisições por ciclo. A série só é buscada para os anúncios dos finalistas, e só depois
do corte.

---

## 9. Painel React

Card por produto: nome, categoria, quadrante, índice de oportunidade, curva de
demanda, nº de concorrentes e entrantes na semana, fornecedores com botão de contato.
Ordenação padrão pelo índice de oportunidade.

Bloco de preço e margem, nesta ordem (valores ilustrativos):

| Campo | Exemplo |
|---|---|
| Preço de mercado | R$ 90 ▲ |
| ↳ fonte | moda · 34% (17 vendedores) |
| ↳ confiança | alta · 18 vendedores |
| Faixa praticada (p15–p85) | R$ 78 – R$ 105 |
| Preço de vitrine sugerido | R$ 89,90 |
| Custo unitário (fornecedor) | R$ 28,40 |
| Taxas ML + imposto | R$ 24,10 |
| **Lucro no preço de mercado** | **R$ 37,40** |
| **Margem (piso · mercado · teto)** | **22% · 41,6% · 55%** |
| **Pior margem da faixa** | **14,7% em R$ 79,00** |

Regras do bloco:

- Faixa praticada ao lado do preço de mercado, para eu ver se há espaço acima dele
- Seta ▲/▼/— conforme a variação semanal passe da tolerância; % no tooltip
- Fonte do preço sempre visível, **com percentual e contagem** ("moda · X% (N
  vendedores)" ou "mediana · mercado disperso"). Só o percentual esconde que 34% podem
  ser 2 pessoas
- Confiança do preço sempre visível (alta/média/baixa)
- Margem sempre em faixa, e a **pior margem da faixa** sempre visível ao lado, com o
  preço em que ocorre. **Se ela ficar abaixo da margem-alvo, cor de alerta** — é o pior
  caso realista, e é ela que decide a compra. Não confundir com a margem do piso
- Zona de prejuízo acima do equilíbrio, quando existir, aparece por extenso: "entre
  R$ 79,00 e R$ 83,33 cada venda dá prejuízo"
- Dispersão alta ou força da moda caindo: selo "preço instável"
- `amostra_pequena`: aviso junto ao preço
- `dados_insuficientes` ou `sem_ancora_de_produto`: nada de preço nem de margem, só o
  aviso
- Demanda sempre rotulada como **visitas**, nunca como venda: "1.240 visitas na semana
  (+38%)". A palavra "vendeu" não aparece em nenhum lugar do card — o §3 explica por quê
- **Percentual de crescimento só quando ele existe.** O cálculo de `Rate` divide por
  `max(anterior, 1)` para não estourar, e esse `1` produz um número mesmo quando não há
  razão alguma a calcular — um produto que saiu de 0 para 19.989 visitas chegou a exibir
  "+1.998.900%". Quem decide a exibição é `VisitGrowth`, nunca `Rate` cru:
  - **semana anterior = 0** → não há percentual, há estreia: "estreou em 19/09 e fez 19.989
    visitas em 7 dias", com selo **estreia** no card. A data diz o que o percentual escondia:
    que antes não existia
  - **base abaixo do piso** (padrão 50 visitas) → o absoluto e o motivo: "recebeu 18 visitas
    a mais que na semana anterior (base pequena demais para percentual)" (I11)
  - **base normal** → percentual como sempre
- A frase do card e o bloco de visitas leem a MESMA fonte. Eles divergirem foi um bug real:
  o filtro do Explorar já descartava a taxa que os dois textos exibiam
- Demanda vinda do proxy de ranking: selo "demanda estimada por posição"
- `demanda_baixa`: selo "demanda baixa · N visitas na semana", para o card não deixar um
  +900% de base pequena passar por mercado quente
- **Teto de compra sempre visível, mesmo sem fornecedor** (§6.1). É o único número
  acionável do card antes de existir fornecedor cadastrado: sem ele, o card diz "olhe para
  este produto" e para por aí. Com ele, diz "olhe para este produto e não pague mais que
  R$ X". Vem com o alvo que o gerou e com o preço onde a restrição aperta
- Quando existe margem completa, ela já traz o teto na própria tabela: o bloco separado
  some, para não dizer a mesma coisa duas vezes
- Memória de cálculo da margem acessível a partir do card (I7)

## 6.2 O que o produto É — marca, certificação, embalagem e quebra

Quatro coisas que o modelo ignorava e que vêm de testes com produto real.

**Loja oficial.** O `official_store_id` era coletado desde o primeiro ciclo e **nunca usado**.
Medido em 28/09/2026 nos potes Rishon: a loja oficial tinha **10 dos 19 anúncios**, mas era
**1 de 10 vendedores**, e **não era a mais barata** (R$ 97,62 contra R$ 93,87). O que atrapalha
ali não é preço — é a marca ocupar metade do que o comprador vê.

Por isso o selo `BrandDominated` mede **presença na prateleira**, não preço. A primeira versão
media preço e acendeu em **28 de 36 produtos**; um aviso que dispara em 78% dos casos não
avisa nada. Com a regra de presença (⅓ dos anúncios, calibrável), acende em **3 de 36** — e
ainda pega a Rishon. O produto **não é excluído**: fica marcado, para o usuário decidir.

**Certificação.** Tabela categoria → órgão (Inmetro, Anvisa, Anatel) em `config/`. É **indício
para perguntar ao despachante, nunca parecer**: dentro de "Brinquedos" há item que precisa de
selo e item que não precisa. Importa porque sem certificação o anúncio é pausado e a carga
retida — **prejuízo total, não margem menor**, e nenhum outro número do sistema vê esse risco.

**Embalagem** entra no custo de venda como valor FIXO. Os mesmos R$ 2 de caixa são 10% de um
produto de R$ 20 e 0,7% de um de R$ 300 — é em produto barato que ela decide.

**Quebra** encarece cada unidade VENDIDA, e a conta é `custo ÷ (1 − quebra)`, não
`custo × (1 + quebra)`. Se 5% quebram, de 100 compradas saem 95 vendas: cada venda carrega o
custo de 100/95 unidades. Multiplicar subestimaria a perda — mesma lógica do ICMS por dentro.

**Kit.** A tarifa fixa do ML é por VENDA. Num item de R$ 20 ela come um terço do preço
avulso; num kit de cinco, é cobrada uma vez só. O card mostra a margem do kit ao lado da
avulsa. **Um kit maior nem sempre é melhor**: cruzar o limiar de frete grátis pode piorá-lo,
e por isso o custo de venda é recalculado no preço do kit, nunca reaproveitado.

Embalagem, quebra e prazos são **provisórios**: só se calibram com a taxa real de devolução e
o custo real da caixa.

## 7.1 O eixo do tempo — "dá tempo de importar?"

**Você não vende hoje. Vende quando a mercadoria chega.** Todo o resto do modelo mede o
mercado de HOJE; um produto em plena aceleração agora pode estar morto quando a caixa
desembarcar em 60 dias. Comprar isso é o erro mais caro que um importador comete, e nenhum
número das seções anteriores o detecta.

O `RunwayCalculator` lê a série de visitas e responde quanto de janela ainda resta.

**É faixa, nunca ponto.** Com 19 dias de série não dá para ajustar uma logística e dizer
"faltam 7,3 semanas" — seria falsa precisão (I6). O que dá para fazer com honestidade é
classificar a FASE pelo formato da curva e associar a ela uma faixa calibrável:

| Fase | Como se reconhece | Faixa padrão |
|---|---|---|
| Acelerando | sobe e a TAXA se sustenta | 6–16 semanas |
| Estável | curva plana dentro da tolerância | 3–8 |
| Perdendo velocidade | sobe, mas a taxa caiu — o pico passou | 1–4 |
| Caindo | segunda metade abaixo da primeira | 0–2 |
| **Desconhecida** | **série < 3 semanas e subindo** | **—** |

Regras que não são detalhe:

- **Taxa constante conta como aceleração.** Uma curva que dobra toda semana cresce a 100%
  sempre, então a diferença entre as taxas é exatamente ZERO — e ela é o exemplo canônico
  da fase exponencial. Tratar esse zero como "ritmo constante" classificaria o melhor caso
  como morno
- **Subindo sem três semanas de série é `Desconhecida`, não "estável".** Um produto que
  subiu +917% pode estar explodindo ou já ter passado do pico, e é essa diferença que decide
  importar. Inventar uma fase daria um piso de runway sem lastro
- **O portão usa o LIMITE INFERIOR**, nunca a média nem o teto. Se a estimativa errar, que
  erre para o lado de não comprar: estoque encalhado custa mais que oportunidade perdida
- **Exige prazo + janela mínima de venda** (padrão 30 dias). Chegar no último dia da janela
  é estoque parado, não negócio
- **"Não sabemos" nunca vira "não dá"**: sem estimativa, o veredito é `null` (I6)

Canais de fornecimento ficam em `config/configuracoes.json`, cada um com o seu prazo:
revenda nacional (5 dias, sem tributação de importação), aérea/Remessa Conforme (25) e
marítima/formal (60). **Prazos e faixas de runway são provisórios** — só se calibram depois
de ver produtos saturarem de verdade.

Custo: **zero chamada nova** à API. É aritmética sobre a série que o ciclo já coleta.

### 9.0 Quem está trabalhando

A empresa tem **uma conta só**. Antes do painel, a tela pergunta "Estou trabalhando com
quem?" e a pessoa escolhe o próprio nome numa lista — sem senha individual.

**Isto é identificação por boa-fé, não autenticação.** O cabeçalho `X-Operador-Id` não prova
nada: quem alcançar a API pode declarar qualquer operador. Serve para a auditoria responder
"quem mudou isto?", nunca para decidir quem pode o quê. **Quem guarda a porta continua sendo
a chave de administrador** (`AdminOnly`), e as duas coisas se somam: escrita sem a chave dá
401, escrita sem operador dá **400** — código de pedido malformado, porque falta dizer quem
está trabalhando, não falta credencial.

| Onde | O quê |
|---|---|
| `operators` | id, nome, nome normalizado (único), criado em, ativo |
| `work_sessions` | uma linha por escolha na tela — reconstrói quem estava no sistema quando |
| `suppliers` | `created_by_operator_id`, `updated_by_operator_id` |

Regras que não são detalhe:

- **Duas grafias do mesmo nome são a mesma pessoa.** "José", "JOSE" e " jose " normalizam
  para a mesma chave, e o índice único é sobre ela. Se virassem três operadores, a auditoria
  se partiria em três e nenhuma contaria a história inteira
- **Nome repetido reaproveita o operador existente** em vez de recusar; se estiver inativo,
  reativa — o histórico dele continua valendo
- **O operador ativo vive em `sessionStorage`**: fechar o navegador exige escolher de novo,
  para ninguém herdar o nome de quem usou a máquina antes. O **último usado** vai em
  `localStorage`, só para destacar o botão — destacar não é entrar
- **Nada de dado sem dono**: a escrita só acontece assinada, e a tela de seleção garante que
  isso não aconteça no uso normal

### 9.05 Barra de pesquisa

Na área "Explorar". Duas fontes, com **custos muito diferentes**, e a tela deixa isso visível:

| Fonte | Custo | Quando roda |
|---|---|---|
| **Histórico** (o que já coletamos) | zero chamada, é SQL nosso | a cada tecla, com 300 ms de espera |
| **Catálogo** (Mercado Livre) | 1 chamada da conta | só no botão, nunca automático |
| **Analisar um produto novo** | ~23 chamadas | só no botão, e o custo vem escrito ao lado |

Medido em 26/09/2026, com token real:

- `/products/search?site_id=MLB&q=` — **200**. Busca por texto funciona. Isto é novo em relação
  ao §3, e derruba a necessidade do aviso "só link/ID" que a spec previa
- `/sites/MLB/domain_discovery/search?q=` — **200**
- `/items/{id}` — **403**; `/items/bulk` — **200 no envelope, 403 dentro de cada item**

**Colar link ou ID de anúncio não funciona, e não há contorno.** Buscar o próprio ID do
anúncio no catálogo devolve zero. A barra reconhece o gesto e explica que é preciso colar o
**título** — devolver "nada encontrado" seria mentira.

Outras duas coisas que só a medição mostrou:

- **Produto inativo é beco sem saída**: `/products/{id}/items` dá 404 nele e no filho dele.
  Consulta genérica ("mouse gamer") voltou **dez inativos de dez**; consulta específica volta
  ativo. Por isso a busca FILTRA inativo, em vez de só avisar
- **404 em `/products/{id}/items` de produto ativo** significa "ninguém está vendendo isto
  agora" — resposta legítima, não erro

Regras de arquitetura:

- **A API nunca renova o token.** O semáforo que serializa a renovação vive DENTRO de um
  processo; API e Worker renovando juntos invalidariam o refresh token de uso único e
  derrubariam a coleta. A busca de catálogo usa um cliente que **lê** o token guardado e, se
  estiver vencendo, avisa que o Worker precisa estar rodando
- **A análise sob demanda roda no Worker**, enfileirada pelo Hangfire, como a coleta
- **Análise com menos de 6 horas é reaproveitada** sem gastar chamada
- **O ciclo sob demanda tem status próprio** (`on-demand`). O quadro mostra "o último ciclo
  CONCLUÍDO"; sem isso, uma busca avulsa viraria um quadro de um produto só
- **Primeira medição não tem base de comparação**: entrantes e variação semanal de preço
  ficam nulos, e dois dos cinco fatores do índice entram neutros. O número NÃO é comparável
  ao dos produtos do ciclo até a segunda coleta
- Escolher um resultado **filtra a lista por aquele produto** (`produto=MLB...` na URL), em vez
  de abrir uma tela nova: mesmo card, mesma lógica, e o link continua compartilhável

### 9.1 Explorar oportunidades

Área separada do quadro, em `/explorar`. O quadro responde "o que saiu do último
ciclo"; esta responde "me mostre os produtos que são assim". O card é o mesmo objeto,
campo por campo: o que muda é QUAIS produtos aparecem, nunca o que um produto mostra.

Endpoints:

| Rota | O que devolve |
|---|---|
| `GET /api/oportunidades/explorar` | Página de cards + total do filtro + o filtro como o servidor o entendeu |
| `GET /api/oportunidades/explorar/filtros-meta` | Categorias que têm produto, faixa real de preço, rótulos das opções |

Universo: a **última análise de cada produto** (`id = max(id)` por `product_id`). A
pergunta da tela é "como está hoje", não "como esteve em algum ciclo".

Filtros: categoria, faixa de preço, faixa do índice, crescimento de visitas,
concorrência, entrantes desde a coleta anterior, condição do anúncio, frete grátis,
confiança do preço e fornecedor cadastrado. **E entre filtros diferentes, OU entre as
opções do mesmo filtro.**

Regras que não são detalhe de implementação:

- **Métrica desconhecida fica fora do filtro daquela métrica.** Produto sem preço medido
  não é barato nem caro; produto sem coleta anterior não tem "nenhum entrante". Nulo é
  "não medimos", nunca zero (I6)
- **Condição e frete são "tem ao menos um anúncio assim"**, não "todos são assim": o card
  é o produto, e um produto costuma ter anúncios novos e usados ao mesmo tempo
- **Crescimento é piso, não faixa**: "+50%" quer dizer 50 ou mais. "Explodindo" inclui a
  demanda nova com volume — quem estreou forte não tem percentual para comparar, e é
  exatamente o caso que o filtro procura. O limiar vem da configuração (I8)
- **Nulo por último na ordenação**, e todo critério desempata pelo id: sem isso, duas
  páginas repetem ou pulam itens quando vários produtos empatam
- **Valor inválido vira 400 com TODOS os problemas listados**, não um erro por requisição
- **A URL é a fonte da verdade** da tela: o filtro sobrevive ao F5, ao botão voltar e vai
  inteiro num link compartilhado. O que o navegador guarda (último filtro + até 20
  filtros nomeados) é conveniência — armazenamento bloqueado não muda nada na tela

---

## 10. "Por que vende agora"

Fase 1: justificativa por regras, sempre com o número absoluto ao lado do percentual
(I11) — "recebeu 3.000 visitas a mais que na semana passada (+60%) na categoria Z,
concorrência cresceu N%, 34% dos vendedores (17 deles) praticam R$ 90".

Fase 2 (Claude API com web search): só para o **top-N por índice** (padrão 5), sob
demanda ou ao fim do ciclo, nunca para todos os finalistas. É a maior fonte de custo
recorrente do sistema; deixe a fase 1 preparada para ela e o gasto desligável por
configuração.

---

## 11. Testes obrigatórios

`PriceReferenceCalculator`

- Arredondamento mais-próximo: 19,90 / 19,99 / 20,00 no balde R$ 20; 23,40 → 23;
  20,50 → 21; 20,49 → 20
- Fronteira: 199,50 → 200; acima de R$ 200, 347 → 345 e 349 → 350
- Corte de 15% com 7, 10 e 20 vendedores; 3 a 6 sem corte; menos de 3 sem preço
- Vendedor com vários anúncios: só o mais barato sobrevive
- Kit convertido para unidade
- Moda forte e central usa moda; moda fraca usa mediana; moda fora da faixa limpa usa
  mediana
- **Balde modal com 2 vendedores em amostra de 7**: passa nos 20% e mesmo assim cai na
  mediana, por não atingir o mínimo absoluto de 3
- Empate resolvido pela proximidade da mediana e depois pelo mais baixo
- Todos os vendedores no mesmo preço
- Amostra sem âncora de produto: status, não preço
- **Golden test**: um vetor fixo de preços com todas as saídas conferidas à mão

`MarginCalculator`

- Abaixo e acima do limiar de frete grátis; modos amostra e lote; margem negativa
- Três pontos de preço (piso/esperada/teto) coerentes entre si
- **Item que cruza o limiar de frete grátis dentro da própria faixa** (piso R$ 76,
  preço de mercado R$ 85): a margem do piso tem que usar a estrutura de custo de abaixo
  do limiar, não a do preço de mercado. É o teste que pega o reaproveitamento indevido
  de `custo_venda`
- ICMS por dentro conferido contra cálculo manual
- Câmbio efetivo com spread aplicado; PTAX de fim de semana caindo no último dia útil
- Preço-teto de compra: comprando exatamente nele, a margem bate a margem-alvo
- **Teto sem fornecedor**: o mesmo número sai sem custo unitário nenhum na entrada, e bate
  com o do caminho que tem fornecedor
- **Um centavo acima do teto já fura o alvo** — é o que faz o número valer alguma coisa
- O teto diz **onde** aperta, e no caso de referência isso é o limiar de frete, não o piso
- Teto negativo é marcado como inviável, e o card mostra aviso em vez de dinheiro negativo

Marca, certificação, embalagem e quebra (§6.2)

- Marca ocupando ⅓ ou mais dos anúncios levanta o selo; **um anúncio só não levanta**, mesmo
  barata — o selo mede presença, não preço
- Marca cara dominando a prateleira **continua** sendo domínio
- O produto marcado **não some** do quadro, e o preço de mercado não muda por causa da marca
- Quebra de 5% sobre custo 10 dá **10,5263** (÷ 0,95), e isso é MAIOR que × 1,05
- Quebra de 100% é recusada: não sobra unidade para vender
- Categoria sem regra devolve zero, e zero significa "não estimamos" (I6)
- Embalagem pesa dez vezes mais em produto de R$ 20 que em um de R$ 300
- Kit dilui a tarifa fixa e melhora a margem; **kit que cruza o limiar de frete piora**

Eixo do tempo (§7.1)

- Série curta ou vazia **não vira estimativa**: fase `Desconhecida`, faixa nula, e o portão
  devolve `null` — nunca `false`
- Curva que dobra toda semana é **aceleração**, apesar de a diferença entre taxas dar zero
- Curva que sobe com a taxa caindo é **perda de velocidade** — o caso que o card de hoje
  mostraria como "crescendo" e que chegaria tarde
- Subindo com duas semanas de série é **Desconhecida**; curva PLANA com duas semanas ainda é
  classificável, porque aí não há dúvida de forma
- **O portão usa o piso**: um prazo que caberia no teto da faixa e não no piso é RECUSADO
- A faixa nunca vira ponto: toda fase estimável devolve os dois lados

Operador (§9.0)

- "José", "JOSE" e " josé " devolvem **o mesmo operador**, nunca três
- Nome vazio, longo demais ou com caractere que não é de nome é recusado com explicação
- Escrita sem `X-Operador-Id` dá **400**; com operador inexistente ou inativo, também
- **A chave de admin continua barrando**: sem ela é 401 mesmo assinando com operador válido
- Fornecedor cadastrado guarda quem cadastrou e quem mexeu por último
- Escolher na tela registra a sessão de trabalho ANTES de entrar
- Falha ao carregar a lista não trava a tela: ainda dá para cadastrar um nome

Exibição do crescimento (§9)

- **Base zero**: a frase diz a data da estreia e o volume, e **não contém "%"**. O número
  da divisão por zero (1.998.900) não pode aparecer em lugar nenhum
- Base zero **sem série**: sem data para citar, ainda assim sem percentual
- **Base 1 a 49**: absoluto mais o motivo da ausência; "+900%" não aparece
- **Base normal**: o percentual continua lá — a correção não pode ter apagado a maioria
- A frase e o bloco de visitas do card mostram o mesmo percentual
- Análise gravada antes do campo `growth` não quebra o card nem inventa percentual
- Nenhum `double` no caminho do cálculo

Demanda

- 2 → 20 visitas (delta abaixo do piso) recebe `demanda_baixa` e fica atrás de
  5.000 → 8.000 (delta 3.000) na ordenação, apesar da taxa muito maior
- Volume alto e estagnado não vira crescimento: `g_demanda` baixo mesmo com `v_norm` alto
- **Série com dias faltando**: a API devolve 4 dos 7 dias; os 3 ausentes entram como zero
  e o `delta_abs` bate com o cálculo manual
- **Dia corrente parcial**: a janela exclui hoje. Rodar o mesmo cálculo de manhã e à
  noite tem que dar o mesmo `delta_abs`
- Produto sem série de visitas cai no proxy por posição e recebe o selo correspondente

Índice de oportunidade

- Todo fator clampado; `pressao_preco > 1` não produz índice negativo
- `f_margem = 0` quando a margem é negativa → índice 0
- Dois ciclos com coortes diferentes produzem índices comparáveis (a normalização
  saturante não pode depender da coorte)

Explorar oportunidades (§9.1)

- Produto com duas análises aparece **uma vez**, pela mais recente: a análise antiga não
  pode fazê-lo entrar numa faixa em que ele já não está
- Produto sem preço medido fica fora de "até R$ 100" **e** de "a partir de R$ 100"
- Produto sem coleta anterior fica fora das três faixas de entrantes, inclusive "nenhum"
- Opções do mesmo filtro somam (OU); filtros diferentes se cortam (E)
- Ordenação por preço e por crescimento deixa o não medido no fim
- Três páginas seguidas não repetem nem pulam item
- Filtro vazio ainda é uma página: total 0, `totalPages` 1, sem divisão por zero
- Query inválida devolve **todos** os problemas numa resposta só
- **Regressão**: o card devolvido pela busca é idêntico ao de `/api/products/{id}`, e o
  quadro mantém os mesmos campos de sempre
- Front: ida e volta pela URL preserva o filtro; valor desconhecido é descartado sem
  quebrar; `localStorage` bloqueado, cheio ou com JSON inválido não afeta a tela

---

## 12. Como quero trabalhar

1. Diagnóstico de endpoints (§3) — as respostas dele definem o que é possível
2. Modelagem do banco e migrations EF Core
3. Coletores
4. `PriceReferenceCalculator` e `MarginCalculator`, com seus testes
5. Fornecedores
6. Índice e quadrantes
7. Front

Um módulo por vez, explicando as decisões e as limitações que você encontrou. Se algum
dado não existir na API, me avise e proponha alternativas **antes** de recorrer a
scraping. Se uma premissa minha estiver errada, diga — prefiro corrigir a premissa a
receber um contorno silencioso.
