# Verificação das versões

## 1.1.0 — aviso de faixa customizada

Verificação em 12/11/2026. O jogo não foi aberto para esta implementação.

- **84 checks passaram**, também em execução independente. Incluem as verificações anteriores de biblioteca, download simulado, instalação e ativação, além do novo observador.
- PSS real em Windows: arquivo sintético mantido aberto pelo processo de teste e por um processo filho próprio foi reconhecido pela whitelist e deixou de ser reconhecido após fechar. O acesso entre processos usa somente `QUERY_INFORMATION | DUP_HANDLE` (`0x440`).
- O leitor de produção rejeitou o mesmo filho, que não se chama Takedawn. Nenhum fixture foi renomeado para imitar o jogo.
- Os testes verificam debounce, reabertura, caminho exato e supressão de candidatos simultâneos. O caminho padrão não altera configuração nem habilita diagnóstico do jogo.
- Revisão independente conferiu limpeza de handles/snapshots e execução em segundo plano. Nenhuma leitura de memória, bytes de música ou posição de arquivo é feita pelo observador.
- Prévia real do popup capturada fora do jogo: janela visível, alpha 240/255, foco permaneceu no gerenciador, duração observada de aproximadamente seis segundos. A imagem está identificada como demonstração.
- Detectar um arquivo aberto não comprova audibilidade, pausa ou contexto de corrida. A validação dentro do Takedawn continua com o usuário.

## 1.0.0 — biblioteca e ativação

Verificado em Windows, em 10 e 11/11/2026.

| Prova | Resultado |
| --- | --- |
| Core Release, 65 verificações sintéticas | Passou também em execução independente; configuração e dados isolados. |
| Revisão independente de instalação e configuração | Sem P0/P1 pendente identificado após correções. |
| Bootstrap oficial de yt-dlp, FFmpeg/ffprobe e Deno | Downloads e hashes verificados; execução real. |
| YouTube → MP3 | Download real do vídeo oficial Big Buck Bunny e conversão concluídos. |
| Spotify público | 50 metadados importados; integralidade explicitamente não confirmada. |
| WAV original → MP3 e metadados | Quatro faixas sintetizadas de 12 segundos; títulos, artistas, álbum e duração conferidos com ffprobe. |
| Instalação e atualização | Exportação gerenciada e alteração de metadados em sandbox. |
| Instalação entre volumes | Biblioteca no C: exportada e reordenada em pasta de teste no F:. |
| Ativação exclusiva com MP3 real | Quatro arquivos em snapshot isolado; pasta/configuração/hashes validados. |
| Compatibilidade de ativação | Configuração 0.3.1 testada em sandbox; 0.2.0 sinalizou seleção manual. Versão local detectada: 0.2.0. |
| Contaminação por áudio extra | Adição de MP3 ao snapshot invalidou o estado pronto. |
| Interface nativa | Criação, edição, importação, reprodução e botão Carregar playlist em sandbox inspecionados por CUA. |
| Build portable | Publicação self-contained win-x64, sem instalação global de runtime. |
| Pacote ZIP extraído | EXE abriu com biblioteca vazia e identificou o Takedawn 0.2.0, sem depender de SDK. |

Os testes sintéticos usam processos e HTTP simulados; não representam downloads reais. As linhas de rede e FFmpeg acima foram verificadas separadamente. A letra ausente no áudio de demonstração foi informada corretamente; os estados de letras simples/sincronizadas e falhas foram cobertos sinteticamente. O jogo não foi aberto; a validação dentro da corrida fica com o usuário, conforme solicitado.

O antigo clipe de teste do yt-dlp estava indisponível e produziu erro explícito. O teste alternativo usou **Big Buck Bunny © 2008 Blender Foundation / peach.blender.org**, sob [CC BY 3.0](https://peach.blender.org/about/), obtido no [canal oficial Blender](https://www.youtube.com/watch?v=aqz-KE-bpKQ). Esse áudio não é incluído no pacote.

As capturas usam somente áudio original sintetizado para demonstração. O pacote distribuído começa com biblioteca vazia, sem dados de teste, músicas ou letras de terceiros.

## 1.1.1 launcher verification

The minimized startup was checked with the manager alone: 95 visible-window samples were minimized, no activation was observed, and the detector initialized. Duplicate launches reused the existing data-folder instance. The enable-notification signal persisted the preference in an existing instance without changing its normal/minimized state. Separate data folders remained independent. Test instances were closed afterwards.

The companion launcher was tested using harmless probe executables in folders containing spaces. Manager flags and each process's working directory matched the contract. No Takedawn process was started. Real gameplay and fullscreen notification appearance remain user validation.

## 1.1.2 automatic close verification

Manager-only probes confirmed graceful exit from the minimized state, isolation between data folders, and a close request issued 500 ms before manager startup. With no manager, the close command returned silently after its bounded five-second wait without creating a data directory or opening a window. The close event uses the existing cancellation and notification-cleanup pipeline. No game was opened.

## 1.1.3 shortcut button verification

The existing native desktop design is retained. **Criar atalho** sits beside notification controls and gives immediate dialog/status feedback; no new motion is introduced. A CUA check confirmed the button is visible, the picker opens at the detected game location, and cancelling preserves the shortcut and preferences. The screenshot uses the original four-track demo library. This WinForms application retains its desktop minimum size rather than providing a mobile layout.

An independent 17-check Windows COM probe created and read shortcuts only in a temporary folder. It verified paths containing spaces, target/arguments/working directory/icon, refusal to overwrite without permission, explicit replacement, and rejection of missing files, invalid names, percent signs and invalid destinations. The GUI defaults replacement confirmation to **No**. No generated shortcut or game was executed.
