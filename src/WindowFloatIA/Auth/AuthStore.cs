using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace WindowFloatIA.Auth;

public sealed class AccountRegistration
{
    public string ClientId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string Label => Email + " · " + ClientId[^Math.Min(8, ClientId.Length)..];
    public override string ToString() => Label;
    public void ClearTokens() { AccessToken = RefreshToken = IdToken = Scope = ""; ExpiresAt = default; }
}
public sealed class AuthState
{
    public string HostId { get; set; } = "urn:uuid:" + Guid.NewGuid();
    public string? SelectedClientId { get; set; }
    public List<AccountRegistration> Accounts { get; set; } = new();
}
public interface IAuthStore { AuthState Load(); void Save(AuthState state); }

public sealed class AuthStore : IAuthStore
{
    private readonly string _path;
    public AuthStore(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowFloatIA", "oauth.dat");
    public AuthState Load()
    {
        if (!File.Exists(_path)) return new();
        var bytes = Transform(File.ReadAllBytes(_path), false);
        try { return JsonSerializer.Deserialize<AuthState>(bytes) ?? throw new InvalidDataException("Sessão inválida."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Save(AuthState state)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
        try
        {
            var encrypted = Transform(bytes, true);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllBytes(_path + ".tmp", encrypted);
            File.Move(_path + ".tmp", _path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                             : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Não foi possível acessar a sessão protegida do Windows.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
        }
    }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
