# Build e distribuição

Requer Windows x64 e SDK .NET 10.0.402, fixado em `global.json` para reproduzir também as dependências de publicação. O runtime alvo é .NET 10; a aplicação não depende do SDK na máquina do usuário. Ao atualizar o SDK, revise e regenere os três perfis de lock (`packages.lock.json`, `packages.singlefile.lock.json` e `packages.portable.lock.json`) antes de voltar a usar o modo bloqueado.

`build-release.bat` executa `tools/Build-Release.ps1`. O script usa diretórios `.build/dotnet` e `.build/packages`, desativa telemetria da CLI, restaura dependências fixadas, compila Release com avisos tratados como erros, roda xUnit e o processo WPF de integração, publica normal e portátil, inclui documentação e gera ZIPs e hashes.

O teste WPF usa uma raiz exclusiva por execução. Não interaja com a janela enquanto ele mede inatividade. Em CI sem desktop interativo, rode a suíte Core/Security/Storage e sinalize que a validação WPF ficou pendente; não remova silenciosamente essa etapa do script de release.

O build também roda `--extended`: registro HKCU com redirecionamento local ao processo, ACL apenas em pastas fictícias, encerramento de processos de teste e realocação portátil. Execute sob um usuário que possa criar chaves temporárias em HKCU e alterar a ACL das pastas de teste. A suíte não configura o logon real nem altera ACL de pastas pessoais.

Testes assistidos opcionais, a partir da raiz do repositório:

```powershell
& tests/CursedPasswordManager.WindowsTests/bin/Release/net10.0-windows/CursedPasswordManager.WindowsTests.exe --interactive .build/manual-session
```

Aguarde `READY_LOCK`, bloqueie/desbloqueie o Windows; após `READY_SUSPEND`, suspenda/retome. O cofre usa somente dados fictícios. Caso suspensão não exista, preserve o log parcial sem declarar aprovação integral.

O modo `--worker=clipboard` tenta uma estação isolada; falha se o Windows não permitir. Somente após autorização para substituir/limpar o conteúdo copiado, use `--worker=clipboard-session .build/manual-clipboard`. Não copie nada durante os aproximadamente 15 segundos do teste. Esse modo usa a sessão real e nunca é chamado pelo build automático.

## Saídas

- `dist/windows-x64/CursedPasswordManager.exe`: autocontido, single-file, win-x64. Bibliotecas nativas do bundle podem ser extraídas pelo runtime; isso não contém dados do cofre.
- `dist/portable-x64/CursedPasswordManager.exe`: autocontido com DLLs e `portable.flag`. Preserve o diretório inteiro.
- Dois ZIPs na pasta `dist`.
- Resultados TRX em `.build/TestResults` e evidências WPF em `.build/windows-validation-*`.

Após publicar, `tools/Verify-Packages.ps1` inicia cópias dos dois executáveis em pastas fictícias com `--portable`, confere presença da janela e raiz de dados, encerra somente esses processos e verifica ZIPs, documentação atualizada e hashes. O relatório fica em `.build/final-package-verification.json`.

`dist`, `bin`, `obj`, `.build`, cofres, backups e configurações locais são ignorados pelo Git. Não há `LICENSE` do projeto por decisão do mantenedor; ele será adicionado no GitHub. Os avisos de terceiros ficam em `docs/third-party` e os avisos do runtime são incluídos na distribuição.

## Publicação manual

```powershell
dotnet publish src/CursedPasswordManager.App -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.singlefile.lock.json -p:RestoreLockedMode=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/windows-x64
dotnet publish src/CursedPasswordManager.App -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.portable.lock.json -p:RestoreLockedMode=true -p:PublishSingleFile=false -o dist/portable-x64
```

Adicione `portable.flag` junto ao executável da segunda saída, ou passe `--portable`. Publicação manual não substitui os testes do script completo. Os executáveis não têm assinatura de um certificado de distribuição; uma assinatura futura deve usar uma chave fora do repositório e nunca uma credencial embutida no build.

## Ícone

`tools/Create-Icon.ps1` gera PNG e ICO multirresolução (16, 24, 32, 48, 64, 128 e 256 pixels) usando desenho vetorial procedural próprio. `assets/cursed.svg` oferece uma versão vetorial do mesmo conceito. A geração não usa assets do Windows nem serviço remoto.
