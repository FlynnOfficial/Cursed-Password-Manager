# Segurança e modelo de ameaças

## Objetivo e fronteiras

Proteger os arquivos em repouso contra leitura e alteração sem a senha mestra; evitar corrupção silenciosa, gravações perdidas e recuperação baseada em dados não autenticados. Não é uma promessa de segurança absoluta.

Atacantes considerados: alguém que copia um cofre; alguém que fornece um backup malformado; processos cooperantes concorrentes; falhas de permissão, disco ou interrupções entre etapas. AES-GCM impede a aceitação de alterações no envelope e conteúdo sem a chave.

Fora das garantias: administrador hostil, kernel comprometido, injeção no processo, keylogger, leitura da memória do usuário, malware que aguarda o desbloqueio, captura de tela, ransomware que apaga todas as cópias e substituição do executável. Nem um cofre cifrado nem o Windows Hello tornam um computador não confiável seguro.

## Derivação e chaves

- Argon2id v1.3, biblioteca Konscious 1.3.1. Um teste compara a implementação com o vetor do RFC 9106.
- Salt aleatório de 16 bytes. Novos cofres: 65.536 KiB, paralelismo 1, 3–8 passagens. A primeira derivação mede 3 passagens e aumenta o custo visando ~500 ms, sem reduzir os mínimos. A calibração não é um benchmark preciso sob variação de carga.
- Leitura aceita 19.456–262.144 KiB, 2–8 passagens e 1–4 vias; rejeita valores fora desses limites antes da derivação, limitando consumo adversarial.
- Chave de proteção: 32 bytes derivados. Cada gravação gera uma **nova chave de dados** de 32 bytes pelo RNG do sistema.
- AES-256-GCM: tag de 16 bytes. A chave de dados é cifrada pela chave de proteção; o JSON é cifrado pela chave de dados.
- Nonces independentes de 12 bytes vêm do RNG. A chave de dados nunca é reutilizada entre gravações. O envelope usa nonces aleatórios com a chave derivada; como em qualquer esquema probabilístico de nonces, colisão é extremamente improvável, não matematicamente impossível. Não se usam timestamps, GUIDs ou contadores reiniciados como nonce.

Os GUIDs são somente identificadores, nunca fonte de material secreto. SHA-256 é usado para comparar **arquivos já cifrados** e representar parentesco; não se grava índice de hashes de senhas.

Referências: [OWASP Password Storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html), [OWASP Cryptographic Storage](https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html), [RFC 9106](https://www.rfc-editor.org/rfc/rfc9106.html), [AesGcm .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm).

## Memória

`CryptographicOperations.ZeroMemory` limpa chaves, material intermediário, buffers UTF-8 da senha mestra, serialização e descriptografia. A chave de sessão é descartável. PasswordBox evita vincular a senha mestra a uma propriedade comum do ViewModel; os campos são limpos logo após o envio.

Isso **não** garante apagamento de strings gerenciadas, cópias internas da biblioteca, heap movido pelo GC, paginação, crash dumps ou memória de controles. O bloqueio remove referências e dados visíveis, mas não protege contra leitura privilegiada da memória. Durante uma operação em andamento, o descarte definitivo da chave é adiado até a saída da operação para não corromper o salvamento.

## Importação não confiável

Leitura limitada a 32 MiB com tamanho verificado antes da alocação; arquivo aberto sem compartilhar escrita. Comprimento declarado deve corresponder exatamente ao arquivo. Versão e parâmetros são validados antes de Argon2id. Conteúdo só é desserializado depois da autenticação GCM. JSON tem profundidade máxima 24, propriedades desconhecidas são rejeitadas e o modelo valida IDs, tamanhos, quantidades, categorias e históricos.

Não há desserialização polimórfica, execução de código, caminhos do sistema dentro do conteúdo importado, extração de ZIP nem acesso à rede. Falhas na preparação não alteram o modelo confirmado. Divergências nunca são sobrescritas apenas pelo nome de um serviço.

## Windows e permissões

No modo normal, a pasta de dados e a recuperação interna recebem ACL restrita ao usuário atual e SYSTEM. Administradores ainda podem tomar posse. As pastas externas seguem as permissões escolhidas pelo usuário; em FAT/exFAT pode não haver ACL. A cifra continua obrigatória em todos os casos.

O portátil não aplica uma ACL vinculada ao SID original, não usa HKCU Run e não cria configuração no perfil anfitrião. Isso permite levar os arquivos a outro computador mediante senha mestra. Dispositivos removíveis continuam sujeitos a acesso físico e cópias clandestinas.

## Clipboard e sessões

Cópia exige ação explícita. Formatos de exclusão de histórico/cloud são adicionados. A limpeza confere sequência e digest em memória, com o clipboard aberto exclusivamente durante a comparação e remoção. Se outro programa o substituiu, nada é apagado. Se o clipboard estiver ocupado, o temporizador tenta novamente. Encerramento forçado pode impedir a limpeza.

Texto com NUL embutido é recusado antes da cópia, porque o formato nativo Unicode termina nesse caractere; aceitar o texto causaria truncamento e impediria a comparação para limpeza.

Atividade é input efetivo na janela ou diálogos do aplicativo. Minimizar ou estar em segundo plano não reinicia o temporizador. Bloqueio Windows e suspensão/retomada invalidam a sessão. Strings anteriormente capturadas por outros programas não podem ser revogadas.

## Revisão realizada e limites

Foram inspecionados uso de nonces/AAD, derivação, rotação, importação, falhas parciais, locks, perda de configurações, clipboard, bloqueio durante operações e isolamento portátil. Foram adicionados testes de corrupção, divergência, interrupção e limites.

Uma consulta ao feed NuGet não apontou vulnerabilidades conhecidas nas dependências no momento da validação. Isso não demonstra ausência de falhas nem manutenção futura. A biblioteca Argon2 deve ser acompanhada e reavaliada; a versão usada é de 2024. Uma auditoria independente continua recomendada antes de uso com dados críticos.
