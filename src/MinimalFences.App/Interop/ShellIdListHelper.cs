using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MinimalFences.Interop;

internal static class ShellIdListHelper
{
    public static List<string> GetPathsFromCida(MemoryStream stream)
    {
        var result = new List<string>();
        byte[] bytes = stream.ToArray();
        if (bytes.Length < 8) return result;

        GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            IntPtr ptr = handle.AddrOfPinnedObject();
            uint cidl = (uint)Marshal.ReadInt32(ptr, 0);
            // Use 64-bit arithmetic: a bogus count must not wrap around and pass the size check.
            long headerSize = 8L + cidl * 4L;
            if (cidl == 0 || bytes.Length < headerSize) return result;
            // Offsets come from another process; one pointing outside the buffer would make the shell read
            // arbitrary memory. An ID list needs at least its 2-byte terminator.
            bool ValidOffset(int offset) => offset >= headerSize && offset <= bytes.Length - 2;

            int folderOffset = Marshal.ReadInt32(ptr, 4);
            if (!ValidOffset(folderOffset)) return result;
            IntPtr folderPidl = IntPtr.Add(ptr, folderOffset);

            var pathBuffer = new StringBuilder(1024);

            for (int i = 1; i <= cidl; i++)
            {
                int itemOffset = Marshal.ReadInt32(ptr, 4 * (i + 1));
                if (!ValidOffset(itemOffset)) continue;
                IntPtr itemPidl = IntPtr.Add(ptr, itemOffset);

                IntPtr combined = NativeMethods.ILCombine(folderPidl, itemPidl);
                if (combined != IntPtr.Zero)
                {
                    pathBuffer.Clear();
                    if (NativeMethods.SHGetPathFromIDListW(combined, pathBuffer))
                    {
                        string p = pathBuffer.ToString();
                        if (!string.IsNullOrWhiteSpace(p)) result.Add(p);
                    }
                    else if (NativeMethods.SHGetNameFromIDList(combined, NativeMethods.SigdnDesktopAbsoluteParsing, out string parsingName) == 0 && !string.IsNullOrWhiteSpace(parsingName))
                    {
                        result.Add(parsingName);
                    }
                    NativeMethods.ILFree(combined);
                }
            }
        }
        catch (Exception ex)
        {
            Services.Log.Write("Error parsing CIDA structure.", ex);
        }
        finally
        {
            handle.Free();
        }

        return result;
    }
}
