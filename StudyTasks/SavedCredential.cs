using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace StudyTasks;

internal sealed record SavedCredential(string UserName, string Password)
{
    private const uint Generic = 1;
    private const uint LocalMachine = 2;
    private const int NotFound = 1168;
    private static string Target(string profile) => "ChaoxingStudyTasks/AutoLogin/" + profile;

    internal static bool Exists(string profile)
    {
        if (!CredRead(Target(profile), Generic, 0, out var pointer)) return false;
        CredFree(pointer);
        return true;
    }

    internal static SavedCredential? Read(string profile)
    {
        if (!CredRead(Target(profile), Generic, 0, out var pointer))
        {
            int error = Marshal.GetLastWin32Error();
            return error == NotFound ? null : throw new Win32Exception(error);
        }
        try
        {
            var data = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (string.IsNullOrEmpty(data.UserName) || data.Blob == IntPtr.Zero ||
                data.BlobSize == 0 || data.BlobSize > 2560 || data.BlobSize % 2 != 0)
                return null;
            var password = Marshal.PtrToStringUni(data.Blob, (int)data.BlobSize / 2);
            return string.IsNullOrEmpty(password) ? null : new SavedCredential(data.UserName, password);
        }
        finally { CredFree(pointer); }
    }

    internal void Save(string profile)
    {
        var blob = Marshal.StringToCoTaskMemUni(Password);
        try
        {
            var credential = new NativeCredential
            {
                Type = Generic, TargetName = Target(profile), UserName = UserName,
                Blob = blob, BlobSize = (uint)Encoding.Unicode.GetByteCount(Password), Persist = LocalMachine
            };
            if (!CredWrite(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }

    internal static void Remove(string profile)
    {
        if (CredDelete(Target(profile), Generic, 0)) return;
        int error = Marshal.GetLastWin32Error();
        if (error != NotFound) throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        internal uint Flags;
        internal uint Type;
        internal string? TargetName;
        internal string? Comment;
        internal FILETIME LastWritten;
        internal uint BlobSize;
        internal IntPtr Blob;
        internal uint Persist;
        internal uint AttributeCount;
        internal IntPtr Attributes;
        internal string? TargetAlias;
        internal string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
