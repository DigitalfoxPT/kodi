# Guia da branch android-tv-seek-preview

## Objetivo e funcionamento

Este fork acrescenta ao Kodi 21.3 uma navegação por imagens no Android TV. O trabalho de descodificar os vídeos e extrair imagens acontece previamente no Windows. Para `Episodio.mkv`, o gerador escreve `Episodio.bif` ao lado do vídeo. O Android usa o índice do BIF para ler apenas o JPEG correspondente ao instante selecionado.

```text
Windows: vídeos → FFmpeg → JPEGs a cada 10 segundos → um BIF por vídeo
Android: esquerda/direita → instante selecionado → índice BIF → JPEG → Estuary
         OK → seek para o instante selecionado; Voltar → cancelar
```

O Windows gera os ficheiros numa pasta local ou numa partilha acessível. O Kodi precisa de conseguir ler o vídeo e o BIF correspondente através do seu sistema de ficheiros. Não existe uma base global de previews. Copiar apenas o vídeo não disponibiliza as imagens.

## Comportamento no Android

- As ações `StepBack`/`SmallStepBack` e `StepForward`, normalmente associadas às setas, ativam o preview quando existe um sidecar BIF com pelo menos 64 bytes.
- O primeiro toque seleciona a marca de 10 segundos anterior ou seguinte à posição atual; os seguintes mudam exatamente 10 segundos. Por exemplo, em 00:37, direita seleciona 00:40 e esquerda seleciona 00:30.
- A reprodução pausa se o player permitir e estiver a reproduzir. OK confirma o seek; Voltar cancela sem alterar a posição. A reprodução retoma apenas se a funcionalidade a tiver pausado.
- Estuary mostra cinco imagens: posição selecionada, -20 s, -10 s, +10 s e +20 s. Os instantes são limitados à duração disponível. O painel não aparece em Live TV.
- Sem BIF, as ações seguem a navegação normal do Kodi. Um BIF corrompido pode ativar o painel mas não produzir uma imagem; o leitor rejeita cabeçalhos, offsets e tamanhos inválidos.
- `Player.SeekPreview` e `Player.SeekPreview(offsetEmSegundos)` fornecem as imagens à skin. O URL de textura contém o instante e uma versão baseada em tamanho/data do BIF, para evitar reutilizar imagens antigas após a sua substituição.
- Abrir ou fechar um vídeo limpa o estado pendente. A interceção das ações de preview e os labels específicos são condicionados por `TARGET_ANDROID`.

## Comportamento do gerador Windows

Aplicação WinUI 3/.NET 10 x64, em português, distribuída como ZIP com runtimes e FFmpeg incluídos. `Kodi.exe` abre `Data/KodiSeekPreviewGenerator.App.exe`.

- Procura recursivamente vídeos nas extensões definidas em `PreviewGenerator.cs`; ignora pastas inacessíveis e processa vídeos sequencialmente.
- Gera um `.bif` por vídeo. Ignora sidecars válidos cuja data seja igual ou posterior à do vídeo. A validação exige a revisão `KSPG2`, pelo que ficheiros produzidos pelo gerador antigo são regenerados.
- Extrai o primeiro frame de cada intervalo absoluto de 10 segundos, com largura máxima de 480 px e proporção preservada. O filtro `select` evita o desvio de aproximadamente 5 segundos que existia com `fps=1/10`.
- Escreve primeiro num ficheiro temporário ao lado do BIF final e substitui-o depois da geração. Cancelar termina o FFmpeg e tenta remover JPEGs e ficheiros temporários.
- Guarda a última pasta em `%LOCALAPPDATA%/KodiSeekPreviewGenerator/settings.json`. Ao reabrir, inicia automaticamente a análise/geração se essa pasta ainda existir. Não instala um serviço nem corre quando a aplicação está fechada.

Veja [as instruções do gerador](../tools/windows/KodiSeekPreviewGenerator/README.md) para utilização, compilação e formato BIF.

## Mapa do código

| Responsabilidade | Ficheiros |
| --- | --- |
| Ações, seleção temporal, pausa, confirmação e cancelamento | `xbmc/SeekHandler.cpp`, `xbmc/SeekHandler.h` |
| Labels de tempo e imagem para a skin | `xbmc/guilib/guiinfo/PlayerGUIInfo.cpp`, `xbmc/GUIInfoManager.cpp`, `xbmc/guilib/guiinfo/GUIInfoLabels.h` |
| Leitura do índice BIF, JPEG e validação de limites | `xbmc/video/VideoGeneratedImageFileLoader.cpp` |
| Suporte dos URLs de imagem na cache | `xbmc/TextureCache.cpp`, `xbmc/TextureCacheJob.cpp` |
| Registo e visibilidade do diálogo de preview | `xbmc/dialogs/GUIDialogSeekPreview.*`, `xbmc/guilib/GUIWindowManager.cpp`, `xbmc/guilib/WindowIDs.h`, `xbmc/dialogs/CMakeLists.txt` |
| Reset do estado na abertura/fecho do vídeo e diálogo | `xbmc/application/ApplicationPlayer.cpp`, `xbmc/application/Application.cpp` |
| Layout das cinco imagens | `addons/skin.estuary/xml/DialogSeekPreview.xml`, `addons/skin.estuary/xml/VideoOSD.xml` |
| Extração genérica de thumbnails por instante | `xbmc/cores/VideoPlayer/DVDFileInfo.cpp`, `xbmc/cores/VideoPlayer/DVDFileInfo.h` |
| UI, pasta guardada e launcher Windows | `tools/windows/KodiSeekPreviewGenerator/src/KodiSeekPreviewGenerator.App/`, `src/KodiSeekPreviewGenerator.Launcher/` dentro da mesma pasta |
| Pesquisa de vídeos e substituição segura do BIF | `tools/windows/KodiSeekPreviewGenerator/src/KodiSeekPreviewGenerator.Core/PreviewGenerator.cs` |
| Comando FFmpeg e progresso | `tools/windows/KodiSeekPreviewGenerator/src/KodiSeekPreviewGenerator.Core/FfmpegRunner.cs` |
| Escrita e validação BIF | `tools/windows/KodiSeekPreviewGenerator/src/KodiSeekPreviewGenerator.Core/BifFile.cs` |

## Compilação e release

O workflow `.github/workflows/release-seek-preview.yml` corre num push para esta branch ou por execução manual nesta branch. Chama os dois workflows reutilizáveis:

- `build-android-tv.yml`: Ubuntu 22.04, Java 17, NDK `21.4.7075529`, plataforma Android 34, build-tools 33.0.1, API NDK 30, alvo ARMv7. Compila as dependências, Kodi e APK. Restaura `ANDROID_KEYSTORE_BASE64` para manter a assinatura existente.
- `build-seek-preview-generator.yml`: Windows 2022, .NET 10, WinUI 3 x64. Corre os testes BIF, audita pacotes NuGet diretos/transitivos, publica self-contained, verifica a janela, inclui FFmpeg e compila/testa o launcher.

Só depois de os dois jobs passarem, a release recebe APK, ZIP Windows e `SHA256SUMS.txt`; o draft é então publicado. Os artefactos de Actions expiram ao fim de 30 dias. A release guarda os downloads publicados.

O tag configurado é `seek-preview-2026.10.10`. Para uma futura versão, atualize o tag, o título e as notas no workflow antes de publicar: o job rejeita um tag existente que aponte para outro commit. Uma repetição para o mesmo commit pode substituir os assets desse tag.

Comandos de referência do gerador, a executar com o SDK .NET 10 instalado:

```powershell
dotnet run --project tools/windows/KodiSeekPreviewGenerator/tests/KodiSeekPreviewGenerator.Core.SmokeTests/KodiSeekPreviewGenerator.Core.SmokeTests.csproj --configuration Release
dotnet publish tools/windows/KodiSeekPreviewGenerator/src/KodiSeekPreviewGenerator.App/KodiSeekPreviewGenerator.App.csproj --configuration Release --runtime win-x64 --self-contained true --property Platform=x64 --output work/publish/Data
```

O publish isolado não inclui o download de FFmpeg nem a compilação do launcher; para o pacote completo siga o workflow Windows. Para o Android, siga o workflow e [o guia original](README.Android.md).

## Validação e manutenção

Os testes BIF verificam cabeçalho, contagem, intervalo, timestamps, sentinel, revisão do gerador e rejeição de magic corrompido. O CI Windows verifica que a aplicação e o launcher abrem uma janela; isso não substitui um teste completo com vídeos reais. A compilação APK não substitui os testes numa Android TV física.

Para testar manualmente: vídeo com BIF e sem BIF; navegação em 00:37; OK e Voltar; vídeo previamente pausado; princípio/fim do vídeo; sidecar substituído; ficheiro numa partilha; cancelamento e reabertura do gerador.

As [notas da release](SEEK-PREVIEW-RELEASE.md) registam as correções oficiais Omega integradas e as bibliotecas atualizadas. As dependências nativas do Kodi 21.3 continuam antigas: OpenSSL 1.1.1w, libcurl 8.10.0, libxml2 2.10.3 e FFmpeg 6.0.1. OpenSSL 1.1.1 está fora de suporte público e existem avisos posteriores para libcurl. A migração precisa de adaptar e testar o build Android; a auditoria NuGet não cobre estas bibliotecas.

O histórico desta branch foi consolidado num único commit chamado `Kodi 21.3`, a pedido do proprietário. Não use esse histórico para reconstruir a evolução da funcionalidade; use este guia, o código e as notas da release. As outras branches e tags herdadas do Kodi mantêm o seu histórico.

## Configurações do fork

A branch mantém apenas os três workflows usados pelo projeto: release, compilação Android e compilação do gerador Windows. Foram removidos os workflows exclusivos do upstream para Doxygen, Weblate e sincronização de traduções, bem como o CODEOWNERS dos mantenedores upstream, as regras do Mergeable e a lista de commits antigos ignorados por git blame.

No repositório, os workflows herdados Doxygen, Weblate, SonarQube e sincronização de traduções estão desativados para evitar execuções `skipped` causadas por pushes na `master`. O workflow de stale issues já estava desativado. O workflow da release permanece ativo.

As execuções antigas de Actions e os seus artefactos temporários foram eliminados. Os downloads publicados encontram-se na release. O tag `seek-preview-2026.10.10` conserva o snapshot usado para compilar os programas; a limpeza posterior da branch altera apenas configurações e documentação, sem alterar o código das aplicações.
