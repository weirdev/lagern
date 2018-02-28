using System;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Linq;

namespace BackupCore
{
    class AesHelper
    {
        private Aes DataKeyAesProvider;
        private Aes DataAesProvider;

        private byte[] DataKeyKey; // Dont store, generated from password
        private byte[] PasswordSalt; // Store

        private byte[] DataKeyKeyHash; // Store to verify password
        private byte[] DataKeyKeyHashSalt; // Store

        private byte[] DataKey; // Store encrypted by DataKeyKey
        private byte[] DataKeyIV; // IV for encrypted DataKey

        private AesHelper(byte[] datakeykey, byte[] passwordsalt, byte[] datakeykeyhash, 
            byte[] datakeykeyhashsalt, byte[] datakey = null, byte[] datakeyiv = null)
        {
            DataKeyKey = datakeykey;
            PasswordSalt = passwordsalt;
            DataKeyKeyHash = datakeykeyhash;
            DataKeyKeyHashSalt = datakeykeyhashsalt;
            DataKeyAesProvider = Aes.Create();
            DataKeyAesProvider.Key = datakeykey;
            DataAesProvider = Aes.Create();
            if (datakey != null)
            {
                DataKey = datakey;
                DataKeyIV = datakeyiv;
                DataAesProvider.Key = DataKey;
                DataKeyAesProvider.IV = DataKeyIV;
            }
            else
            {
                DataKey = DataAesProvider.Key;
                DataKeyIV = DataKeyAesProvider.IV;
            }
        }

        public static AesHelper CreateFromPassword(string password)
        {
            var phasher = new Rfc2898DeriveBytes(password, 8);
            var psalt = phasher.Salt;
            var datakeykey = phasher.GetBytes(128);
            var phashsalt = new byte[8];
            using (RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider())
            {
                rngCsp.GetBytes(phashsalt);
            }
            var phashhasher = new Rfc2898DeriveBytes(datakeykey, phashsalt, 8192);
            var phashhash = phashhasher.GetBytes(20); 
            
            return new AesHelper(datakeykey, psalt, phashhash, phashsalt);
        }

        public static AesHelper CreateFromKeyFile(byte[] file, string password)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(file);
            var phasher = new Rfc2898DeriveBytes(password, savedobjects["passwordsalt-v1"]);
            var datakeykey = phasher.GetBytes(128);
            var phashhasher = new Rfc2898DeriveBytes(datakeykey, savedobjects["datakeykeyhashsalt-v1"], 8192);
            var phashhash = phashhasher.GetBytes(20);
            if (phashhash.SequenceEqual(savedobjects["datakeykeyhash-v1"]))
            {
                return new AesHelper(datakeykey, savedobjects["passwordsalt-v1"], savedobjects["datakeykeyhash-v1"],
                    savedobjects["datakeykeyhashsalt-v1"],
                    DecryptAesDataKey(savedobjects["encrypteddatakey-v1"], datakeykey, savedobjects["datakeyiv-v1"]),
                    savedobjects["datakeyiv-v1"]);
            }
            else
            {
                throw new PasswordIncorrectException("The password used to decrypt the keyfile " +
                    "does not match the file used to creat it.");
            }
        }

        public (ICryptoTransform encryptor, byte[] iv) GetDataEncyptor()
        {
            DataAesProvider.GenerateIV();
            return (DataAesProvider.CreateEncryptor(), DataAesProvider.IV);
        }

        public ICryptoTransform GetDecryptor(byte[] iv)
        {
            return DataAesProvider.CreateDecryptor(DataAesProvider.Key, iv);
        }

        private byte[] EncryptAesDataKey()
        {
            using (ICryptoTransform encryptor = DataKeyAesProvider.CreateEncryptor())
            {
                return encryptor.TransformFinalBlock(DataKey, 0, DataKey.Length);
            }
        }

        private static byte[] DecryptAesDataKey(byte[] enc_datakey, byte[] datakeykey, byte[] datakeyiv)
        {
            using (var dec = Aes.Create())
            {
                dec.Key = datakeykey;
                dec.IV = datakeyiv;
                using (ICryptoTransform decryptor = dec.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(enc_datakey, 0, enc_datakey.Length);
                }
            }
        }

        /// <summary>
        /// Store encrypted data key and other info needed to
        /// regenerate AesHelper given a password.
        /// </summary>
        /// <returns></returns>
        public byte[] CreateKeyFile()
        {
            Dictionary<string, byte[]> kfdata = new Dictionary<string, byte[]>();
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
    }
}
