Option Explicit
Dim shell, files, folder, manager, game, gameFolder, gameError
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
folder = files.GetParentFolderName(WScript.ScriptFullName)
manager = files.BuildPath(folder, "BurnoutPlaylistStudio.exe")
If WScript.Arguments.Count <> 1 Then
    MsgBox "Use a shortcut passing the full path to Takedawn.exe.", 48, "Playlist Studio"
    WScript.Quit 1
End If
game = WScript.Arguments(0)
If Not files.FileExists(manager) Or Not files.FileExists(game) Then
    MsgBox "Playlist Studio or Takedawn was moved. Update this shortcut.", 48, "Playlist Studio"
    WScript.Quit 1
End If
If InStr(manager, "%") > 0 Or InStr(game, "%") > 0 Then
    MsgBox "Use folders without percent signs for this launcher.", 48, "Playlist Studio"
    WScript.Quit 1
End If
shell.CurrentDirectory = folder
shell.Run Quote(manager) & " --minimized --enable-song-notice", 7, False
gameFolder = files.GetParentFolderName(game)
shell.CurrentDirectory = gameFolder
On Error Resume Next
shell.Run Quote(game), 1, True
gameError = Err.Description
Err.Clear
On Error GoTo 0
shell.CurrentDirectory = folder
shell.Run Quote(manager) & " --close-after-game", 7, False
If Len(gameError) > 0 Then
    MsgBox "Could not start Takedawn: " & gameError, 48, "Playlist Studio"
    WScript.Quit 1
End If

Function Quote(value)
    Quote = Chr(34) & value & Chr(34)
End Function
