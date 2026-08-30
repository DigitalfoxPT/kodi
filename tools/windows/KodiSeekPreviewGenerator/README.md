# Kodi Seek Preview Generator

Aplicação WinUI 3 para Windows que cria sidecars de preview para a versão Android TV deste fork.

Para cada vídeo é criado exatamente um ficheiro BIF na mesma pasta e com o mesmo nome-base:

```text
Shows/Serie/Temporada 01/Episodio 01.mkv
Shows/Serie/Temporada 01/Episodio 01.bif
```

O BIF contém imagens JPEG indexadas de 10 em 10 segundos. O Kodi lê apenas a imagem correspondente ao ponto selecionado. Não existe um ficheiro global e o Android TV nunca tenta descodificar o vídeo para produzir previews.

## Utilização

1. Extraia o ZIP completo da aplicação; mantenha `ffmpeg.exe` junto do executável.
2. Abra `KodiSeekPreviewGenerator.App.exe`.
3. Escolha a pasta principal (por exemplo, `Shows`).
4. Clique em **Analisar e gerar previews**.

A aplicação percorre todas as subpastas. Um `.bif` válido e mais recente do que o respetivo vídeo é ignorado. Um sidecar inexistente, inválido ou desatualizado é recriado através de um ficheiro temporário, evitando deixar resultados incompletos após um cancelamento.

Não é instalado nem executado qualquer serviço. A aplicação só trabalha quando é aberta pelo utilizador.
