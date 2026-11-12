using System.Diagnostics;

using System.Text;

using System.Text.Json;

using System.Text.RegularExpressions;



namespace Burnout3.PlaylistStudio.Core;



public sealed partial class PlaylistService

{

    private sealed class InstallManifest

    {

        public int Version{get;set;}=1;

        public Guid PlaylistId{get;set;}

        public string PlaylistName{get;set;}="";

        public Dictionary<string,ManagedFile> Files{get;set;}=[];

    }

    private sealed record ManagedFile(Guid TrackId,string Sha256);

    private sealed record InstallCopy(string Source,string Name,Guid TrackId,string Hash,bool Audio);

    public Task<InstallResult> InstallPlaylistAsync(Guid playlistId,string?musicRoot=null,CancellationToken cancellationToken=default)

        =>Task.Run(()=>InstallPlaylist(playlistId,musicRoot,cancellationToken),cancellationToken);

    private InstallResult InstallPlaylist(Guid playlistId,string?musicRoot,CancellationToken token,bool updateMusicRoot=true)

    {

        EnsureTakedawnClosed();
        var playlist=State(d=>Snapshot(Find(d,playlistId)));string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(musicRoot??State(d=>d.TakedawnRoot)));

        Directory.CreateDirectory(root);SafeFiles.Within(root,Path.Combine(root,".probe"));

        using var mutex=new Mutex(false,@"Local\BurnoutInstall-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))));bool locked=false;

        try

        {

            try{locked=mutex.WaitOne(TimeSpan.FromSeconds(15));}catch(AbandonedMutexException){locked=true;}if(!locked)throw new IOException("Outra instalação está em andamento.");

            token.ThrowIfCancellationRequested();var warnings=new List<string>();

            string folder=SafeFiles.Within(root,Path.Combine(root,SafeFiles.Name(GameText(playlist.Name),20)+" ["+playlist.Id.ToString("N")[..8]+"]"));InstallManifest old=new(){PlaylistId=playlist.Id,PlaylistName=playlist.Name};

            foreach(string candidate in Directory.EnumerateDirectories(root))

            {

                string mf=SafeFiles.Within(root,Path.Combine(candidate,".playlist-studio.json"));if(!File.Exists(mf))continue;

                InstallManifest? found;try{found=JsonSerializer.Deserialize<InstallManifest>(File.ReadAllBytes(mf),Json);}catch(JsonException){continue;}

                if(found?.PlaylistId==playlist.Id){folder=candidate;old=found;break;}

            }

            if(old.Version!=1||old.PlaylistId!=playlist.Id)throw new InvalidDataException("Manifesto de instalação incompatível.");

            foreach(var pair in old.Files)ValidateManagedName(folder,pair.Key,pair.Value.TrackId);

            if(Directory.Exists(folder)&&!File.Exists(Path.Combine(folder,".playlist-studio.json"))&&Directory.EnumerateFileSystemEntries(folder).Any())throw new IOException("Pasta de destino já contém arquivos não gerenciados; escolha outra playlist/nome.");

            if(!Directory.Exists(folder)&&Directory.EnumerateDirectories(root).Count()>=100)throw new IOException("Raiz Takedawn já tem 100 soundtracks/pastas.");

            Directory.CreateDirectory(folder);string manifestPath=SafeFiles.Within(root,Path.Combine(folder,".playlist-studio.json"));

            var manifest=new InstallManifest{PlaylistId=playlist.Id,PlaylistName=playlist.Name};var requestedIds=playlist.Tracks.Select(t=>t.Id).ToHashSet();var copies=new List<InstallCopy>();

            for(int index=0;index<playlist.Tracks.Count;index++)

            {

                token.ThrowIfCancellationRequested();var track=playlist.Tracks[index];

                if(track.Status!=TrackStatus.Downloaded||track.LocalPath==null||track.MetadataDirty)

                {

                    foreach(var pair in old.Files.Where(p=>p.Value.TrackId==track.Id))manifest.Files[pair.Key]=pair.Value;

                    warnings.Add($"{track.Title}: áudio ainda não pronto; eventual versão já instalada foi preservada.");continue;

                }

                string stem=$"{index+1:0000}_"+SafeFiles.Name(GameText(track.Title),55)+"_"+track.Id.ToString("N")[..8];

                foreach(string extension in new[]{"mp3","lrc","txt"})

                {

                    string source=extension=="mp3"?AudioPath(playlist.Id,track.Id):SidecarPath(playlist.Id,track.Id,extension);if(!File.Exists(source))continue;

                    string name=stem+"."+extension;ValidateManagedName(folder,name,track.Id);string hash=SafeFiles.Hash(source);copies.Add(new(source,name,track.Id,hash,extension=="mp3"));manifest.Files[name]=new(track.Id,hash);

                }

            }

            if(copies.Count==0&&old.Files.Count==0)throw new InvalidOperationException("Baixe/processe ao menos uma faixa antes de instalar.");

            string parent=Directory.GetParent(root)?.FullName??throw new IOException("Raiz de música inválida.");string taskId=DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N");

            string backupRoot=Path.Combine(parent,"PlaylistStudio-Backups"),backup=SafeFiles.Within(backupRoot,Path.Combine(backupRoot,taskId));Directory.CreateDirectory(backup);

            var prior=new Dictionary<string,string>();

            foreach(var file in old.Files)

            {

                string target=SafeFiles.Within(folder,Path.Combine(folder,file.Key));if(!File.Exists(target))continue;

                if(SafeFiles.Hash(target)!=file.Value.Sha256){warnings.Add(file.Key+": modificado pelo usuário; não será substituído ou removido.");manifest.Files.Remove(file.Key);continue;}

                string saved=SafeFiles.Within(backup,Path.Combine(backup,file.Key));File.Copy(target,saved);if(SafeFiles.Hash(saved)!=file.Value.Sha256)throw new IOException("Backup de instalação não conferiu.");prior[file.Key]=file.Value.Sha256;

            }

            if(File.Exists(manifestPath))File.Copy(manifestPath,Path.Combine(backup,".playlist-studio.json"));

            // Stage on the destination volume, outside the scanned soundtrack root, so File.Replace remains atomic across custom drives.
            string stageRoot=Path.Combine(parent,"."+Path.GetFileName(root)+".playliststudio-staging"),stage=SafeFiles.Within(stageRoot,Path.Combine(stageRoot,Guid.NewGuid().ToString("N")));Directory.CreateDirectory(stage);

            var changed=new List<InstallCopy>();int copied=0,skipped=0;

            try

            {

                foreach(var copy in copies)

                {

                    token.ThrowIfCancellationRequested();string dest=SafeFiles.Within(folder,Path.Combine(folder,copy.Name));

                    if(File.Exists(dest))

                    {

                        if(!old.Files.TryGetValue(copy.Name,out var owned)||SafeFiles.Hash(dest)!=owned.Sha256){manifest.Files.Remove(copy.Name);warnings.Add(copy.Name+": arquivo existente não gerenciado/modificado preservado.");continue;}

                        if(owned.Sha256==copy.Hash){if(copy.Audio)skipped++;continue;}

                    }

                    string temp=SafeFiles.Within(stage,Path.Combine(stage,copy.Name));File.Copy(copy.Source,temp);if(SafeFiles.Hash(temp)!=copy.Hash)throw new IOException("Cópia de áudio não conferiu.");

                }

                foreach(var copy in copies)

                {

                    string temp=SafeFiles.Within(stage,Path.Combine(stage,copy.Name));if(!File.Exists(temp))continue;token.ThrowIfCancellationRequested();string dest=SafeFiles.Within(folder,Path.Combine(folder,copy.Name));

                    if(File.Exists(dest)){if(!prior.TryGetValue(copy.Name,out var oldHash)||SafeFiles.Hash(dest)!=oldHash)throw new IOException("Arquivo mudou durante a instalação.");File.Replace(temp,dest,null);}else File.Move(temp,dest);

                    changed.Add(copy);if(copy.Audio)copied++;

                }

                token.ThrowIfCancellationRequested();SafeFiles.Write(manifestPath,JsonSerializer.SerializeToUtf8Bytes(manifest,Json));

            }

            catch

            {

                foreach(var copy in changed.AsEnumerable().Reverse())

                {

                    string dest=SafeFiles.Within(folder,Path.Combine(folder,copy.Name));if(!File.Exists(dest)||SafeFiles.Hash(dest)!=copy.Hash)continue;

                    string saved=Path.Combine(backup,copy.Name);if(prior.ContainsKey(copy.Name)&&File.Exists(saved)){string rollback=dest+".rollback-"+Guid.NewGuid().ToString("N");File.Copy(saved,rollback);File.Replace(rollback,dest,null);}else File.Delete(dest);

                }

                throw;

            }

            finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}

            foreach(var stale in old.Files.Where(p=>!manifest.Files.ContainsKey(p.Key)))

            {

                string target=SafeFiles.Within(folder,Path.Combine(folder,stale.Key));if(!File.Exists(target))continue;

                // Only superseded managed files or removed track IDs are eligible; unavailable current tracks are retained above.

                bool superseded=copies.Any(c=>c.TrackId==stale.Value.TrackId&&manifest.Files.ContainsKey(c.Name));

                if((!requestedIds.Contains(stale.Value.TrackId)||superseded)&&prior.ContainsKey(stale.Key)&&SafeFiles.Hash(target)==stale.Value.Sha256)VerifiedFileDeletion.Delete(target,stale.Value.Sha256,GameRunning);

            }

            State(d=>{if(updateMusicRoot)d.TakedawnRoot=root;d.Exports.RemoveAll(e=>e.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase));d.Exports.Add(new(playlistId,root,folder));foreach(var track in Find(d,playlistId).Tracks){var audio=manifest.Files.FirstOrDefault(p=>p.Value.TrackId==track.Id&&p.Key.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase));track.InstalledPath=audio.Key!=null?Path.Combine(folder,audio.Key):null;}return 0;},true);

            return new(root,folder,copied,skipped,warnings);

        }

        finally{if(locked)mutex.ReleaseMutex();}

    }

    private void EnsureTakedawnClosed()
    {if(GameRunning())throw new InvalidOperationException("Feche Takedawn antes de instalar ou alterar a configuração.");}
    private static string GameText(string text)=>new(text.Select(c=>c>=32&&c<=255?c:'_').ToArray());

    private static void ValidateManagedName(string folder,string name,Guid track)

    {

        if(name!=Path.GetFileName(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!Regex.IsMatch(name,@"^\d{4}_.+_"+track.ToString("N")[..8]+@"\.(mp3|lrc|txt)$",RegexOptions.IgnoreCase))throw new InvalidDataException("Nome inválido em manifesto de música gerenciada.");

        SafeFiles.Within(folder,Path.Combine(folder,name));

    }

    public ConfigResult ConfigureTakedawn(string?configPath=null,string?musicRoot=null,Version?version=null,string?soundtrack=null,bool activate=false)

    {

        string path=Path.GetFullPath(configPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Takedawn","settings.toml"));

        string root=Path.GetFullPath(musicRoot??State(d=>d.TakedawnRoot));if(!Directory.Exists(root))throw new DirectoryNotFoundException("Instale uma playlist ou escolha uma raiz de música existente.");

        EnsureTakedawnClosed();

        bool exists=File.Exists(path);byte[] original=exists?File.ReadAllBytes(path):[];string text=Encoding.UTF8.GetString(original).TrimStart('\uFEFF');

        if(text.Contains("\"\"\"",StringComparison.Ordinal)||text.Contains("'''",StringComparison.Ordinal))throw new InvalidDataException("TOML com strings multilinha: configure manualmente para preservar o arquivo.");

        if(Regex.IsMatch(text,@"(?m)^\s*\[\s*[""']audio[""']\s*\]" ) || Regex.IsMatch(text,@"(?m)^\s*(?:audio|[""']audio[""'])\s*(?:=|\.)") || Regex.IsMatch(text,@"(?m)^\s*[""']custom_music_dir[""']\s*="))
            throw new InvalidDataException("TOML com audio/chave não canônica: configure manualmente para preservar o arquivo.");
        string newline=text.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";var lines=text.Split(["\r\n","\n"],StringSplitOptions.None).ToList();

        var sections=lines.Select((line,index)=>(Line:line,Index:index)).Where(x=>Regex.IsMatch(x.Line,@"^\s*\[audio\]\s*(#.*)?$" )).ToArray();if(sections.Length>1)throw new InvalidDataException("TOML tem seções audio duplicadas; configure manualmente.");

        string Quote(string value)=>"\""+value.Replace('\\','/').Replace("\"","\\\"")+"\"";
        var values=new Dictionary<string,string>{{"custom_music_dir",Quote(root)}};
        if(activate&&version!=null&&version>=new Version(0,2,1))
        {
            if(string.IsNullOrWhiteSpace(soundtrack)||soundtrack.Length>31||soundtrack.Any(c=>c<32||c>126))throw new ArgumentException("Nome ativo deve ser ASCII e ter até31caracteres.");
            values["custom_music_at_start"]="true";values["custom_music_soundtrack"]=Quote(soundtrack);
        }
        if(sections.Length==0){if(lines.Count>0&&lines[^1].Length>0)lines.Add("");lines.Add("[audio]");foreach(var pair in values)lines.Add(pair.Key+" = "+pair.Value);}
        else
        {
            int start=sections[0].Index;
            foreach(var pair in values)
            {
                int end=lines.FindIndex(start+1,l=>Regex.IsMatch(l,@"^\s*\["));if(end<0)end=lines.Count;
                var keys=Enumerable.Range(start+1,end-start-1).Where(i=>Regex.IsMatch(lines[i],@"^\s*"+pair.Key+@"\s*=")).ToArray();if(keys.Length>1)throw new InvalidDataException("TOML tem chave audio duplicada: "+pair.Key);
                if(keys.Length==1)lines[keys[0]]=pair.Key+" = "+pair.Value;else lines.Insert(end,pair.Key+" = "+pair.Value);
            }
        }
        string? backup=null;Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if(exists){if(!File.ReadAllBytes(path).SequenceEqual(original))throw new IOException("Configuração mudou durante a leitura.");backup=path+".playliststudio-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".bak";File.WriteAllBytes(backup,original);}

        SafeFiles.Write(path,Encoding.UTF8.GetBytes(string.Join(newline,lines).TrimEnd()+newline),false);

        return new(path,backup,root);

    }

}
