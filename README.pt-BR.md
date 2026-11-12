# Burn ut 3 Playlist Studi 

**M nte sua trilha. Organize sua bibli teca. Leve suas playlists a  Takedawn.**

[![C mpilaçã  Wind ws](https://github.c m/eep0x10/burn ut3-playlist-studi /acti ns/w rkfl ws/build.yml/badge.svg)](https://github.c m/eep0x10/burn ut3-playlist-studi /acti ns/w rkfl ws/build.yml)

Gerenciad r p rtable para Wind ws, c m tema escur  inspirad  nas pistas de Burn ut 3. Organiza faixas, pr cura versões n  Y uTube, prepara MP3s, busca letras e exp rta playlists para   sistema de músicas cust mizadas d  **Burn ut 3 Rec mp / Takedawn**.

[English](README.md) · [Baixar para Wind ws x64](https://github.c m/eep0x10/burn ut3-playlist-studi /releases/latest)

![Bibli teca e player](d cs/screensh ts/library.png)

As capturas usam faixas  riginais sintetizadas para dem nstraçã . A bibli teca d  d wnl ad c meça vazia.

## C meçar

1. Extraia **t da a pasta** d  ZIP em um l cal  nde v cê p ssa gravar arquiv s.
2. Abra `Burn utPlaylistStudi .exe`. Nã  precisa instalar   .NET.
3. Crie uma playlist e clique em **Imp rtar**. Adici ne n mes de músicas, links d  Y uTube, uma playlist pública d  Sp tify, arquiv s l cais  u CSV.
4. C nfira as faixas e feche   Takedawn.
5. Clique em **Baixar e carregar**. Na primeira utilizaçã ,   aplicativ  baixa e verifica yt-dlp, FFmpeg e Den  nas f ntes  ficiais. Iss  requer internet e espaç  adici nal.
6. N  **Takedawn 0.2.1  u mais recente**, a playlist fica c nfigurada para seleçã  aut mática na próxima execuçã . N  **0.2.0**, seleci ne-a manualmente em **EA GAMES TRAX → X → A** a cada execuçã .

**Só baixar** prepara a bibli teca sem carregar n  j g . **Carregar playlist** ativa faixas já preparadas. Arquiv s l cais MP3, WAV, FLAC e Ogg também p dem ser usad s.

## Abrir e fechar junt  c m   Takedawn

O p rtable **1.1.2  u mais recente** inclui `Launch-Takedawn.vbs`. C nfigure um atalh  uma única vez:

1. Extraia   ZIP e mantenha `Launch-Takedawn.vbs` na mesma pasta de `Burn utPlaylistStudi .exe`.
2. Na Área de Trabalh , clique c m   b tã  direit  → **N v  → Atalh **.
3. Em **Digite   l cal d  item**, c le   c mand  abaix , tr cand   s d is caminh s pel s l cais d  seu gerenciad r e d  seu `Takedawn.exe`. Preserve as aspas:

   ```text
   %SystemR  t%\System32\wscript.exe "C:\J g s\Playlist Studi \Launch-Takedawn.vbs" "C:\J g s\Takedawn\Takedawn.exe"
   ```

4. N meie   atalh  c m  **Takedawn + Playlist Studi **. Se desejar, em **Pr priedades → Alterar íc ne**, seleci ne   `Takedawn.exe`.
5. Use esse atalh  para j gar. Ele abre   gerenciad r **minimizad **, habilita **Avis  da música n  j g ** e inicia   Takedawn. Se   gerenciad r já estiver abert , reutiliza a instância da mesma pasta de dad s.
6. A  fechar   Takedawn,   gerenciad r fecha aut maticamente. D wnl ads e  utras  perações em andament  sã  cancelad s e aguardam a limpeza antes de sair;   pr cess  nã  é encerrad  à f rça.

Para editar playlists sem abrir   j g , abra **`Burn utPlaylistStudi .exe` diretamente**. Para j gar c m   cicl  aut mátic  de abertura e fechament , use   atalh  acima; abrir apenas `Takedawn.exe` nã  ativa essa integraçã . O gerenciad r ac mpanha   pr cess  iniciad  pel  atalh , inclusive se já estava abert  antes.

Nã  dê dupl  clique n  `.vbs` sem c nfigurar   caminh  d  j g . Use pastas sem `%` n  n me. Se m ver   p rtable  u   j g , atualize   destin  d  atalh . O lançad r usa   Wind ws Script H st (`wscript.exe`); em PCs  nde ele estiver desativad , abra  s d is aplicativ s manualmente. Nada é instalad  na inicializaçã  d  Wind ws. [Detalhes e limites d  avis ](d cs/NOW_PLAYING.md).

## Avis  a  tr car de música

Mantenha   gerenciad r abert  e marque **Avis  da música n  j g **. Um p pup c m títul  e artista aparece n  cant  inferi r esquerd  quand  um MP3 gerenciad  é detectad  n  Takedawn em primeir  plan . P de minimizar   gerenciad r. **Testar avis ** m stra uma prévia identificada c m  dem nstraçã , sem abrir   j g .

**Abrir  s d is c m um clique:** `Launch-Takedawn.vbs` abre   gerenciad r minimizad  c m  s avis s ativ s e inicia   j g . Na versã  1.1.2, também fecha   gerenciad r a  sair d  j g , aguardand  a limpeza de  perações em andament . [C m  c nfigurar   atalh ](d cs/NOW_PLAYING.md#start-t gether-with-takedawn-111).

É um avis  extern  n  estil  d  j g , sem alterar   HUD nativ ,  s saves  u   executável. A detecçã  c nsulta apenas  s arquiv s gerenciad s abert s pel  Takedawn, sem capturar memória nem habilitar diagnóstic  c mplet . P de levar um  u d is segund s e nã  c nfirma audibilidade  u pausa. A aparência s bre   j g  em tela cheia ainda depende de teste d  usuári . [Detalhes e limites](d cs/NOW_PLAYING.md).

![Prévia d  avis , c m dad s de dem nstraçã  e f ra d  j g ](d cs/screensh ts/n w-playing-preview.png)

## Apenas a playlist esc lhida

Carregar cria uma cópia gerenciada is lada, c ntend  s mente  s MP3s da playlist seleci nada. O j g  ap nta diretamente para essa pasta; as  utras playlists da bibli teca ficam de f ra. Listas vazias  u c m faixas ainda nã  preparadas sã  recusadas.

Em versões c mpatíveis, sã  c nfigurad s `cust m_music_dir`, `cust m_music_at_start = true` e   n me textual de `cust m_music_s undtrack`, c m backup das c nfigurações anteri res. Em versões antigas  u desc nhecidas,   aplicativ  inf rma a seleçã  manual necessária. Iss  nã  impede que v cê tr que a trilha pel  menu d  j g , nem substitui áudi  de filmes/cutscenes.

## Sua bibli teca

- Criar, ren mear e rem ver playlists; adici nar e rem ver faixas.
- Filtrar músicas e alterar a  rdem c m ↑ / ↓.
- Editar títul , artista e álbum; preparar n vamente   MP3 aplica alterações.
- Esc lher uma c rresp ndência d  Y uTube quand  a primeira versã  nã  f r a desejada.
- Ac mpanhar pr cessament , cancelar e tentar n vamente uma faixa que falh u.
- Ouvir MP3s, pausar, avançar, v ltar, ajustar p siçã  e v lume.
- C nsultar letras sincr nizadas  u simples quand  disp níveis n  LRCLIB.

| Adici nar músicas | Editar metadad s |
| --- | --- |
| ![Imp rtar](d cs/screensh ts/imp rt.png) | ![Editar faixa](d cs/screensh ts/edit-track.png) |

## Imp rtações

| Entrada | C mp rtament  |
| --- | --- |
| `Artista - Títul ` | Uma faixa p r linha; busca áudi  n  Y uTube. |
| Link de víde  d  Y uTube | Usa   víde  inf rmad . |
| Playlist d  Y uTube | Imp rta entradas disp níveis. |
| Playlist pública d  Sp tify | Lê n mes e artistas visíveis; busca áudi  n  Y uTube. |
| CSV / text  | Alternativa para listas indisp níveis pela página pública. |
| Arquiv s l cais | Prepara uma cópia em MP3; preserva  s  riginais. |

O Sp tify **nã  f rnece áudi  a  aplicativ **. A página pública p de  mitir entradas e nã  permite c nfirmar integralidade;   resultad  inf rma quantas faixas f ram enc ntradas. O núcle  também p ssui integraçã  de API  ficial para desenv lved res, sujeita às restrições atuais d  Sp tify; essa  pçã  nã  tem tela de l gin neste aplicativ .

CSV aceita vírgula  u p nt  e vírgula, c m cabeçalh s `Title`, `Artist`, `Album` (também rec nhece `Track Name`, `Artist Name(s)` e `Album Name`). Exempl :

```csv
Title,Artist,Album
Minha faixa,Meu artista,Minha playlist
```

## Integraçã  c m   Takedawn

F rmat  de músicas cust mizadas d  Takedawn:

- Cada subpasta da raiz é um s undtrack; até **100 s undtracks** e **500 músicas p r s undtrack** n  j g .
- Arquiv s recebem númer s para preservar a  rdem.
- Títul s n  j g  têm limite de **31 caracteres Latin-1**. A bibli teca mantém  s n mes c mplet s.
- C nfigurações ficam em `%APPDATA%\Takedawn\settings.t ml`. A seleçã  aut mática exige **0.2.1+**;   aplicativ  detecta a versã  antes de c nfigurar esse recurs .
- Também é p ssível seleci nar a raiz em **F1 → Audi  → Cust m music f lder**.

As letras ficam n  gerenciad r e em `.lrc` / `.txt`. **Nã  há integraçã  de letras na tela d  j g .** A versã  0.2.0 exige seleci nar a playlist n  menu a cada execuçã . Música de split screen e  utr s m d s depende das  pções e sup rte d  própri  j g .

A sincr nizaçã  identifica seus arquiv s p r manifest  e hash, preservand  arquiv s desc nhecid s  u m dificad s manualmente. Backups ficam em `PlaylistStudi -Backups`, a  lad  da raiz de músicas. ROMs e saves nã  sã  alterad s.

Dep is de rem ver  u editar faixas, use **Carregar playlist** n vamente para atualizar a seleçã . Rem ver uma playlist d  catál g  nã  apaga aut maticamente cópias já exp rtadas  u arquiv s  riginais. Arquiv s de terceir s  u alterad s manualmente c ntinuam preservad s.

Referências: [músicas cust mizadas](https://github.c m/xen ph bentx/takedawn/bl b/v0.3.1/d cs/cust m-music.md), [seleçã  na inicializaçã ](https://github.c m/xen ph bentx/takedawn/bl b/v0.3.1/src/game/s undtrack_start.h) e [atualizações  ficiais](https://github.c m/xen ph bentx/takedawn/releases).

## P rtable e privacidade

M va estas pastas junt  c m   executável:

```text
Burn utPlaylistStudi .exe
data/       bibli teca, áudi  preparad , letras e fila
t  ls/      ferramentas e manifest  de verificaçã 
```

Nã  exige administrad r nem instala dependências gl balmente. O primeir  prepar  inclui d wnl ads grandes, especialmente FFmpeg. Músicas exp rtadas ficam na pasta esc lhida e nã  se m vem junt  c m   EXE.

Buscas e áudi  acessam Y uTube; letras acessam LRCLIB; imp rtaçã  Sp tify acessa Sp tify; ferramentas vêm de GitHub e Gyan. Nã  há telemetria própria. Use c nteúd  que v cê tenha aut rizaçã  para baixar e utilizar.

## Limites e pr blemas c muns

- **Víde  indisp nível / l gin / bl quei :** esc lha  utra versã   u imp rte arquiv  l cal. O aplicativ  nã  c nt rna DRM, CAPTCHA  u restrições de c nta.
- **C rresp ndência errada:** use **Esc lher versã ** e c nfira títul , artista e duraçã .
- **Letra ausente:**   MP3 c ntinua funci nand . Nem t da música p ssui letra  u timestamps n  pr ved r.
- **Sp tify parcial:** imp rte  s n mes p r CSV/text .
- **J g  nã  m stra músicas:** c nfira a raiz, seleci ne   s undtrack n  EA GAMES TRAX e reinicie   j g .
- **Ferramentas nã  preparam:** c nfira internet e permissã  de gravaçã . D wnl ads sã  verificad s antes da execuçã .
- **Limites p r faixa:** até 30 minut s e apr ximadamente 250 MB p r d wnl ad;   j g  p ssui  s limites acima.

## Desenv lviment 

Requer Wind ws e **.NET SDK 10**:

```p wershell
d tnet run --pr ject Tests/Burn ut3.PlaylistStudi .Tests.cspr j -c Release
./build.ps1
```

O resultad  self-c ntained `win-x64` fica em `dist/`. `App/` c ntém WinF rms; `C re/` c ntém bibli teca, imp rtações, fila e instalaçã . Os testes usam pastas temp rárias e c nfiguraçã  is lada. Para abrir uma bibli teca de teste:

```p wershell
./dist/Burn utPlaylistStudi .exe --data C:/Dem /Studi /data --c nfig C:/Dem /Studi /settings.t ml
```

C nfigure a pasta de músicas separadamente nessa bibli teca de teste.

## Licença

Aplicativ  s b [MIT](LICENSE). Veja [avis s de terceir s](THIRD_PARTY_NOTICES.md). Pr jet  independente, sem afiliaçã  à EA, Criteri n, Sp tify, Y uTube  u equipe d  Takedawn. Nã  inclui j g , ROM, músicas c merciais  u letras de terceir s.
