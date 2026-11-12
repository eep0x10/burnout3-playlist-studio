using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace Burnout3.PlaylistStudio.Core;
internal static class PssAudioHandles
{
    [StructLayout(LayoutKind.Explicit,Size=136)]private struct Entry
    {[FieldOffset(0)]public IntPtr Handle;[FieldOffset(56)]public ushort TypeLength;[FieldOffset(64)]public IntPtr Type;}
    internal static IReadOnlyList<string> Read(int pid,IEnumerable<string> allowed)=>ReadCore(pid,allowed,false);
    internal static IReadOnlyList<string> ReadSyntheticChild(int pid,IEnumerable<string> allowed)=>ReadCore(pid,allowed,true);
    private static IReadOnlyList<string> ReadCore(int pid,IEnumerable<string> allowed,bool synthetic)
    {
        if(!OperatingSystem.IsWindows()||IntPtr.Size!=8)throw new PlatformNotSupportedException("PSS requer Windows x64.");
        var whitelist=allowed.Select(Path.GetFullPath).Where(p=>Path.GetExtension(p).Equals(".mp3",StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(whitelist.Count==0)return [];
        IntPtr process=pid==Environment.ProcessId?new(-1):OpenProcess(0x440,false,pid);
        if(process==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr snapshot=IntPtr.Zero,marker=IntPtr.Zero;var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Validate the acquired process HANDLE, eliminating PID-reuse name races.
            var executable=new StringBuilder(32768);uint size=(uint)executable.Capacity;
            if(pid!=Environment.ProcessId&&(!QueryFullProcessImageName(process,0,executable,ref size)||!(synthetic?executable.ToString().Equals(Environment.ProcessPath,StringComparison.OrdinalIgnoreCase):Path.GetFileName(executable.ToString()).Equals("Takedawn.exe",StringComparison.OrdinalIgnoreCase))))throw new InvalidOperationException("Somente Takedawn pode ser observado.");
            uint error=PssCaptureSnapshot(process,0x14,0,out snapshot);if(error!=0)throw new Win32Exception((int)error);
            error=PssWalkMarkerCreate(IntPtr.Zero,out marker);if(error!=0)throw new Win32Exception((int)error);
            while(true)
            {
                error=PssWalkSnapshot(snapshot,2,marker,out Entry entry,136);if(error==259)break;if(error!=0)throw new Win32Exception((int)error);
                if(entry.Type==IntPtr.Zero||entry.TypeLength>32||Marshal.PtrToStringUni(entry.Type,entry.TypeLength/2)?.TrimEnd('\0')!="File")continue;
                IntPtr duplicate=IntPtr.Zero;
                try
                {
                    if(!DuplicateHandle(process,entry.Handle,new(-1),out duplicate,0,false,2)||GetFileType(duplicate)!=1)continue;
                    var name=new StringBuilder(32768);uint count=GetFinalPathNameByHandle(duplicate,name,(uint)name.Capacity,8);
                    if(count==0||count>=name.Capacity)continue;
                    string path=name.ToString();if(path.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))path=@"\\"+path[8..];else if(path.StartsWith(@"\\?\"))path=path[4..];
                    if(whitelist.TryGetValue(path,out var exact))result.Add(exact);
                }
                finally{if(duplicate!=IntPtr.Zero)CloseHandle(duplicate);}
            }
        }
        finally{if(marker!=IntPtr.Zero)PssWalkMarkerFree(marker);if(snapshot!=IntPtr.Zero)PssFreeSnapshot(new(-1),snapshot);if(pid!=Environment.ProcessId)CloseHandle(process);}
        return result.ToArray();
    }
    [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref uint size);
    [DllImport("kernel32.dll")]private static extern uint PssCaptureSnapshot(IntPtr process,uint flags,uint context,out IntPtr snapshot);
    [DllImport("kernel32.dll")]private static extern uint PssWalkMarkerCreate(IntPtr allocator,out IntPtr marker);
    [DllImport("kernel32.dll")]private static extern uint PssWalkSnapshot(IntPtr snapshot,int info,IntPtr marker,out Entry entry,uint length);
    [DllImport("kernel32.dll")]private static extern uint PssWalkMarkerFree(IntPtr marker);
    [DllImport("kernel32.dll")]private static extern uint PssFreeSnapshot(IntPtr process,IntPtr snapshot);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool DuplicateHandle(IntPtr source,IntPtr handle,IntPtr target,out IntPtr duplicate,uint access,bool inherit,uint options);
    [DllImport("kernel32.dll")]private static extern uint GetFileType(IntPtr file);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern uint GetFinalPathNameByHandle(IntPtr file,StringBuilder name,uint size,uint flags);
}
