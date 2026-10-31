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
            if (cidl == 0 || bytes.Length < 8 + (cidl * 4)) return result;

            int folderOffset = Marshal.ReadInt32(ptr, 4);
            IntPtr folderPidl = IntPtr.Add(ptr, folderOffset);

            var pathBuffer = new StringBuilder(1024);

            for (int i = 1; i <= cidl; i++)
            {
                int itemOffset = Marshal.ReadInt32(ptr, 4 * (i + 1));
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
