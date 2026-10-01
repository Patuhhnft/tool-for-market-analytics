# Roda a suíte de testes sem esbarrar nas DLLs travadas.
#
# O problema que este script resolve: com a API ou o Worker rodando, o `dotnet test` tenta
# recompilar os projetos e o Windows recusa sobrescrever uma DLL em uso. O build falha com
# MSB3027 depois de dez tentativas, e o erro não diz o que fazer — só que o arquivo está
# bloqueado por "LeitorMercadoLivre.Api (24620)".
#
# Também sobe o Postgres, porque sem ele ~53 testes de integração são PULADOS em silêncio.
# Uma suíte verde com um terço dos testes pulados dá uma falsa sensação de segurança.

[CmdletBinding()]
param(
    # Não mexe nos serviços; útil se você já sabe que estão parados.
    [switch]$NaoPararServicos,

    # Sobe API e Worker de novo no fim.
    [switch]$Religar
)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot

# Mesma razão do Iniciar.ps1: com ErrorActionPreference em 'Stop', o stderr de um executável
# nativo vira erro terminante e mata o script numa verificação de rotina.
function Test-Docker {
    $ErrorActionPreference = 'Continue'
    docker info 2>$null 1>$null
    return $LASTEXITCODE -eq 0
}

function Stop-Servico {
    param([string]$Nome)

    $processos = Get-Process -Name $Nome -ErrorAction SilentlyContinue
    if ($processos) {
        Write-Host "  parando $Nome" -ForegroundColor DarkGray
        $processos | Stop-Process -Force
        return $true
    }
    return $false
}

Write-Host "`n  Testes do Leitor`n" -ForegroundColor Cyan

# --- 1. Serviços que travam as DLLs ---------------------------------------------------
$estavamRodando = $false
if (-not $NaoPararServicos) {
    Write-Host '[1/3] Liberando as DLLs'
    $api = Stop-Servico -Nome 'LeitorMercadoLivre.Api'
    $worker = Stop-Servico -Nome 'LeitorMercadoLivre.Worker'
    $estavamRodando = $api -or $worker

    if ($estavamRodando) {
        # O Windows leva um instante para soltar o arquivo depois que o processo morre.
        Start-Sleep -Seconds 2
    } else {
        Write-Host '  nada rodando' -ForegroundColor DarkGray
    }
}

# --- 2. Postgres ----------------------------------------------------------------------
Write-Host '[2/3] Banco'
if (-not (Test-Docker)) {
    Write-Host '  Docker parado: os testes de integração serão PULADOS.' -ForegroundColor Yellow
    Write-Host '  Abra o Docker Desktop e rode de novo para valer.' -ForegroundColor Yellow
} else {
    Push-Location $raiz
    docker compose up -d | Out-Null
    Pop-Location

    foreach ($tentativa in 1..30) {
        docker exec leitor-postgres pg_isready -U leitor 2>$null 1>$null
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Seconds 2
    }
    Write-Host '  Postgres no ar' -ForegroundColor Green
}

# --- 3. Testes ------------------------------------------------------------------------
Write-Host "[3/3] Rodando`n"
$ErrorActionPreference = 'Continue'
dotnet test (Join-Path $raiz 'LeitorMercadoLivre.slnx')
$resultado = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

if ($Religar -and $estavamRodando) {
    Write-Host "`n  Religando os serviços..." -ForegroundColor DarkGray
    & (Join-Path $raiz 'Iniciar.ps1')
}

exit $resultado
