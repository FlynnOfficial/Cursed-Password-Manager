# Geração uniforme e auditoria local

Os alfabetos são disjuntos: 10 dígitos, 26 minúsculas, 26 maiúsculas e os símbolos definidos em `PasswordGenerator.SymbolAlphabet`. Comprimentos aceitos: 10 a 60. Sem grupo selecionado, a geração é recusada.

## Uniformidade

Para alfabeto de tamanho A e comprimento L, cada candidato tem probabilidade `A^(-L)`: cada posição usa `RandomNumberGenerator.GetInt32(A)`. Essa API evita o viés da redução modular simples.

Se a política de cobertura está ativa, o candidato é aceito somente quando contém pelo menos um caractere de cada grupo selecionado. Seja S o conjunto válido. Para qualquer `x ∈ S`:

```text
P(resultado = x) = soma(k >= 0) [(1 - |S| / A^L)^k / A^L] = 1 / |S|
```

Não se fixa um caractere por grupo antes de embaralhar, pois esse procedimento não produz necessariamente distribuição uniforme sobre todas as strings válidas. O comprimento mínimo torna a rejeição prática com até quatro grupos. O laço tem tempo esperado finito; como todo algoritmo de rejeição, não há número máximo determinístico de tentativas.

## Entropia

Sem restrições: `H = L log2(A)`. Com cobertura obrigatória, conta-se o conjunto válido por inclusão-exclusão:

```text
|S| = soma(T subconjunto dos grupos) (-1)^|T| (A - soma(tamanho(g), g ∈ T))^L
H = log2(|S|)
```

`BigInteger` evita perda de exatidão na contagem. O log final é aproximado em ponto flutuante para apresentação. Os testes verificam comprimentos, grupos, zeros à esquerda, cálculo de entropia e um limite estatístico amplo para detectar regressões grosseiras. O teste estatístico não é uma prova criptográfica; a justificativa é o algoritmo e o RNG do sistema.

## Auditoria

Regras locais deliberadamente simples: menos de 12 caracteres; numéricas com menos de 20; mesma senha entre contas; diversidade muito baixa; blocos periódicos; trechos de cinco caracteres em sequências ascendentes/descendentes ou padrões comuns de teclado; pequena lista fixa de exemplos públicos comuns.

Comparações ficam somente na memória da sessão. Não há hash persistente, consulta de rede nem relatório em disco. Os resultados identificam a credencial por ID e só são recalculados sob demanda; uma alteração invalida o relatório anterior. Não se afirma que todas as senhas fracas são detectadas ou que uma senha não foi vazada.
