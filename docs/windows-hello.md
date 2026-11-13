# Windows Hello: modelo e condições de suporte

## Implementação

O código usa CNG/`RSACng` com **Microsoft Passport Key Storage Provider**, disponível para APIs desktop. A chave fica no armazenamento do usuário Windows e tem nome próprio do aplicativo. Sua chave privada não é exportada. O arquivo `device-unlock.json` guarda apenas identificador, sufixo de chave e ciphertext.

O material cifrado por RSA-OAEP-SHA256 contém a chave de proteção da sessão (32 bytes), ID do cofre (16) e salt (16). Esse vínculo é conferido antes de usar o material para autenticar o cofre. Um salt novo na troca da senha mestra invalida o envelope anterior.

A criação exige senha mestra. A chave é de 2.048 bits, somente para descriptografia, sem exportação. São exigidos `NgcCacheType = 1` e `NgcKeyImplType = 1` (implementação por hardware). Propriedade ausente ou valor inesperado é motivo de recusa; não há fallback para chave de software, DPAPI ou simples confirmação visual.

Antes e depois da operação autorizada, o código tenta descriptografar com `NCRYPT_SILENT_FLAG`. Só aceita a recusa `NTE_SILENT_CONTEXT`; sucesso ou outro erro não comprovam a política e impedem a ativação/desbloqueio. O pedido normal solicita gesto por `PinCacheIsGestureRequired`. Cancelamento não libera a sessão. As propriedades de uso/cache/exportação/hardware são verificadas a cada abertura.

## Distribuições

| Distribuição | Comportamento |
| --- | --- |
| EXE normal | Pode tentar ativar se o provedor cumprir todos os requisitos |
| Portátil | Desabilitado; senha mestra é o método de desbloqueio |
| MSIX | Não distribuído: a abordagem escolhida não depende de identidade de pacote |

As APIs WinRT `KeyCredentialManager` e `UserConsentVerifier` foram avaliadas. O fluxo documentado de assinatura de desafios de `KeyCredentialManager` é orientado à autenticação; uma confirmação isolada não protege uma chave de cofre. A implementação usa diretamente a operação de descriptografia no provedor do Windows, sem derivar chaves de assinaturas públicas e sem um verificador booleano ignorável.

## Limitações reais

- As propriedades NGC são específicas do provedor e sua disponibilidade pode variar. O suporte a RSA-OAEP e ao comportamento de cache precisa ser comprovado no dispositivo; não se presume suporte universal no Windows 10/11.
- O caminho positivo depende de configuração do Hello e TPM compatível. Ele ainda precisa de testes interativos em hardware representativo. Um resultado positivo não foi inventado no ambiente de desenvolvimento.
- O teste portátil confirma falha segura antes de acessar o provedor. Os testes de cifra confirmam vínculo do material ao ID/salt.
- Reset de PIN, TPM, perfil ou exclusão da chave pode invalidar o desbloqueio. A senha mestra continua suficiente.
- Um processo comprometido sob o mesmo usuário pode solicitar operações ao provedor e apresentar uma solicitação de autenticação. A fronteira é usuário/dispositivo/gesto, não um isolamento perfeito por executável. A presença do usuário precisa continuar obrigatória no provedor; malware com acesso à memória de uma sessão aberta está fora das garantias.
- Desativar remove a chave privada e o registro local. Rotacionar a senha mestra também desativa o recurso. Backups criptografados não incluem esse vínculo ao dispositivo.

## Referências de API

- [Windows Hello — Microsoft](https://learn.microsoft.com/en-us/windows/apps/develop/security/windows-hello)
- [NCryptCreatePersistedKey](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptcreatepersistedkey)
- [NCryptDecrypt e NCRYPT_SILENT_FLAG](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptdecrypt)
- [Propriedades de armazenamento de chaves CNG](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers)
- [RSACng](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsacng)

Não habilite uma distribuição em que a política precise ser relaxada para funcionar. Ampliação de compatibilidade deve preservar proteção criptográfica e incluir testes de tentativa de bypass silencioso.
