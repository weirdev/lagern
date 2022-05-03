using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Linq;
using System.IO;

namespace BackupCore
{
    public class AesHelper
    {
        private RijndaelManaged DataAesProvider;
        public static readonly int IVSize = 16;

        public RijndaelManaged DataKeyAesProvider { get; private set; }
        public byte[] DataKeyKey { get; private set; }
        public byte[] PasswordSalt { get; private set; }
        public byte[] DataKeyKeyHash { get; private set; }
        public byte[] DataKeyKeyHashSalt { get; private set; }
        public byte[] DataKeyIV { get; private set; }
        public byte[] DataKey { get; private set; }

        public AesHelper(byte[] datakeykey, byte[] passwordsalt, byte[] datakeykeyhash,
            byte[] datakeykeyhashsalt, RijndaelManaged dataKeyAesProvider, byte[] datakeyiv, byte[]? datakey = null)
        {
            DataKeyKey = datakeykey;
            PasswordSalt = passwordsalt;
            DataKeyKeyHash = datakeykeyhash;
            DataKeyKeyHashSalt = datakeykeyhashsalt;
            DataKeyAesProvider = dataKeyAesProvider;
            DataKeyIV = datakeyiv;
            DataAesProvider = new RijndaelManaged
            {
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7,
                BlockSize = 128,
                KeySize = 128
            };
            if (datakey != null)
            {
                DataKey = datakey;
                DataAesProvider.Key = DataKey;
            }
            else
            {
                DataAesProvider.GenerateKey();
                DataKey = DataAesProvider.Key;
            }
        }

        public static RijndaelManaged CreateDataKeyAesProvider(byte[] datakeykey)
        {
            RijndaelManaged dataKeyAesProvider = new()
            {
                Padding = PaddingMode.PKCS7,
                Mode = CipherMode.CBC,
                BlockSize = 128,
                KeySize = 128
            };
            // Key must be set after construction
            dataKeyAesProvider.Key = datakeykey;
            return dataKeyAesProvider;
        }

        /// <summary>
        /// Create a new AES helper given a password
        /// </summary>
        /// <param name="password"></param>
        /// <returns></returns>
        public static AesHelper CreateFromPassword(string password)
        {
            var phasher = new Rfc2898DeriveBytes(password, 8);
            var psalt = phasher.Salt;
            var datakeykey = phasher.GetBytes(16); // 128 bits
            var phashsalt = new byte[8];
            using (RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider())
            {
                rngCsp.GetBytes(phashsalt);
            }
            var phashhasher = new Rfc2898DeriveBytes(datakeykey, phashsalt, 8192);
            var phashhash = phashhasher.GetBytes(IVSize);

            RijndaelManaged dataKeyAesProvider = CreateDataKeyAesProvider(datakeykey);
            dataKeyAesProvider.GenerateIV();
            return new AesHelper(datakeykey, psalt, phashhash, phashsalt, dataKeyAesProvider, dataKeyAesProvider.IV);
        }

        /// <summary>
        /// Create an AES helper based on a previously saved keyfile and a password to decrypt it
        /// </summary>
        /// <param name="file"></param>
        /// <param name="password"></param>
        public static AesHelper CreateFromKeyFile(byte[] file, string password)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(file);
            return CreateFromKeyFile(savedobjects["passwordsalt-v1"], savedobjects["datakeykeyhashsalt-v1"],
                savedobjects["datakeyiv-v1"], savedobjects["datakeykeyhash-v1"], savedobjects["encrypteddatakey-v1"], password);
        }

        public static AesHelper CreateFromKeyFile(byte[] passwordSalt, byte[] dataKeyKeyHashSalt, byte[] dataKeyIV,
            byte[] dataKeyKeyHash, byte[] encrypteddatakey, string password)
        {
            var phasher = new Rfc2898DeriveBytes(password, passwordSalt);
            var datakeykey = phasher.GetBytes(16);
            var phashhasher = new Rfc2898DeriveBytes(datakeykey, dataKeyKeyHashSalt, 8192);
            var phashhash = phashhasher.GetBytes(IVSize);
            if (phashhash.SequenceEqual(dataKeyKeyHash))
            {
                RijndaelManaged dataKeyAesProvider = CreateDataKeyAesProvider(datakeykey);
                dataKeyAesProvider.IV = dataKeyIV;
                return new AesHelper(datakeykey, passwordSalt, dataKeyKeyHash,
                    dataKeyKeyHashSalt, dataKeyAesProvider, dataKeyIV,
                    DecryptAesDataKey(encrypteddatakey, dataKeyAesProvider));
            }
            else
            {
                throw new PasswordIncorrectException("The password used to decrypt the keyfile " +
                    "does not match the file used to create it.");
            }
        }

        public IVCryptoStream GetEncryptedStream(Stream input)
        {
            // TODO: Stream wrapping currently experiencing errors
            //return IVCryptoStream.CreateEncryptedStream(input, this);
            throw new NotImplementedException();
        }

        public IVCryptoStream GetDecryptedStream(Stream input)
        {
            //return IVCryptoStream.CreateDecryptedStream(input, this);
            throw new NotImplementedException();
        }

        public byte[] EncryptBytes(byte[] input)
        {
            if (input.Length == 0)
            {
                return Array.Empty<byte>();
            }
            else
            {
                (ICryptoTransform encryptor, byte[] iv) = GetDataEncyptor();
                try
                {
                    byte[] encrypted = encryptor.TransformFinalBlock(input, 0, input.Length);
                    byte[] ivAndEncrypted = new byte[IVSize + encrypted.Length];
                    if (iv.Length != IVSize)
                    {
                        throw new InvalidDataException("IV is incorrect size");
                    }
                    Array.Copy(iv, ivAndEncrypted, iv.Length);
                    Array.Copy(encrypted, 0, ivAndEncrypted, iv.Length, encrypted.Length);
                    return ivAndEncrypted;
                }
                finally
                {
                    encryptor.Dispose();
                }
            }
        }

        public byte[] DecryptBytes(byte[] input)
        {
            if (input.Length == 0)
            {
                return Array.Empty<byte>();
            } 
            else
            {
                byte[] iv = new byte[IVSize];
                Array.Copy(input, iv, IVSize);
                ICryptoTransform decryptor = GetDataDecryptor(iv);
                try
                {
                    // TODO: Sometimes failure here, uuid: 795243
                    return decryptor.TransformFinalBlock(input, IVSize, input.Length - IVSize);
                }
                catch (Exception e)
                {
                    throw new Exception("Decryption failure. Input size: " + input.Length, e);
                }
                finally
                {
                    decryptor.Dispose();
                }
            }
        }

        private (ICryptoTransform encryptor, byte[] iv) GetDataEncyptor()
        {
            DataAesProvider.GenerateIV();
            return (DataAesProvider.CreateEncryptor(), DataAesProvider.IV);
        }

        private ICryptoTransform GetDataDecryptor(byte[] iv)
        {
            return DataAesProvider.CreateDecryptor(DataAesProvider.Key, iv);
        }

        public byte[] EncryptAesDataKey()
        {
            using ICryptoTransform encryptor = DataKeyAesProvider.CreateEncryptor();
            return encryptor.TransformFinalBlock(DataKey, 0, DataKey.Length);
        }

        public static byte[] DecryptAesDataKey(byte[] enc_datakey, RijndaelManaged dataKeyAesProvider)
        {
            using ICryptoTransform decryptor = dataKeyAesProvider.CreateDecryptor();
            return decryptor.TransformFinalBlock(enc_datakey, 0, enc_datakey.Length);
        }

        /// <summary>
        /// Store encrypted data key and other info needed to
        /// regenerate AesHelper given a password.
        /// </summary>
        /// <returns></returns>
        public byte[] CreateKeyFile()
        {
            Dictionary<string, byte[]> kfdata = new();
            // -"-v1"
            // passwordsalt
            // datakeykeyhash
            // datakeykeyhashsalt
            // encrypteddatakey
            // datakeyiv
            kfdata.Add("passwordsalt-v1", PasswordSalt);
            kfdata.Add("datakeykeyhash-v1", DataKeyKeyHash);
            kfdata.Add("datakeykeyhashsalt-v1", DataKeyKeyHashSalt);
            kfdata.Add("encrypteddatakey-v1", EncryptAesDataKey());
            kfdata.Add("datakeyiv-v1", DataKeyIV);
            return BinaryEncoding.dict_encode(kfdata);
        }

        public class PasswordIncorrectException : Exception
        {
            public PasswordIncorrectException(string message) : base(message) { }
        }

        public class IVCryptoStream : Stream
        {
            public static IVCryptoStream CreateEncryptedStream(Stream input, AesHelper aes)
            {
                (ICryptoTransform encryptor, byte[] iv) = aes.GetDataEncyptor();
                return new IVCryptoStream(new CryptoStream(input, encryptor, CryptoStreamMode.Read), input, iv, true);
            }

            public static IVCryptoStream CreateDecryptedStream(Stream input, AesHelper aes)
            {
                byte[] iv = new byte[IVSize];
                input.Read(iv, 0, iv.Length);
                ICryptoTransform decryptor = aes.GetDataDecryptor(iv);
                return new IVCryptoStream(new CryptoStream(input, decryptor, CryptoStreamMode.Read), input, iv, false);
            }

            private CryptoStream CryptoInner { get; set; }

            private Stream RawInner { get; set; }

            private byte[] IV { get; set; }

            public override bool CanRead => CryptoInner.CanRead;

            public override bool CanSeek => false;

            public override bool CanWrite => CryptoInner.CanWrite;

            public void Clear() => CryptoInner.Clear();

            public override long Length
            {
                get
                {
                    if (EncryptedOut)
                    {
                        return RawInner.Length + IV.Length;
                    }
                    else
                    {
                        return RawInner.Length - IV.Length;
                    }
                }
            }

            public override long Position
            {
                get
                {
                    if (EncryptedOut)
                    {
                        return CryptoInner.Position + IVPosition;
                    }
                    else
                    {
                        return CryptoInner.Position - IV.Length;
                    }
                }
                set => throw new NotSupportedException();
            }

            private int IVPosition { get; set; }

            /// <summary>
            /// Encrypted out includes IV as first IV.Length bytes of stream.
            /// When this is false the inner stream has the leading IV
            /// </summary>
            private bool EncryptedOut { get; set; }

            IVCryptoStream(CryptoStream cryptoinner, Stream rawinner, byte[] iv, bool encryptedout)
            {
                CryptoInner = cryptoinner;
                RawInner = rawinner;
                IV = iv;
                EncryptedOut = encryptedout;
                if (encryptedout)
                {
                    IVPosition = 0;
                }
                else
                {
                    IVPosition = iv.Length;
                }
            }

            public override void Flush() => CryptoInner.Flush();

            public override int Read(byte[] buffer, int offset, int count)
            {
                int ivbytesread = 0;
                if (EncryptedOut)
                {
                    // First copy IV bytes
                    if (IVPosition < IV.Length)
                    {
                        int ivend = (IVPosition + count < IV.Length) ? IVPosition + count : IV.Length;
                        ivbytesread = ivend - IVPosition;
                        Array.Copy(IV, IVPosition, buffer, offset, ivbytesread);
                        IVPosition = ivend;
                    }
                }
                int innerbytesread = 0;
                if (ivbytesread < count) // bytes left to copy
                {
                    innerbytesread = CryptoInner.Read(buffer, offset, count + IV.Length); // Read 
                }
                return ivbytesread + innerbytesread;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException("IVCryptoStream does not support seeking.");
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException("Cannot set length on IVCryptoStream.");
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                CryptoInner.Write(buffer, offset, count);
            }
        }
    }
}
