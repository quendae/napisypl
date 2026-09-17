using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NapisyPL.Core.Security;

public sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    private const int CryptprotectUiForbidden = 0x1;

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        EnsureWindows();
        return Transform(plaintext, protect: true);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        EnsureWindows();
        return Transform(ciphertext, protect: false);
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputPtr = IntPtr.Zero;
        DataBlob output = default;
        try
        {
            inputPtr = Marshal.AllocHGlobal(Math.Max(input.Length, 1));
            if (input.Length > 0)
                Marshal.Copy(input, 0, inputPtr, input.Length);
            var inputBlob = new DataBlob { Size = input.Length, Data = inputPtr };

            var ok = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output);

            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var result = new byte[output.Size];
            if (output.Size > 0)
                Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            if (inputPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(inputPtr);
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Zapamiętywanie kluczy API używa Windows DPAPI.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
