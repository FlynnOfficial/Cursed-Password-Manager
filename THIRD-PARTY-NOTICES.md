# Dependências e atribuições

O código e os recursos originais deste projeto são identificados por **FlynnOfHearts**.

Dependências de execução:

| Dependência | Versão | Licença / origem |
| --- | --- | --- |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT — [repositório do autor](https://github.com/kmaragon/Konscious.Security.Cryptography) |
| Konscious.Security.Cryptography.Blake2 | 1.1.1 | MIT — mesmo projeto |
| .NET / Windows Desktop Runtime | Selecionada pelo SDK .NET 10 na publicação | [licenças do .NET](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [WPF](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT) e avisos distribuídos com o runtime |

Dependências apenas de testes: xUnit 2.9.3 (Apache-2.0), xunit.runner.visualstudio 3.1.1 (Apache-2.0), Microsoft.NET.Test.Sdk 17.14.1 e seus componentes de test platform (MIT). A lista exata, inclusive dependências transitivas, está nos arquivos `packages.lock.json` de cada projeto.

Os avisos e licenças dos pacotes devem acompanhar as distribuições quando exigido. O script de build preserva os avisos do runtime e inclui esta relação. Consulte os metadados oficiais dos pacotes antes de redistribuir uma versão alterada.

Os ícones são desenhos originais em `assets/cursed.svg` e no script reproduzível `tools/Create-Icon.ps1`. Nenhuma imagem ou logotipo da Microsoft foi incorporado como recurso do aplicativo.
