using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Burnout3.PlaylistStudio.Core;
internal static class VerifiedFileDeletion
{
    // No FILE_SHARE_WRITE/DELETE: the name cannot be replaced while hashing.
    // Delete disposition targets that very handle, never a second path lookup.
    internal static bool Delete(string path,string expectedHash,Func<bool> gameRunning)
    {
        using var handle=CreateFile(path,0x80010000,1,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        using var stream=new FileStream(handle,FileAccess.Read);
        string actual=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));if(actual!=expectedHash)return false;
        if(gameRunning())throw new InvalidOperationException("Takedawn abriu durante limpeza; operação pendente.");
        var disposition=new FileDisposition{DeleteFile=true};
        if(!SetFileInformationByHandle(handle,4,ref disposition,4))throw new Win32Exception(Marshal.GetLastWin32Error());return true;
    }
    [StructLayout(LayoutKind.Sequential)]private struct FileDisposition{[MarshalAs(UnmanagedType.Bool)]public bool DeleteFile;}
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int informationClass,ref FileDisposition information,uint length);
}
