# Persistência, replicação e recuperação

## Estado confirmado

A recuperação interna é o ponto de confirmação obrigatório. Uma alteração é considerada persistida quando a cópia interna foi escrita e substituída após a verificação do temporário. Sucesso total exige também as demais réplicas habilitadas. O resultado identifica caminho, estado e horário da sincronização bem-sucedida durante a sessão atual.

O horário da última sincronização não é guardado num índice permanente. Após reiniciar, o estado é novamente verificado; não se presume integridade com base na data do arquivo.

## Protocolo

1. Preparar um único array de bytes cifrados e autenticar seu conteúdo.
2. Obter locks exclusivos `.lock` em ordem de caminho. Uma cópia indisponível é registrada como falha; a ausência de lock da recuperação interna impede qualquer confirmação.
3. Comparar SHA-256 dos arquivos acessíveis com os hashes observados na abertura/última gravação. Alterações concorrentes desconhecidas interrompem a operação antes de substituir arquivos. Arquivos ausentes podem ser recriados.
4. Preservar a cópia interna anterior em snapshot identificado por hash.
5. Para cada destino, criar temporário aleatório no mesmo diretório, escrever assincronamente, descarregar buffers (`Flush(true)`), reler e comparar bytes.
6. Usar `File.Replace` com `.prev` se o destino existe; `File.Move` sem sobrescrita se não existe. As garantias finais dependem do sistema de arquivos e hardware.
7. Atualizar o hash observado somente para destinos confirmados.
8. Limitar a cinco snapshots anteriores; se a limpeza falhar, informar o estado e manter os arquivos.

O temporário só contém ciphertext. Uma interrupção do processo pode deixar temporários órfãos; o aplicativo não os assume como versões válidas nem os usa silenciosamente. Não há atomicidade entre discos, e `Flush` não corrige hardware que informa persistência sem cumpri-la.

## Concorrência

Um mutex impede duas janelas para a mesma raiz de configuração na sessão Windows. Locks de arquivo por destino coordenam instâncias que compartilham caminhos mesmo com configurações distintas. O controle otimista por hash detecta que outra instância gravou depois da leitura.

Isso coordena processos cooperantes. Um programa malicioso executado como o mesmo usuário pode ignorar os locks, substituir arquivos ou apagar os dados; a autenticação ainda detecta adulteração, mas não evita negação de serviço. Acesso concorrente via compartilhamentos de rede, links e aliases incomuns não é uma implantação certificada. Prefira discos locais.

## Falhas

| Momento | Resultado recuperável |
| --- | --- |
| Antes da substituição interna | Cópia corrente anterior; temporário não assumido como confirmado |
| Após interna, antes da principal | Interna nova e outras antigas; divergência apresentada na próxima abertura |
| Após principal, antes da secundária | Interna/principal novas; secundária pendente |
| Uma unidade ausente | Demais cópias continuam; status parcial |
| Principal e secundária apagadas | Recuperação interna descoberta pelo caminho estável |
| Configurações perdidas | Padrões carregados; recuperação interna não é apagada |
| Corrupção da corrente | Outra cópia, `.prev`, snapshot ou exportação autenticada |

Quando arquivos diferem, a interface não escolhe a revisão maior. O usuário seleciona um candidato, autentica e confere os registros antes de sincronizar. O destino substituído é preservado em `.prev`, e a interna também em snapshot. Esses históricos têm retenção limitada: exporte uma divergência importante antes de continuar editando.

## Migração de pastas

A alteração dos destinos exige cofre aberto. Novas pastas contendo cofre diferente são recusadas; nenhuma migração silenciosa substitui dados desconhecidos. Copia e verifica todos os destinos novos, grava as configurações e então assume os novos caminhos. Os diretórios antigos são preservados, inclusive suas permissões e cópias.

## Portátil

`portable.flag` ou `--portable` define a raiz `Data` próxima ao executável. Recuperação: `Data/Recovery`. Os padrões principal/secundária são relativos à raiz de dados. Ao escolher destinos, o programa armazena caminhos relativos quando possível; outra unidade pode exigir caminho absoluto.

Não há gravação no perfil anfitrião, registro, dependência de DPAPI nem vínculo obrigatório ao dispositivo. O pacote portátil de arquivos auxiliares evita o cache de extração do bundle .NET. Mover o diretório inteiro preserva configuração, cofre e recuperação. Três cópias no mesmo pendrive não são três dispositivos independentes.
