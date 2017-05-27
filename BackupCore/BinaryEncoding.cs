using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace BackupCore
{
    public class BinaryEncoding
    {

        /// <summary>
        /// Takes raw binary data to be stored and encodes it so it can be read back in.
        /// Meant to be called repeatedly to add more data to dst.
        /// Slow for large data sets. Meant to be only used for small bits of data. Such
        /// as the headers of the other encode methods.
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
        public static List<byte[]> decode(byte[] src, int maxobjects=0)
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
                if (maxobjects != 0 && rawobjects.Count >= maxobjects) // Don't keep scanning if we have all the objects we want
                {
                    if (i < src.Length) // There are bytes at the end not in objects, blindly add them without scanning
                    {
                        byte[] addition = new byte[src.Length - i];
                        Array.Copy(src, i, addition, 0, src.Length - i);
                        rawobjects.Add(addition);
                    }
                    break; // exit outer for loop and return
                }
            }
            return rawobjects;
        }

        /// <summary>
        /// Converts objects of raw binary data identified by string key to
        /// a binary only representation. Preferred encoding method for all
        /// classes looking to serialize their data.
        /// </summary>
        /// <param name="dataobjects">Key=name(+version) of object to save,
        /// Values=binary data of object</param>
        public static byte[] dict_encode(Dictionary<string, byte[]> dataobjects)
        {
            // String data labels (keys) will allow backwards compatability for later updates
            // to the storage format. (For instance data saved with the key "SomeValue-v1" 
            // could be read, then resaved with a new key "SomeValue-v2".
            MemoryStream encodeddata = new MemoryStream();
            foreach (var kv in dataobjects)
            {
                // Write header
                byte[] binkey = Encoding.UTF8.GetBytes(kv.Key);
                byte[] bindatalen = BitConverter.GetBytes(kv.Value.Length);
                List<byte> header = new List<byte>();
                encode(binkey, header);
                encode(bindatalen, header);
                encodeddata.Write(header.ToArray(), 0, header.Count);
                // Write body
                if (kv.Value != null)
                {
                    encodeddata.Write(kv.Value, 0, kv.Value.Length);
                }
            }
            byte[] bencodeddata = encodeddata.ToArray();
            encodeddata.Close();
            return bencodeddata;
        }

        /// <summary>
        /// Takes stored binary data and reads all sets of raw data present.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dst"></param>
        /// <returns>A dictionary of names(+version) to data objects.</returns>
        public static Dictionary<string, byte[]> dict_decode(byte[] src)
        {
            Dictionary<string, byte[]> rawobjects = new Dictionary<string, byte[]>();

            byte[] next = src;
            while (next != null)
            {
                List<byte[]> savedheaderbodynext = decode(next, 2);
                string key = Encoding.UTF8.GetString(savedheaderbodynext[0]);
                int datalen = BitConverter.ToInt32(savedheaderbodynext[1], 0);
                if (datalen != 0)
                {
                    byte[] body = new byte[datalen];
                    Array.Copy(savedheaderbodynext[2], body, datalen);
                    rawobjects.Add(key, body);

                    if (savedheaderbodynext[2].Length - datalen > 0)
                    {
                        next = new byte[savedheaderbodynext[2].Length - datalen];
                        Array.Copy(savedheaderbodynext[2], datalen, next, 0, savedheaderbodynext[2].Length - datalen);
                    }
                    else
                    {
                        next = null;
                    }
                }
                else
                {
                    rawobjects.Add(key, null);
                    if (savedheaderbodynext.Count >= 3)
                    {
                        next = savedheaderbodynext[2];
                    }
                    else
                    {
                        next = null;
                    }
                }
            }
            return rawobjects;
        }

        /// <summary>
        /// Encodes an enumerable (list) of bytes.
        /// This includes no labels like dict_encode(), so it is preferred that
        /// data encoded with this function be wrapped with a dict_encode().
        /// </summary>
        /// <param name="objects"></param>
        /// <returns></returns>
        public static byte[] enum_encode(IEnumerable<byte[]> objects)
        {
            if (objects == null)
            {
                return new byte[0];
            }
            MemoryStream encodeddata = new MemoryStream();

            List<byte[]> lobjects = new List<byte[]>(objects);
            // Write header
            // array : object count, lengths of the objects we will write...
            byte[] binheader = new byte[lobjects.Count * 4 + 4]; // four bytes per int32 we write
            Array.Copy(BitConverter.GetBytes(lobjects.Count), binheader, 4);
            for (int i = 0; i < lobjects.Count; i++)
            {
                if (lobjects[i] != null)
                {
                    Array.Copy(BitConverter.GetBytes(lobjects[i].Length), 0, binheader, i * 4 + 4, 4);
                }
                else
                {
                    Array.Copy(BitConverter.GetBytes(0), 0, binheader, i * 4 + 4, 4);
                }
            }
            encodeddata.Write(binheader, 0, binheader.Length);
            // Write body
            foreach (var obj in lobjects)
            {
                if (obj != null)
                {
                    encodeddata.Write(obj, 0, obj.Length);
                }
            }
            byte[] bencodeddata = encodeddata.ToArray();
            encodeddata.Close();
            return bencodeddata;
        }

        public static List<byte[]> enum_decode(byte[] src)
        {
            if (src == null || src.Length == 0)
            {
                return null;
            }
            List<byte[]> rawobjects = new List<byte[]>();

            int objcount = BitConverter.ToInt32(src, 0);

            int newobjstartindex = objcount * 4 + 4;
            for (int i = 0; i < objcount; i++)
            {
                int newobjlen = BitConverter.ToInt32(src, i * 4 + 4);
                byte[] newobj;
                if (newobjlen != 0)
                {
                    newobj = new byte[newobjlen];
                    Array.Copy(src, newobjstartindex, newobj, 0, newobjlen);
                }
                else
                {
                    newobj = null;
                }
                rawobjects.Add(newobj);
                newobjstartindex += newobjlen;
            }
            return rawobjects;
        }
    }
}
