# Arquitetura

O produto é um processo WPF para Windows x64. A operação normal não contém clientes HTTP, resolução de serviços externos, telemetria ou servidor. A restauração NuGet é uma atividade de desenvolvimento separada.

## Responsabilidades

- **Core:** modelos serializáveis, validação de limites, pesquisa/ordenação, histórico, mesclagem, auditoria e geração de senhas. Não conhece WPF, registro ou sistema de arquivos.
- **Security:** envelope binário versionado, derivação Argon2id, AES-GCM, validação prévia de parâmetros e descarte de buffers de chaves.
- **Storage:** leitura limitada, escrita temporária/substituição, réplicas com observação de hashes, locks de arquivo, snapshots, caminhos e configurações não sensíveis.
- **App:** composição, `MainViewModel`, comandos, diálogos, temas e integrações Windows. O code-behind cuida de PasswordBox, eventos de janela, input, bandeja e eventos do sistema.

O ViewModel recebe os caminhos e as configurações no construtor. O armazenamento e a sessão são compostos ali; não há contêiner de DI nem repositórios genéricos sem necessidade. Os serviços Windows ficam fora das camadas portáveis.

## Fluxo de alteração

1. O usuário atua em um comando, com exclusão de operações concorrentes na interface.
2. A mutação ocorre sobre uma cópia do modelo; os limites são validados.
3. A cifra prepara os bytes de uma nova revisão fora da thread de apresentação.
4. O armazenamento autentica os bytes, obtém locks e verifica observações anteriores.
5. Confirma a recuperação interna e tenta publicar os mesmos bytes nos demais destinos.
6. Somente então o ViewModel assume o novo modelo. Falhas de réplicas ficam visíveis.

Uma falha antes da confirmação mantém o modelo anterior. Uma falha depois da recuperação interna gera um resultado parcial, pois a alteração já foi persistida. Não existe uma transação distribuída entre volumes.

## Sessão e bloqueio

A senha mestra existe apenas durante o pedido de derivação. A sessão mantém a chave de proteção derivada; as chaves aleatórias de dados só existem durante uma operação de cifra. A interface mantém o modelo descriptografado enquanto o cofre está aberto.

Bloquear invalida a geração de sessão, esconde/limpa listas e campos, fecha diálogos e tenta limpar o clipboard pertencente ao aplicativo. Uma operação de disco em curso não é abortada pela metade: termina ou falha, sem reabrir a interface. A chave é zerada ao terminar essa operação. Uma resposta de autenticação atrasada não pode reabrir uma sessão já bloqueada.

## Interface

Exatamente três abas compartilham os mesmos controles. Dicionários `XpClassic`, `CursedXp` e `CursedModern` definem pincéis e raios; `Controls.xaml` define templates comuns. `DynamicResource` permite a troca sem reconstruir a interface. Pré-visualizações nas configurações são miniaturas ilustrativas da paleta; as imagens do README são renderizações reais das telas de teste.

## Testabilidade

Testes xUnit exercitam as camadas independentes. O executável WindowsTests usa uma thread STA, dispatcher WPF, diálogos e comandos reais com dados fictícios. Ele registra resultados e renderiza os controles para PNG, incluindo os temas e o estado bloqueado. Não há modo especial de desbloqueio no executável distribuído.
