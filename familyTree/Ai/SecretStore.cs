using System.Runtime.InteropServices;
using System.Text;

namespace Mycelia.Ai;

/// <summary>
/// Encrypts API keys, MCP header values, environment values and the Assistant's conversations with Windows DPAPI
/// (current user), as multifariousStudio does.  Called through crypt32.dll so no NuGet package is needed.  Stored
/// values carry the prefix "dpapi:"; anything without it is read as plain text.
/// </summary>
public static class SecretStore
{
    private const string Prefix = "dpapi:";
    private const int UiForbidden = 0x1;

    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
            return string.Empty;
        if (IsProtected(plain))
            return plain;
        byte[] data = Encoding.UTF8.GetBytes(plain);
        var input = new DataBlob();
        var output = new DataBlob();
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            input.Size = data.Length;
            input.Data = handle.AddrOfPinnedObject();
            if (!CryptProtectData(ref input, "Mycelia", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output))
                throw new InvalidOperationException("Windows couldn't encrypt the value (error " + Marshal.GetLastWin32Error() + ").");
            byte[] result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return Prefix + Convert.ToBase64String(result);
        }
        finally
        {
            handle.Free();
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    /// <summary>The plain value, or an empty string when it can't be decrypted (another user or computer).</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return string.Empty;
        if (!IsProtected(stored))
            return stored;
        byte[] data;
        try { data = Convert.FromBase64String(stored[Prefix.Length..]); }
        catch (FormatException) { return string.Empty; }
        var input = new DataBlob();
        var output = new DataBlob();
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            input.Size = data.Length;
            input.Data = handle.AddrOfPinnedObject();
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output))
                return string.Empty;
            byte[] result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return Encoding.UTF8.GetString(result);
        }
        finally
        {
            handle.Free();
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    public static Dictionary<string, string> ProtectValues(IReadOnlyDictionary<string, string> values) =>
        values.ToDictionary(p => p.Key, p => Protect(p.Value));

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
