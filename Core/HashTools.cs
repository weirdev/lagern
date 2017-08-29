using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BackupCore
{
    public static class HashTools
    {
        private static readonly uint[] _lookup32 = CreateLookup32();

        private static readonly MD5 md5hasher = MD5.Create();

        // We hash one byte at a time, so there are only 256 possible values to hash
        // Thus, there are only 256 possible hash results and we need not compute these more than once.
        public static readonly byte[][] md5hashes = CreateMD5ByteLookupTable();

        private static uint[] CreateLookup32()
        {
            var result = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                string s = i.ToString("X2");
                result[i] = ((uint)s[0]) + ((uint)s[1] << 16);
            }
            return result;
        }

        public static string ByteArrayToHexViaLookup32(byte[] bytes)
        {
            var lookup32 = _lookup32;
            var result = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                var val = lookup32[bytes[i]];
                result[2 * i] = (char)val;
                result[2 * i + 1] = (char)(val >> 16);
            }
            return new string(result);
        }

        public static byte[] HexStringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        public static bool ByteArrayLessThan(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                throw new ArgumentException("The bytes to be compared must be equal");
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] < right[i])
                {
                    return true;
                }
                else if (left[i] > right[i])
                {
                    return false;
                }
            }
            return false;
        }

        public static bool ByteArrayLessThanEqualTo(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                throw new ArgumentException("The bytes to be compared must be equal");
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] < right[i])
                {
                    return true;
                }
                else if (left[i] > right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static byte[][] CreateMD5ByteLookupTable()
        {
            // Creat hash lookup table
            var md5hashes = new byte[256][];
            for (int i = 0; i < md5hashes.Length; i++)
            {
                byte[] tohash = new byte[1];
                tohash[0] = (byte)i;
                md5hashes[i] = md5hasher.ComputeHash(tohash);
            }
            return md5hashes;
        }

        public static void ByteSum(byte[] asum, byte b)
        {
            int carry = 0;
            int sum;
            sum = asum[0] + b;
            asum[0] = (byte)(sum & 0xFF);
            carry = sum >> 8;
            for (int i = 1; carry != 0 && i < asum.Length; i++)
            {
                sum = asum[i] + carry;
                asum[i] = (byte)(sum & 0xFF);
                carry = sum >> 8;
            }
        }

        public static void BytesSum(byte[] asum, byte[] b)
        {
            int carry = 0;
            int sum;
            for (int i = 0; i < asum.Length; i++)
            {
                sum = asum[i] + b[i] + carry;
                asum[i] = (byte)(sum & 0xFF);
                carry = sum >> 8;
            }
        }

        public static void ByteDifference(byte[] adiff, byte b)
        {
            int carry = 0;
            int diff;
            diff = adiff[0] - b;
            adiff[0] = (byte)(diff & 0xFF);
            carry = diff >= 0 ? 0 : 1;
            for (int i = 1; carry != 0 && i < adiff.Length; i++)
            {
                diff = adiff[i] - carry;
                adiff[i] = (byte)(diff & 0xFF);
                carry = diff >= 0 ? 0 : 1;
            }
        }

        public static void BytesDifference(byte[] adiff, byte[] b)
        {
            int carry = 0;
            int diff;
            for (int i = 0; i < adiff.Length; i++)
            {
                diff = adiff[i] - b[i] - carry;
                adiff[i] = (byte)(diff & 0xFF);
                carry = diff >= 0 ? 0 : 1;
            }
        }

        public static void BytesLeftShift(byte[] shift, int amount)
        {
            if (amount >= 8)
            {
                int byteshifts = amount / 8;
                byte[] sh = new byte[shift.Length];
                Array.Copy(shift, 0, sh, byteshifts, shift.Length - byteshifts);
                amount = amount % 8;
            }
            int carry = 0;
            int shifted;
            for (int i = 0; i < shift.Length; i++)
            {
                shifted = (shift[i] << amount) + carry;
                shift[i] = (byte)(shifted & 0xFF);
                carry = shifted >> 8;
            }
        }
    }
}
