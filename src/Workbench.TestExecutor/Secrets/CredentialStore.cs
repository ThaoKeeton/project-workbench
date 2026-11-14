using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace Workbench.TestExecutor;

public static class CredentialTargets
{
    public const string OpenAi = "ProjectWorkbench/com.projectworkbench.test-executor/openai";
    public const string Linear = "ProjectWorkbench/com.projectworkbench.test-executor/linear";
}

public interface IApiKeyStore
{
    string? Read(string target);
}

public interface ICredentialBlobReader
{
    byte[]? Read(string target);
}

public sealed class WindowsCredentialStore : IApiKeyStore
{
    private readonly ICredentialBlobReader _reader;

    public WindowsCredentialStore() : this(new Advapi32CredentialReader())
    {
    }

    public WindowsCredentialStore(ICredentialBlobReader reader) => _reader = reader;

    public string? Read(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > 256 || target.Any(char.IsControl)) return null;
        var blob = _reader.Read(target);
        if (blob is not { Length: > 1 } || blob.Length % 2 != 0) return null;
        var text = Encoding.Unicode.GetString(blob).TrimEnd('\0').Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

public sealed partial class Advapi32CredentialReader : ICredentialBlobReader
{
    public byte[]? Read(string target)
    {
        if (!OperatingSystem.IsWindows()) return null;
        return ReadWindows(target);
    }

    [ExcludeFromCodeCoverage(Justification = "P/Invoke runs only on Windows and is covered by the credential-manager contract, not by Linux unit tests.")]
    private static byte[]? ReadWindows(string target)
    {
        if (!CredRead(target, 1, 0, out var handle)) return null;
        try
        {
            var native = Marshal.PtrToStructure<NativeCredential>(handle);
            if (native.CredentialBlob == IntPtr.Zero || native.CredentialBlobSize <= 0) return null;
            var blob = new byte[native.CredentialBlobSize];
            Marshal.Copy(native.CredentialBlob, blob, 0, blob.Length);
            return blob;
        }
        finally
        {
            CredFree(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr credential);
}
