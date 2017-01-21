using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public class BinaryEncoding
    {

        /// <summary>
        /// Takes raw binary data to be stored and encodes it so it can be read back in.
        /// Meant to be called repeatedly to add more data to dst.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dst"></param>
        public static void encode(byte[] src, List<byte> dst)
        {
            // 24 = arbitrarily chosen escape byte
            // 0 = null space seperating data
            foreach (byte b in src)
            {
                if (b == 24 || b == 0)
                {
                    dst.Add(24);
                    if (b == 24)
                    {
                        dst.Add(36);
                    }
                    else if (b == 0)
                    {
                        dst.Add(49);
                    }
                }
                else
                {
                    dst.Add(b);
                }
            }
            dst.Add(0);
        }

        /// <summary>
        /// Takes stored binary data and reads all sets of raw data present.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dst"></param>
        /// <returns>A list of each of the byte arrays originally saved.</returns>
        public static List<byte[]> decode(byte[] src)
        {
            List<byte[]> rawobjects = new List<byte[]>();

            int i = 0;
            while (i < src.Length)
            {
                List<byte> raw = new List<byte>();
                for (; i < src.Length; i++)
                {
                    if (src[i] == 24)
                    {
                        if (src[i + 1] == 36)
                        {
                            raw.Add(24);
                        }
                        else if (src[i + 1] == 49)
                        {
                            raw.Add(0);
                        }
                        i += 1;
                    }
                    else if (src[i] == 0)
                    {
                        i += 1;
                        break;
                    }
                    else
                    {
                        raw.Add(src[i]);
                    }
                }
                rawobjects.Add(raw.ToArray());
            }
            return rawobjects;
        }
    }
}
