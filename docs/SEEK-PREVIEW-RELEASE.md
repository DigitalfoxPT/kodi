# Kodi 21.3 Seek Preview - 2026.10.10

Esta release inclui as duas aplicações do projeto:

- `kodiapp-armeabi-v7a-release.apk`: Kodi 21.3 para Android TV ARMv7, com previews de navegação a partir de ficheiros BIF.
- `KodiSeekPreviewGenerator-win-x64.zip`: gerador de previews para Windows x64, com .NET e Windows App SDK incluídos. Extraia o ZIP completo e abra `KodiSeekPreviewGenerator/Kodi.exe`, mantendo a pasta `Data` ao lado.
- `SHA256SUMS.txt`: hashes SHA-256 dos dois downloads.

## Atualizações

- Correções da branch oficial Omega até `f8815ee40f49a700c047982d752be4b2a61420e2`: reinício dos serviços UPnP ao retomar no Android, atualização do ponto de retoma no UPnP e compatibilidade com a palavra reservada `sets` no MySQL 9.6.
- Windows App SDK atualizado de 2.4.0 para 2.5.1 estável.
- GitHub Actions de checkout, Java, .NET, cache e artefactos atualizados.
- FFmpeg obtido da distribuição atual de Gyan durante a compilação; a versão exata fica em `Data/FFMPEG-VERSION.txt`.
- Publicação conjunta apenas após sucesso das duas compilações, testes BIF, auditoria NuGet e testes de abertura da aplicação Windows.

O projeto mantém a base Kodi 21.3. O Kodi 22.0rc1 ainda é uma pré-release e não foi adotado.

## Dependências nativas pendentes

O APK mantém OpenSSL 1.1.1w, libcurl 8.10.0, libxml2 2.10.3 e FFmpeg 6.0.1 herdados do Kodi 21.3. OpenSSL 1.1.1 está [fora do suporte público](https://openssl-corporation.org/blog/2023-06-15-1.1.1-EOL-Reminder.html) e existem [avisos de segurança posteriores para libcurl 8.10.0](https://curl.se/docs/vuln-8.10.0.html). Estas dependências precisam de migração ou revisão de correções com testes de compatibilidade Android; não foram atualizadas nesta release. A auditoria NuGet cobre apenas o gerador Windows.

O [guia da branch](ANDROID-TV-SEEK-PREVIEW.md) documenta o funcionamento, mapa do código, compilações e testes manuais. A compilação do APK não substitui testes numa Android TV física.

O APK mantém a chave persistente usada nas compilações anteriores. A capacidade de atualizar uma instalação existente depende de esta usar a mesma assinatura.

O histórico de `android-tv-seek-preview` foi consolidado num único commit com o código atual e estas atualizações. As outras branches e as tags históricas do Kodi não foram alteradas.

Fontes: [Kodi Omega](https://github.com/xbmc/xbmc/compare/21.3-Omega...f8815ee40f49a700c047982d752be4b2a61420e2), [Windows App SDK 2.5.1](https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.5.1).
