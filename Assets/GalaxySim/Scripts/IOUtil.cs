using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;

static class IOUtil
{
    public static void StructToBytes(object structObj, byte[] buf)
    {
        int size = Marshal.SizeOf(structObj);
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structObj, buffer, false);
            //byte[] bytes = new byte[size];
            Marshal.Copy(buffer, buf, 0, size);
            //return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
    
    public static object BytesToStruct(byte[] bytes, Type structType)
    {
        int size = Marshal.SizeOf(structType);
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(bytes, 0, buffer, size);
            return Marshal.PtrToStructure(buffer, structType);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static void SaveToFile<T>(string path, T[] data) where T : struct
    {
        var file = File.Create(path);
        file.Write(BitConverter.GetBytes(data.Length), 0, 4);
        if (data.Length != 0)
        {
            int size = Marshal.SizeOf(data[0]);
            byte[] buf = new byte[size];
            foreach (var d in data)
            {
                StructToBytes(d, buf);
                file.Write(buf, 0, buf.Length);
            }
        }
        file.Flush();
        file.Close();
    }

    public static object[] LoadFromFile(string path, Type structType)
    {
        var file = File.OpenRead(path);
        int size = Marshal.SizeOf(structType);
        byte[] lenBuf = new byte[4];
        file.Read(lenBuf, 0, 4);
        int len = BitConverter.ToInt32(lenBuf, 0);

        byte[] buf = new byte[size];
        object[] res = new object[len];
        for (int i = 0; i < len; i++)
        {
            file.Read(buf, 0, size);
            res[i] = BytesToStruct(buf, structType);
        }
        file.Close();
        return res;
    }
}
