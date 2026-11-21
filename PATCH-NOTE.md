# Agent — arquivos para habilitar a geração de APK

Este pacote é um conjunto de arquivos para aplicar manualmente ao repositório `g3stavo73/Agent`.
Ele preserva os caminhos relativos à raiz do repositório.

## Como aplicar

1. Baixe e extraia este ZIP.
2. Copie o conteúdo extraído para a raiz do clone do repositório `Agent`, mantendo as pastas.
3. Aceite a substituição dos dois arquivos indicados como **SUBSTITUIR**.
4. Adicione os arquivos indicados como **NOVO**.
5. Faça commit e push para `main`.

Não extraia o conteúdo dentro de `scr/Agent.Mobile`; a pasta `.github` e o arquivo `Agent.sln` precisam ficar na raiz do repositório.

## Arquivos incluídos

| Caminho no repositório | Ação | Motivo |
|---|---|---|
| `Agent.sln` | SUBSTITUIR | Corrige os caminhos dos projetos: o repositório usa `scr/`, não `src/`. |
| `scr/Agent.Mobile/Agent.Mobile.csproj` | SUBSTITUIR | Corrige a referência ao projeto Core (nome real da pasta `agent.core`) e declara o formato APK com `AndroidPackageFormats`. |
| `scr/Agent.Mobile/Resources/AppIcon/appicon.svg` | NOVO | Ícone MAUI referenciado pelo projeto, ausente na árvore atual do repositório. |
| `scr/Agent.Mobile/Resources/Splash/splash.svg` | NOVO | Splash screen MAUI referenciada pelo projeto, ausente na árvore atual do repositório. |
| `.github/workflows/android-apk.yml` | NOVO | Workflow que instala .NET/MAUI Android, compila o app e publica o APK como artefato. |

## Como obter o APK

Depois de enviar os arquivos:

1. Abra a aba **Actions** do GitHub no repositório.
2. Selecione **Android APK**.
3. Aguarde a execução terminar com sucesso.
4. Abra a execução e baixe o artefato **Agent-Android-APK**.
5. Dentro do ZIP do artefato estarão `Agent.apk` e `Agent.apk.sha256`.

O workflow também pode ser iniciado manualmente pelo evento `workflow_dispatch`.

## Observações importantes

- O workflow compila somente `scr/Agent.Mobile/Agent.Mobile.csproj` e seu projeto referenciado `scr/agent.core`. A API ASP.NET Core é um serviço separado e não é embutida no APK.
- O SDK é obtido a partir de `global.json` (atualmente fixado em `11.0.100`) e o alvo Android permanece `net11.0-android36.0`, conforme o projeto existente.
- O workflow cria uma chave de assinatura temporária a cada execução. O APK serve para instalação e teste, mas builds futuros terão outra assinatura. Para atualizar o app sem desinstalá-lo, será necessário configurar uma chave de assinatura permanente nos GitHub Actions Secrets e reutilizá-la.
- Este pacote não altera `global.json`, `dependencies.yml`, o código-fonte das telas, nem as configurações de backend.
- A compilação ainda precisa ser executada pelo GitHub Actions. A criação deste pacote não significa que o APK já foi gerado ou que o projeto passou em build.
