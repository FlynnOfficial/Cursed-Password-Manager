# Formato do cofre — versão 1

Extensões: `.cpmvault` e `.cpmbackup`. Ambas usam o mesmo envelope; exportação cria ID, chave e senha de proteção independentes. Um arquivo de backup também pode ser aberto pelo fluxo explícito de recuperação.

Inteiros little-endian. GUIDs usam os 16 bytes de `Guid.ToByteArray()` no .NET (a ordem de bytes dos primeiros campos difere da representação textual). Tamanho máximo do arquivo: 32 MiB. Nenhum byte extra é permitido.

| Offset | Bytes | Conteúdo |
| ---: | ---: | --- |
| 0 | 8 | ASCII `CPMVLT01` |
| 8 | 4 | Versão int32 = 1 |
| 12 | 16 | ID do cofre |
| 28 | 8 | Revisão int64, positiva |
| 36 | 16 | ID aleatório do commit |
| 52 | 32 | SHA-256 do arquivo pai; zeros no primeiro commit |
| 84 | 16 | Salt Argon2id |
| 100 | 4 | Memória Argon2id em KiB |
| 104 | 4 | Passagens Argon2id |
| 108 | 4 | Paralelismo Argon2id |
| 112 | 12 | Nonce da proteção da chave de dados |
| 124 | 32 | Chave de dados cifrada |
| 156 | 16 | Tag da chave de dados |
| 172 | 12 | Nonce do conteúdo |
| 184 | 4 | Tamanho N do conteúdo cifrado |
| 188 | N | Conteúdo cifrado |
| 188 + N | 16 | Tag do conteúdo |

AAD do envelope da chave: bytes `[0,112)`. AAD do conteúdo: bytes `[0,188)`. Portanto, alterações em identidade, revisão, pai, salt, parâmetros, envelope, nonce ou comprimento invalidam a autenticação. Os metadados só são confiáveis depois da verificação criptográfica.

O algoritmo e seus tamanhos são fixados pela versão. Não há seleção de cifra controlada por entrada externa. Uma nova combinação exigirá uma nova versão explicitamente suportada.

## Conteúdo interno

JSON UTF-8 de `VaultData`, cifrado integralmente:

```text
Categories: nomes de categorias
Credentials:
  Id, Service, Account, Password, Category, Favorite
  CreatedAt, ModifiedAt
  History: [ Id, Password, ChangedAt ]
```

Nenhum desses campos aparece nas configurações. IDs duplicados são rejeitados dentro do cofre. A categoria Outros é obrigatória e as categorias não podem se repetir ignorando caixa. A interface calcula indicadores de favorito e força; eles não substituem os dados autenticados.

## Evolução

Versões desconhecidas são rejeitadas, sem tentativa de adivinhação ou downgrade. Migrações futuras devem autenticar a versão de origem, preparar uma cópia em memória, validar o destino, preservar um backup de origem e só depois confirmar uma nova revisão.

O hash pai registra o parentesco imediato, mas não é uma âncora externa contra rollback. Nesta versão, qualquer divergência de arquivos exige decisão explícita; uma revisão numericamente maior nunca é selecionada automaticamente.
