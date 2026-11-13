using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace StatusLight.Core.Hook;

/// <summary>
/// Rename-over-existing that succeeds while another process has the destination open with
/// FILE_SHARE_DELETE (the app's reader does), matching rename(2) on POSIX. File.Move(overwrite)
/// uses MoveFileEx, which fails with access denied in that case. Needs Windows 10 1709+ on NTFS;
/// returns false anywhere it is unsupported or fails so the caller can fall back.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsReplace
{
    private const uint DELETE = 0x00010000;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint FILE_SHARE_READ_WRITE_DELETE = 0x7;
    private const uint OPEN_EXISTING = 3;
    private const int FileRenameInfoEx = 22;
    private const uint FILE_RENAME_FLAG_REPLACE_IF_EXISTS = 0x1;
    private const uint FILE_RENAME_FLAG_POSIX_SEMANTICS = 0x2;

    public static unsafe bool TryReplace(string source, string destination)
    {
        try
        {
            using var handle = CreateFileW(Path.GetFullPath(source), DELETE | SYNCHRONIZE,
                FILE_SHARE_READ_WRITE_DELETE, 0, OPEN_EXISTING, 0, 0);
            if (handle.IsInvalid) return false;

            // FILE_RENAME_INFO: Flags (DWORD, padded to pointer size), RootDirectory (HANDLE),
            // FileNameLength (DWORD, bytes), FileName (WCHAR[]).
            var name = Path.GetFullPath(destination);
            var lengthOffset = 2 * IntPtr.Size;
            var nameOffset = lengthOffset + sizeof(uint);
            var structSize = (nameOffset + sizeof(char) + IntPtr.Size - 1) / IntPtr.Size * IntPtr.Size;
            var nameBytes = name.Length * sizeof(char);
            var buffer = new byte[structSize + nameBytes];
            fixed (byte* p = buffer)
            fixed (char* n = name)
            {
                *(uint*)p = FILE_RENAME_FLAG_REPLACE_IF_EXISTS | FILE_RENAME_FLAG_POSIX_SEMANTICS;
                *(uint*)(p + lengthOffset) = (uint)nameBytes;
                Buffer.MemoryCopy(n, p + nameOffset, nameBytes, nameBytes);
                return SetFileInformationByHandle(handle, FileRenameInfoEx, p, (uint)buffer.Length);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformationByHandle(SafeFileHandle file, int infoClass,
        void* info, uint bufferSize);
}
