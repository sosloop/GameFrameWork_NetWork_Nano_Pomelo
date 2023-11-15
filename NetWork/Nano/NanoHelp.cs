using System;
using System.Text;

namespace Nano
{
    public static class NanoHelp
    {
        public static string ToHex(this byte b)
        {
            return b.ToString("X2");
        }

        public static string ToHex(this byte[] bytes)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach (byte b in bytes)
            {
                stringBuilder.Append(b.ToString("X2"));
            }
            return stringBuilder.ToString();
        }
        
        public static string Utf8ToStr(this byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }
        
        public static void WriteLength(this byte[] bytes, int size)
        {
            bytes[1] = Convert.ToByte(size >> 16 & 0xFF);
            bytes[2] = Convert.ToByte(size >> 8 & 0xFF);
            bytes[3] = Convert.ToByte(size & 0xFF);
        }
        
    }
}