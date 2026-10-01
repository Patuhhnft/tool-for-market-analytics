@echo off
rem Duplo clique para subir o Leitor do Mercado Livre.
rem O -ExecutionPolicy Bypass vale so para esta execucao: o Windows bloqueia .ps1 no duplo
rem clique por padrao, e mudar a politica da maquina inteira seria um exagero.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Iniciar.ps1" %*
if errorlevel 1 pause
