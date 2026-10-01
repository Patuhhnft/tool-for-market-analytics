<#
    Sobe o Leitor do Mercado Livre: banco, Worker, API e painel, cada um em sua janela.
    Fechar a janela para o serviço. O banco continua no Docker.

        .\Iniciar.ps1              sobe tudo
        .\Iniciar.ps1 -Autorizar   refaz a autorização e guarda um refresh token novo
        .\Iniciar.ps1 -Forcar      encerra uma API antiga sem perguntar

    Este arquivo é UTF-8 COM BOM de propósito: sem o BOM, o Windows PowerShell 5.1 lê como
    ANSI e os acentos viram lixo.
#>

# Write-Host é o certo aqui: o script existe para conversar com quem está olhando o console,
# em cores. A regra do analisador mira módulos reutilizáveis, não script interativo.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost', '',
    Justification = 'Script interativo: a saída colorida no console é a interface dele.')]
param([switch]$Autorizar, [switch]$Forcar)

$ErrorActionPreference = 'Stop'

# Sem isto, acento em janela de console legada sai como lixo.
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$raiz = $PSScriptRoot
$worker = Join-Path $raiz 'src\LeitorMercadoLivre.Worker'
$appId = '2727129198048698'   # App ID não é segredo: aparece na URL de autorização
$redirect = 'https://exemplo.com.br/oauth/callback'

function Start-Janela {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$Titulo, [string]$Pasta, [string]$Comando)

    if (-not $PSCmdlet.ShouldProcess($Titulo, 'Abrir janela')) { return }

    Start-Process powershell -ArgumentList '-NoExit', '-Command',
        "`$host.UI.RawUI.WindowTitle='$Titulo'; Set-Location '$Pasta'; $Comando" | Out-Null
}

function Read-ChaveSecreta {
    # Leitura VISÍVEL de propósito. Em prompt -AsSecureString o Ctrl+V não cola no console do
    # Windows: ele vira um caractere de controle, e a chave entra truncada sem ninguém notar.
    # Como o segredo vai para o cofre em texto puro de qualquer jeito, esconder a digitação
    # custaria mais do que protege.
    while ($true) {
        Write-Host ''
        Write-Host 'Chave secreta do app — DevCenter > seu app > Chave secreta.' -ForegroundColor Cyan
        $texto = ([string](Read-Host 'Cole aqui e tecle Enter')).Trim()

        if ($texto.Length -lt 16) {
            Write-Host "  Muito curta ($($texto.Length) caracteres). A chave do Mercado Livre tem 30 ou mais." -ForegroundColor Yellow
            continue
        }

        if ($texto -notmatch '^[A-Za-z0-9]+$') {
            Write-Host '  Tem caractere estranho. Cole só a chave, sem espaços nem aspas.' -ForegroundColor Yellow
            continue
        }

        $mascara = $texto.Substring(0, 4) + ('*' * ($texto.Length - 8)) + $texto.Substring($texto.Length - 4)
        Write-Host "  Recebido: $mascara ($($texto.Length) caracteres)" -ForegroundColor Green
        return $texto
    }
}

function Save-ChaveSecreta {
    param([string]$Chave)

    dotnet user-secrets set 'MercadoLivre:ClientSecret' $Chave --project $worker | Out-Null
    Write-Host 'Chave secreta guardada no cofre do .NET (fora do repositório).' -ForegroundColor Green
}

# --- Reautorização: use quando o Worker disser que a renovação foi recusada ----------
if ($Autorizar) {
    Start-Process ("https://auth.mercadolivre.com.br/authorization?response_type=code" +
        "&client_id=$appId&redirect_uri=$([uri]::EscapeDataString($redirect))")

    Write-Host 'Autorize no navegador. A página de destino vai dar erro: é o esperado.'
    $url = Read-Host 'Cole aqui a URL inteira da barra de endereços'

    $code = [regex]::Match($url, 'code=([^&]+)').Groups[1].Value
    if (-not $code) { throw 'Não achei code= na URL colada.' }

    $chave = Read-ChaveSecreta
    $token = Invoke-RestMethod -Method Post -Uri 'https://api.mercadolibre.com/oauth/token' `
        -ContentType 'application/x-www-form-urlencoded' `
        -Body @{ grant_type = 'authorization_code'; client_id = $appId; client_secret = $chave
                 code = $code; redirect_uri = $redirect }

    Save-ChaveSecreta -Chave $chave
    dotnet user-secrets set 'MercadoLivre:BootstrapRefreshToken' $token.refresh_token --project $worker | Out-Null

    Write-Host 'Refresh token novo guardado. Rode .\Iniciar.ps1 para subir.' -ForegroundColor Green
    exit 0
}

# --- 1. Banco -----------------------------------------------------------------------
Write-Host "`n  Leitor do Mercado Livre`n" -ForegroundColor Cyan
Write-Host '[1/5] Banco'

# O daemon está no ar?
#
# O 'docker info' escreve no stderr quando o Docker está parado. Com $ErrorActionPreference
# em 'Stop', o PowerShell 5.1 transforma esse stderr num erro TERMINANTE e mata o script
# aqui -- numa verificação de rotina, cujo trabalho é justamente descobrir que está parado.
# Por isso a preferência volta a 'Continue' dentro da função: o que interessa é o código de
# saída, não o texto que o docker imprimiu.
function Test-Docker {
    $ErrorActionPreference = 'Continue'
    docker info 2>$null 1>$null
    return $LASTEXITCODE -eq 0
}

$dockerDesktop = 'C:\Program Files\Docker\Docker\Docker Desktop.exe'

if (-not (Test-Docker)) {
    if (-not (Test-Path $dockerDesktop)) {
        throw "O Docker não está rodando e não encontrei o Docker Desktop em '$dockerDesktop'. Abra-o à mão e rode de novo."
    }

    Write-Host '  Docker parado. Abrindo o Docker Desktop (pode levar ~1 minuto)...' -ForegroundColor Yellow
    Start-Process $dockerDesktop | Out-Null

    $subiu = $false
    foreach ($tentativa in 1..90) {
        Start-Sleep -Seconds 2
        if (Test-Docker) { $subiu = $true; break }
        if ($tentativa % 10 -eq 0) { Write-Host "  ainda subindo... ($($tentativa * 2)s)" -ForegroundColor DarkGray }
    }

    if (-not $subiu) {
        throw 'O Docker Desktop não subiu em 3 minutos. Abra-o à mão, espere ficar verde e rode de novo.'
    }

    Write-Host '  Docker no ar.' -ForegroundColor Green
}

Push-Location $raiz
docker compose up -d | Out-Null
Pop-Location

function Test-Porta {
    param([int]$Porta)

    Test-NetConnection localhost -Port $Porta -InformationLevel Quiet -WarningAction SilentlyContinue
}

# Porta 5078 ocupada: quem está lá?
#
# A mensagem antiga mandava "fechar a janela da API antiga" — mas a API pode ter sido subida
# sem janela nenhuma (por um script, por uma sessão de terminal já fechada), e aí o usuário
# ficava sem saída: um erro que pede para fechar algo que não existe.
#
# Agora o script identifica o processo. Se for a NOSSA API, ele se oferece para encerrá-la,
# porque é exatamente o que o usuário faria. Se for outra coisa, avisa o que é — matar um
# processo alheio sem perguntar seria pior que o problema.
if (Test-Porta -Porta 5078) {
    $dono = Get-NetTCPConnection -LocalPort 5078 -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty OwningProcess
    $processo = if ($dono) { Get-Process -Id $dono -ErrorAction SilentlyContinue } else { $null }

    if ($processo -and $processo.ProcessName -eq 'LeitorMercadoLivre.Api') {
        Write-Host "  A API já está rodando (PID $($processo.Id)), provavelmente de uma execução anterior." -ForegroundColor Yellow

        # Sem console interativo, Read-Host leria EOF e o script travaria ou seguiria com uma
        # resposta vazia. Nesse caso o padrão é encerrar: quem chamou sem terminal quer subir.
        $interativo = -not [Console]::IsInputRedirected
        $resposta = if ($Forcar -or -not $interativo) { 's' } else { Read-Host '  Encerrar e subir de novo? [S/n]' }

        if ($resposta -eq '' -or $resposta -match '^[sSyY]') {
            $processo | Stop-Process -Force
            # O Windows leva um instante para soltar a porta depois que o processo morre.
            foreach ($tentativa in 1..20) {
                Start-Sleep -Milliseconds 500
                if (-not (Test-Porta -Porta 5078)) { break }
            }
            Write-Host '  API anterior encerrada.' -ForegroundColor Green
        } else {
            throw 'A API anterior continua rodando na porta 5078. Encerre-a e rode de novo.'
        }
    } elseif ($processo) {
        throw "A porta 5078 está ocupada por '$($processo.ProcessName)' (PID $($processo.Id)), que não é deste projeto. Encerre-o ou mude a porta da API."
    } else {
        throw 'A porta 5078 está ocupada e não consegui identificar por quem. Rode: Get-NetTCPConnection -LocalPort 5078'
    }
}

$estado = ''
foreach ($tentativa in 1..40) {
    $estado = docker inspect -f '{{.State.Health.Status}}' leitor-postgres 2>$null
    if ($estado -eq 'healthy') { break }
    Start-Sleep -Seconds 2
}
if ($estado -ne 'healthy') { throw "Postgres não ficou saudável (estado: $estado)." }
Write-Host '  Postgres no ar.' -ForegroundColor Green

# --- 2. Compilação ------------------------------------------------------------------
# Uma vez só, aqui. Se cada janela rodasse "dotnet run" por conta, as duas compilariam os
# mesmos projetos ao mesmo tempo e travariam o mesmo arquivo (CS2012).
Write-Host '[2/5] Compilando'
dotnet build (Join-Path $raiz 'LeitorMercadoLivre.slnx') --nologo --verbosity quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou. Rode: dotnet build LeitorMercadoLivre.slnx' }
Write-Host '  Compilado.' -ForegroundColor Green

# --- 3. Worker (coleta ao subir e a cada 6h) ----------------------------------------
Write-Host '[3/5] Worker'

$segredos = dotnet user-secrets list --project $worker 2>$null
if ($segredos -notmatch 'MercadoLivre:ClientSecret') {
    Write-Host '  Chave secreta ainda não guardada.' -ForegroundColor Yellow
    Save-ChaveSecreta -Chave (Read-ChaveSecreta)
}

Start-Janela -Titulo 'Leitor - Worker' -Pasta $raiz -Comando 'dotnet run --no-build --project src/LeitorMercadoLivre.Worker'

# --- 4. API -------------------------------------------------------------------------
Write-Host '[4/5] API'
Start-Janela -Titulo 'Leitor - API' -Pasta $raiz -Comando 'dotnet run --no-build --project src/LeitorMercadoLivre.Api --launch-profile http'

# --- 5. Painel ----------------------------------------------------------------------
Write-Host '[5/5] Painel'
$painel = Join-Path $raiz 'frontend'

if (-not (Test-Path (Join-Path $painel 'node_modules'))) {
    Write-Host '  Instalando dependências (só na primeira vez)...' -ForegroundColor Yellow
    Push-Location $painel
    npm install | Out-Null
    Pop-Location
}

Start-Janela -Titulo 'Leitor - Painel' -Pasta $painel -Comando 'npm run dev'

foreach ($tentativa in 1..60) {
    if (Test-Porta -Porta 5173) { break }
    Start-Sleep -Seconds 1
}
# Duas abas: o painel e a fila de coletas. O Hangfire mostra se o ciclo rodou, falhou ou
# esta na fila -- sem ele, um ciclo travado so aparece como card velho na tela.
Start-Process 'http://localhost:5173'
if (Test-Porta -Porta 5078) {
    Start-Sleep -Milliseconds 700
    Start-Process 'http://localhost:5078/hangfire'
}

Write-Host "`n  Painel:   http://localhost:5173" -ForegroundColor Cyan
Write-Host '  Hangfire: http://localhost:5078/hangfire' -ForegroundColor Cyan
Write-Host "  Para parar: feche as janelas.`n" -ForegroundColor DarkGray
