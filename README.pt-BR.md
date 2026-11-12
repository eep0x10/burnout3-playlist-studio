# Burnout 3 Playlist Studio

**Monte sua trilha. Organize sua biblioteca. Leve suas playlists ao Takedawn.**

[![Compilação Windows](https://github.com/eep0x10/burnout3-playlist-studio/actions/workflows/build.yml/badge.svg)](https://github.com/eep0x10/burnout3-playlist-studio/actions/workflows/build.yml)

Gerenciador portable para Windows, com tema escuro inspirado nas pistas de Burnout 3. Organiza faixas, procura versões no YouTube, prepara MP3s, busca letras e exporta playlists para o sistema de músicas customizadas do **Burnout 3 Recomp / Takedawn**.

[English](README.md) · [Baixar para Windows x64](https://github.com/eep0x10/burnout3-playlist-studio/releases/latest)

![Biblioteca e player](docs/screenshots/library.png)

As capturas usam faixas originais sintetizadas para demonstração. A biblioteca do download começa vazia.

## Começar

1. Extraia **toda a pasta** do ZIP em um local onde você possa gravar arquivos.
2. Abra `BurnoutPlaylistStudio.exe`. Não precisa instalar o .NET.
3. Crie uma playlist e clique em **Importar**. Adicione nomes de músicas, links do YouTube, uma playlist pública do Spotify, arquivos locais ou CSV.
4. Confira as faixas e feche o Takedawn.
5. Clique em **Baixar e carregar**. Na primeira utilização, o aplicativo baixa e verifica yt-dlp, FFmpeg e Deno nas fontes oficiais. Isso requer internet e espaço adicional.
6. No **Takedawn 0.2.1 ou mais recente**, a playlist fica configurada para seleção automática na próxima execução. No **0.2.0**, selecione-a manualmente em **EA GAMES TRAX → X → A** a cada execução.

**Só baixar** prepara a biblioteca sem carregar no jogo. **Carregar playlist** ativa faixas já preparadas. Arquivos locais MP3, WAV, FLAC e Ogg também podem ser usados.

## Aviso ao trocar de música

Mantenha o gerenciador aberto e marque **Aviso da música no jogo**. Um popup com título e artista aparece no canto inferior esquerdo quando um MP3 gerenciado é detectado no Takedawn em primeiro plano. Pode minimizar o gerenciador. **Testar aviso** mostra uma prévia identificada como demonstração, sem abrir o jogo.

**Abrir os dois com um clique:** o portable 1.1.1 inclui `Launch-Takedawn.vbs`, que abre o gerenciador minimizado com os avisos ativos e inicia o jogo. [Como configurar o atalho](docs/NOW_PLAYING.md#start-together-with-takedawn-111).

É um aviso externo no estilo do jogo, sem alterar o HUD nativo, os saves ou o executável. A detecção consulta apenas os arquivos gerenciados abertos pelo Takedawn, sem capturar memória nem habilitar diagnóstico completo. Pode levar um ou dois segundos e não confirma audibilidade ou pausa. A aparência sobre o jogo em tela cheia ainda depende de teste do usuário. [Detalhes e limites](docs/NOW_PLAYING.md).

![Prévia do aviso, com dados de demonstração e fora do jogo](docs/screenshots/now-playing-preview.png)

## Apenas a playlist escolhida

Carregar cria uma cópia gerenciada isolada, contendo somente os MP3s da playlist selecionada. O jogo aponta diretamente para essa pasta; as outras playlists da biblioteca ficam de fora. Listas vazias ou com faixas ainda não preparadas são recusadas.

Em versões compatíveis, são configurados `custom_music_dir`, `custom_music_at_start = true` e o nome textual de `custom_music_soundtrack`, com backup das configurações anteriores. Em versões antigas ou desconhecidas, o aplicativo informa a seleção manual necessária. Isso não impede que você troque a trilha pelo menu do jogo, nem substitui áudio de filmes/cutscenes.

## Sua biblioteca

- Criar, renomear e remover playlists; adicionar e remover faixas.
- Filtrar músicas e alterar a ordem com ↑ / ↓.
- Editar título, artista e álbum; preparar novamente o MP3 aplica alterações.
- Escolher uma correspondência do YouTube quando a primeira versão não for a desejada.
- Acompanhar processamento, cancelar e tentar novamente uma faixa que falhou.
- Ouvir MP3s, pausar, avançar, voltar, ajustar posição e volume.
- Consultar letras sincronizadas ou simples quando disponíveis no LRCLIB.

| Adicionar músicas | Editar metadados |
| --- | --- |
| ![Importar](docs/screenshots/import.png) | ![Editar faixa](docs/screenshots/edit-track.png) |

## Importações

| Entrada | Comportamento |
| --- | --- |
| `Artista - Título` | Uma faixa por linha; busca áudio no YouTube. |
| Link de vídeo do YouTube | Usa o vídeo informado. |
| Playlist do YouTube | Importa entradas disponíveis. |
| Playlist pública do Spotify | Lê nomes e artistas visíveis; busca áudio no YouTube. |
| CSV / texto | Alternativa para listas indisponíveis pela página pública. |
| Arquivos locais | Prepara uma cópia em MP3; preserva os originais. |

O Spotify **não fornece áudio ao aplicativo**. A página pública pode omitir entradas e não permite confirmar integralidade; o resultado informa quantas faixas foram encontradas. O núcleo também possui integração de API oficial para desenvolvedores, sujeita às restrições atuais do Spotify; essa opção não tem tela de login neste aplicativo.

CSV aceita vírgula ou ponto e vírgula, com cabeçalhos `Title`, `Artist`, `Album` (também reconhece `Track Name`, `Artist Name(s)` e `Album Name`). Exemplo:

```csv
Title,Artist,Album
Minha faixa,Meu artista,Minha playlist
```

## Integração com o Takedawn

Formato de músicas customizadas do Takedawn:

- Cada subpasta da raiz é um soundtrack; até **100 soundtracks** e **500 músicas por soundtrack** no jogo.
- Arquivos recebem números para preservar a ordem.
- Títulos no jogo têm limite de **31 caracteres Latin-1**. A biblioteca mantém os nomes completos.
- Configurações ficam em `%APPDATA%\Takedawn\settings.toml`. A seleção automática exige **0.2.1+**; o aplicativo detecta a versão antes de configurar esse recurso.
- Também é possível selecionar a raiz em **F1 → Audio → Custom music folder**.

As letras ficam no gerenciador e em `.lrc` / `.txt`. **Não há integração de letras na tela do jogo.** A versão 0.2.0 exige selecionar a playlist no menu a cada execução. Música de split screen e outros modos depende das opções e suporte do próprio jogo.

A sincronização identifica seus arquivos por manifesto e hash, preservando arquivos desconhecidos ou modificados manualmente. Backups ficam em `PlaylistStudio-Backups`, ao lado da raiz de músicas. ROMs e saves não são alterados.

Depois de remover ou editar faixas, use **Carregar playlist** novamente para atualizar a seleção. Remover uma playlist do catálogo não apaga automaticamente cópias já exportadas ou arquivos originais. Arquivos de terceiros ou alterados manualmente continuam preservados.

Referências: [músicas customizadas](https://github.com/xenophobentx/takedawn/blob/v0.3.1/docs/custom-music.md), [seleção na inicialização](https://github.com/xenophobentx/takedawn/blob/v0.3.1/src/game/soundtrack_start.h) e [atualizações oficiais](https://github.com/xenophobentx/takedawn/releases).

## Portable e privacidade

Mova estas pastas junto com o executável:

```text
BurnoutPlaylistStudio.exe
data/       biblioteca, áudio preparado, letras e fila
tools/      ferramentas e manifesto de verificação
```

Não exige administrador nem instala dependências globalmente. O primeiro preparo inclui downloads grandes, especialmente FFmpeg. Músicas exportadas ficam na pasta escolhida e não se movem junto com o EXE.

Buscas e áudio acessam YouTube; letras acessam LRCLIB; importação Spotify acessa Spotify; ferramentas vêm de GitHub e Gyan. Não há telemetria própria. Use conteúdo que você tenha autorização para baixar e utilizar.

## Limites e problemas comuns

- **Vídeo indisponível / login / bloqueio:** escolha outra versão ou importe arquivo local. O aplicativo não contorna DRM, CAPTCHA ou restrições de conta.
- **Correspondência errada:** use **Escolher versão** e confira título, artista e duração.
- **Letra ausente:** o MP3 continua funcionando. Nem toda música possui letra ou timestamps no provedor.
- **Spotify parcial:** importe os nomes por CSV/texto.
- **Jogo não mostra músicas:** confira a raiz, selecione o soundtrack no EA GAMES TRAX e reinicie o jogo.
- **Ferramentas não preparam:** confira internet e permissão de gravação. Downloads são verificados antes da execução.
- **Limites por faixa:** até 30 minutos e aproximadamente 250 MB por download; o jogo possui os limites acima.

## Desenvolvimento

Requer Windows e **.NET SDK 10**:

```powershell
dotnet run --project Tests/Burnout3.PlaylistStudio.Tests.csproj -c Release
./build.ps1
```

O resultado self-contained `win-x64` fica em `dist/`. `App/` contém WinForms; `Core/` contém biblioteca, importações, fila e instalação. Os testes usam pastas temporárias e configuração isolada. Para abrir uma biblioteca de teste:

```powershell
./dist/BurnoutPlaylistStudio.exe --data C:/Demo/Studio/data --config C:/Demo/Studio/settings.toml
```

Configure a pasta de músicas separadamente nessa biblioteca de teste.

## Licença

Aplicativo sob [MIT](LICENSE). Veja [avisos de terceiros](THIRD_PARTY_NOTICES.md). Projeto independente, sem afiliação à EA, Criterion, Spotify, YouTube ou equipe do Takedawn. Não inclui jogo, ROM, músicas comerciais ou letras de terceiros.
