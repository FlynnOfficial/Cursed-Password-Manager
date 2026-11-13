@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-Release.ps1"
if errorlevel 1 (
  echo.
  echo FALHA: consulte as mensagens acima. Nenhuma publicacao foi confirmada.
  exit /b 1
)
echo.
echo Distribuicoes prontas em dist\windows-x64 e dist\portable-x64.
exit /b 0
