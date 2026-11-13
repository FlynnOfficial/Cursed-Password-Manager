# Matriz de validação

Ambiente: Windows 10 22H2 x64 (build 19045), SDK .NET 10.0.402 e runtime 10.0.13. Validação realizada em 12 de novembro de 2026. Todos os dados de teste são fictícios.

## Automatizado e executado

A suíte xUnit cobre 66 casos: geração numérica e grupos, 10/30/60 caracteres, rejeição de opções inválidas, zeros à esquerda, inclusão-exclusão, vetor Argon2id do RFC 9106, criação/desbloqueio, senha errada, adulteração de diferentes regiões, truncamento, excesso de bytes, limites do KDF, novas chaves/nonces, troca de senha mestra, descarte de sessão, vínculo do desbloqueio por dispositivo, histórico/restauração, categorias, favoritos/pesquisa, ordenação, mesclagem e auditoria.

No armazenamento: três cópias idênticas, perda de principal/secundária/ambas, restauração da interna, corrupção, interrupção antes da substituição, destino indisponível, recuperação indisponível, lock negado, escrita concorrente, revisões divergentes, retenção/restauração de snapshots, migração, destino desconhecido, configuração portátil, exportação com senha própria e rejeição de arquivo excessivo.

O processo WPF executa 29 verificações de primeira execução, criação e reabertura persistente, geração oculta, salvar via diálogo, favoritos, pesquisa instantânea, edição/histórico, auditoria, persistência e renderização de três temas, limpeza no bloqueio, recusa do Hello no portátil, integridade, bandeja/restauração e inatividade real. Também verifica que bloquear durante uma autenticação em andamento impede reabertura tardia, rejeita NUL antes de acessar o clipboard e exercita os callbacks de suspensão/retomada por injeção explícita de eventos. A janela fictícia fica oculta durante os 62 segundos de espera para impedir que entrada humana reinicie o prazo; uma execução anterior com janela ativa falhou nessa etapa e foi preservada em `.build/release-extended.log`.

A suíte Windows adicional executa 26 verificações de registro de inicialização com HKCU redirecionado apenas no processo de teste, ACL real de pasta e arquivo, negação real de escrita nas cópias interna/secundária, encerramento forçado de processos antes/depois da substituição atômica e reabertura das três cópias portáteis em outra pasta e processo. A ACL foi preparada exclusivamente na pasta fictícia com FullControl do usuário, equivalente à pasta de perfil; a pasta do checkout concede apenas Modify. As regras de negação são removidas em `finally`. Em execução separada, o cofre criado pelo usuário anfitrião foi aberto pela conta de sandbox, comprovando SID diferente e autenticação das três cópias: cinco verificações adicionais aprovadas. Isso valida leitura de configuração e descriptografia entre usuários no mesmo computador, sem certificar toda a interface em outro perfil.

Com autorização do usuário, o clipboard real foi testado: Unicode, formatos e valores de exclusão de histórico/cloud, limpeza pelo temporizador, preservação de conteúdo posterior (inclusive texto idêntico), limpeza ao descartar, disputa com outro thread e rejeição de NUL. O teste revelou que NUL embutido truncava o texto nativo e impedia a limpeza por hash. A cópia agora rejeita esse caractere antes de alterar o clipboard; a repetição passou. A estação de clipboard isolada foi recusada pelo Windows (acesso negado), por isso o modo de sessão exige autorização expressa e não roda no build automático.

O usuário bloqueou/desbloqueou a sessão: os eventos reais confirmaram limpeza das credenciais e manutenção do bloqueio após logon. A suspensão física não pôde ser executada: `powercfg /a` informa ausência de suporte do firmware a S1/S2/S3/S0; apenas hibernação e inicialização rápida disponíveis. Hibernação não foi testada. A consulta nativa `Tbsi_GetDeviceInfo` retornou `0x8028400f` (TPM não encontrado); o provedor Platform retornou `0x80090030`. Não houve tentativa de criar chaves Hello nem de automatizar PIN/biometria.

Resultados detalhados são gerados em `.build/TestResults/*.trx`, `.build/windows-validation-*` e `.build/extended-validation-*`. Evidências adicionais desta execução: `.build/interactive-validation/events.txt`, `.build/clipboard-session-2.log` e `.build/portable-other-user.log`. O monitor de energia foi encerrado após os dois testes de sessão, porque a opção Suspender não existe neste computador; não foi marcado como aprovado integralmente. O script de release falha se qualquer etapa automática falhar. Capturas aprovadas são copiadas para `assets/screenshots`.

Uma consulta pública ao feed NuGet com `dotnet list package --vulnerable --include-transitive` não identificou pacotes vulneráveis nas fontes consultadas. Isso é uma fotografia do feed, não uma auditoria independente.

## Limites da evidência

| Cenário | Estado |
| --- | --- |
| Compilação e testes de núcleo no Windows | Executados |
| Controles WPF, temas e comandos com dados fictícios | Executados em processo STA real |
| Temporizador real de bloqueio | Executado com um minuto de inatividade |
| Bandeja, minimizar e restaurar | Executados |
| Hello desabilitado no portátil | Executado |
| Hello positivo com PIN/biometria/TPM | Depende de teste interativo em hardware compatível; não certificado |
| Janela de configurações e persistência de tema | Executadas |
| HKCU Run e inicialização após logon | Registrar/remover e argumentos passaram em HKCU isolado; novo logon real pendente |
| Bloqueio/desbloqueio da sessão Windows | Eventos reais executados com participação do usuário; aprovados |
| Suspensão/retomada | Callbacks injetados testados; teste físico impedido pelo firmware, hibernação pendente |
| Portátil em outra máquina ou outro perfil | Mudança de pasta/processo e descriptografia sob SID diferente aprovadas; outra máquina e interface completa em outro perfil pendentes |
| Windows 11 | Alvo suportado pelo framework; execução específica pendente |
| Interrupção antes/depois de substituir arquivo | Processos reais encerrados à força, versões e recuperação autenticadas |
| Queda de energia/dispositivo com cache defeituoso | Não reproduzida fisicamente |
| ACL de arquivos | Proteção, herança e negação real de escrita aprovadas no usuário atual; outro usuário pendente |
| Clipboard nativo | Temporizador, posse, disputa, descarte, Unicode e NUL testados e aprovados |
| Histórico/cloud clipboard e captura por terceiros | Formatos nativos e valores verificados; interface de histórico, sincronização entre dispositivos e terceiros não certificados |
| Revisão criptográfica independente / teste de intrusão | Não realizada |

Não confunda uma falha simulada de IO com corte físico de energia. Não confunda um teste do modelo ou um controle renderizado com certificação de todas as combinações de hardware, DPI, leitor de tela e Windows.

## Roteiro de homologação adicional

1. Em Windows 10 e 11 com dados fictícios, criar cofre em discos distintos; encerrar e reabrir; desconectar/reconectar a unidade secundária e sincronizar.
2. Testar 100%, 150% e 200% de escala, teclado e leitor de tela; conferir todos os diálogos nos três temas.
3. Bloquear a sessão e suspender/retomar; garantir que o cofre e diálogos estão bloqueados.
4. Copiar senha, aguardar limpeza; copiar outro conteúdo antes do prazo e garantir preservação; verificar as políticas reais de clipboard do Windows.
5. Em TPM/Hello compatível, ativar após senha mestra, cancelar um pedido, desbloquear com PIN e biometria; confirmar recusa silenciosa após autenticação e após reinicialização. Resetar Hello em ambiente descartável e conferir fallback por senha mestra.
6. Levar o portátil a outro perfil/computador, abrir apenas com senha mestra e confirmar ausência de configuração do aplicativo no perfil anfitrião.
7. Testar pasta sem permissão, disco cheio, término abrupto durante cada etapa e recuperação de todas as divergências sem excluir fontes antes da conferência.
