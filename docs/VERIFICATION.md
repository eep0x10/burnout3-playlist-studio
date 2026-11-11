# Verificação da versão 1.0.0

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
