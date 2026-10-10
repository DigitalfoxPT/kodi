# Kodi Seek Preview Generator

Aplicação WinUI 3 para Windows que cria sidecars de preview para a versão Android TV deste fork.

Para cada vídeo é criado exatamente um ficheiro BIF na mesma pasta e com o mesmo nome-base:

```text
Shows/Serie/Temporada 01/Episodio 01.mkv
Shows/Serie/Temporada 01/Episodio 01.bif
```

O BIF contém imagens JPEG indexadas de 10 em 10 segundos. O Kodi lê apenas a imagem correspondente ao ponto selecionado. Não existe um ficheiro global e o Android TV nunca tenta descodificar o vídeo para produzir previews.

## Utilização

1. Extraia o ZIP completo da aplicação; mantenha a pasta `Data` junto de `Kodi.exe`.
2. Abra `Kodi.exe`.
3. Escolha a pasta principal (por exemplo, `Shows`).
4. Clique em **Analisar e gerar previews**.

A aplicação percorre todas as subpastas. Um `.bif` válido com data igual ou posterior à do respetivo vídeo é ignorado. Um sidecar inexistente, inválido ou desatualizado é recriado através de um ficheiro temporário, evitando deixar resultados incompletos após um cancelamento.

A última pasta fica guardada em `%LOCALAPPDATA%/KodiSeekPreviewGenerator/settings.json`. Ao reabrir a aplicação, a análise/geração inicia automaticamente se essa pasta ainda existir. O botão **Cancelar** termina a geração em curso; os vídeos originais não são alterados.

Não é instalado nem executado qualquer serviço. A aplicação só trabalha quando é aberta pelo utilizador.

## Imagens e formato BIF

O FFmpeg seleciona o primeiro frame de cada intervalo de 10 segundos, começando em zero, e reduz a largura para um máximo de 480 px, mantendo a proporção. O filtro está em `src/KodiSeekPreviewGenerator.Core/FfmpegRunner.cs`.

Todos os inteiros do BIF usam little-endian. A estrutura escrita por `BifFile.cs` é:

| Posição/estrutura | Conteúdo |
| --- | --- |
| Bytes 0–7 | Magic `89 42 49 46 0D 0A 1A 0A` |
| Bytes 8–11 | Versão BIF: 0 |
| Bytes 12–15 | Número de imagens JPEG |
| Bytes 16–19 | Multiplicador temporal: 10000 ms |
| Bytes 20–24 | Revisão do gerador: ASCII `KSPG2` |
| Restante cabeçalho até 63 | Reservado, preenchido com zeros |
| A partir do byte 64 | Índice com `N+1` entradas de 8 bytes: timestamp `uint32` e offset `uint32` |
| Última entrada do índice | Timestamp `0xFFFFFFFF` e offset do fim do ficheiro |
| Após o índice | JPEGs concatenados |

Os timestamps 0, 1, 2 correspondem a 0 s, 10 s, 20 s. O limite imposto pelos offsets de 32 bits é inferior a 4 GiB. O marcador `KSPG2` permite ao gerador rejeitar sidecars antigos com imagens de meio de intervalo e regenerá-los; não é uma nova versão do formato BIF.

O leitor Android está em `xbmc/video/VideoGeneratedImageFileLoader.cpp` na raiz do repositório. Alterações ao índice, timestamps ou imagens devem ser verificadas nos dois lados.

## Desenvolvimento

Os projetos `App`, `Core`, `Launcher` e os testes estão respetivamente em `src/` e `tests/`. A aplicação usa .NET 10 e Windows App SDK 2.5.1. O pacote publicado é x64 e inclui os runtimes, `Data/ffmpeg.exe`, avisos de licença e `Data/FFMPEG-VERSION.txt`.

A referência para compilação e testes é `.github/workflows/build-seek-preview-generator.yml` na raiz do repositório. O publish .NET cria a aplicação em `Data`; o workflow acrescenta FFmpeg e compila o launcher `Kodi.exe` em C++. Os testes existentes verificam a escrita BIF e a abertura das janelas da aplicação/launcher.

Consulte o [guia completo da branch](../../../docs/ANDROID-TV-SEEK-PREVIEW.md) para o mapa do código Android, comportamento do comando, release e limitações.
