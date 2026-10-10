# Contexto desta branch

Leia primeiro `docs/ANDROID-TV-SEEK-PREVIEW.md`. Esse documento é a referência para o comportamento, arquitetura, ficheiros relevantes, compilações e limitações de `android-tv-seek-preview`.

- Base: Kodi 21.3 Omega, com patches próprios para previews no Android TV e correções oficiais Omega documentadas nas notas da release.
- Duas aplicações: APK Android TV ARMv7 e gerador BIF Windows x64 em WinUI 3/.NET 10. O executável Windows `Kodi.exe` é o launcher do gerador.
- Contrato essencial: um `.bif` junto de cada vídeo, JPEGs nas marcas de 10 segundos, confirmação com OK e cancelamento com Voltar. O preview Android lê o BIF; não gere imagens descodificando o vídeo no Android.
- Antes de alterar o formato BIF ou a seleção temporal, consulte o produtor C# e o leitor C++ indicados no guia. Atualize os dois lados e a documentação quando necessário.
- Use os workflows da branch como referência dos comandos de compilação. A chave Android é um secret persistente; não crie nem substitua a assinatura inadvertidamente.
- A auditoria NuGet cobre apenas o Windows. A lista de dependências nativas antigas e a necessidade de migração estão documentadas; não declare toda a aplicação livre de vulnerabilidades com base nessa auditoria.
- O histórico foi consolidado por pedido do proprietário. Para novas alterações, siga o pedido atual do utilizador; não faça novos force-pushes, limpezas de histórico ou eliminações de tags automaticamente.
