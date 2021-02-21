using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.Linq;
using System.Security.Cryptography;

namespace CoreTest
{
    [TestClass]
    public class AesHelperTest
    {
        [TestMethod]
        public void TestCreateFromPassword()
        {
            string password = "password";
            AesHelper aesHelper = AesHelper.CreateFromPassword(password);
            Assert.IsNotNull(aesHelper);
        }

        [TestMethod]
        public void TestCreateKeyFile()
        {
            AesHelper aesHelper = AesHelper.CreateFromPassword("12");
            byte[] keyfile = aesHelper.CreateKeyFile();
            Assert.IsNotNull(keyfile);
            Assert.AreNotEqual(keyfile.Length, 0);
        }

        [TestMethod]
        public void TestCreateFromKeyFile()
        {
            string password = "12";
            string wrongpassword = password + "wrong";
            AesHelper aesHelper = AesHelper.CreateFromPassword(password);
            byte[] keyfile = aesHelper.CreateKeyFile();

            // Test wrong password
            AesHelper aesHelper2;
            Assert.ThrowsException<AesHelper.PasswordIncorrectException>(
                () => AesHelper.CreateFromKeyFile(keyfile, wrongpassword));

            // Test correct password
            aesHelper2 = AesHelper.CreateFromKeyFile(keyfile, password);
            Assert.IsNotNull(aesHelper2);

            // NOTE: Do not test equivalent encryption, IV is random
            // so multiple encryptions will return different files

            // Test Equivalent decryption
            byte[] data = new byte[100];
            CoreTest.RandomData(data);
            byte[] decrypted = aesHelper.DecryptBytes(aesHelper.EncryptBytes(data));
            Assert.IsTrue(decrypted.SequenceEqual(data));
            Assert.IsTrue(decrypted
                .SequenceEqual(aesHelper2.DecryptBytes(aesHelper2.EncryptBytes(data))));
            byte[] decrypted2 = aesHelper2.DecryptBytes(aesHelper.EncryptBytes(data));
            decrypted2.SequenceEqual(data);
            Assert.IsTrue(decrypted2
                .SequenceEqual(aesHelper.DecryptBytes(aesHelper2.EncryptBytes(data))));
        }

        [TestMethod]
        public void TestAesHelperReconstruct()
        {
            string password = "12";
            AesHelper aesHelper = AesHelper.CreateFromPassword(password);
            byte[] keyfile = aesHelper.CreateKeyFile();

            // Test correct password
            AesHelper aesHelperDirect = new AesHelper(aesHelper.DataKeyKey, aesHelper.PasswordSalt, aesHelper.DataKeyKeyHash,
                aesHelper.DataKeyKeyHashSalt, aesHelper.DataKeyAesProvider, aesHelper.DataKeyIV, aesHelper.DataKey);
            Assert.IsNotNull(aesHelperDirect);

            Assert.IsTrue(aesHelper.DataKeyKey.SequenceEqual(aesHelper.DataKeyAesProvider.Key));
            Assert.IsTrue(aesHelper.DataKeyIV.SequenceEqual(aesHelper.DataKeyAesProvider.IV));

            RijndaelManaged dataKeyAesProvider = AesHelper.CreateDataKeyAesProvider(aesHelper.DataKeyKey);
            dataKeyAesProvider.IV = aesHelper.DataKeyIV;
            byte[] decryptedAesDataKey = AesHelper.DecryptAesDataKey(aesHelper.EncryptAesDataKey(), dataKeyAesProvider);
            Assert.IsTrue(decryptedAesDataKey.SequenceEqual(aesHelper.DataKey));

            //AesHelper aesHelperFromFile = AesHelper.CreateFromKeyFile(keyfile, password);
            AesHelper aesHelperFromFile = AesHelper.CreateFromKeyFile(aesHelper.PasswordSalt, aesHelper.DataKeyKeyHashSalt, 
                aesHelper.DataKeyIV, aesHelper.DataKeyKeyHash, aesHelper.EncryptAesDataKey(), password);

            Assert.IsTrue(aesHelperDirect.DataKeyKey.SequenceEqual(aesHelperFromFile.DataKeyKey));
            Assert.IsTrue(aesHelperDirect.PasswordSalt.SequenceEqual(aesHelperFromFile.PasswordSalt));
            Assert.IsTrue(aesHelperDirect.DataKeyKeyHash.SequenceEqual(aesHelperFromFile.DataKeyKeyHash));
            Assert.IsTrue(aesHelperDirect.DataKeyKeyHashSalt.SequenceEqual(aesHelperFromFile.DataKeyKeyHashSalt));
            Assert.IsTrue(aesHelperDirect.DataKeyIV.SequenceEqual(aesHelperFromFile.DataKeyIV));
            Assert.IsTrue(aesHelperDirect.DataKey.SequenceEqual(aesHelperFromFile.DataKey));

            // NOTE: Do not test equivalent encryption, IV is random
            // so multiple encryptions will return different files

            // Test Equivalent decryption
            byte[] data = new byte[100];
            CoreTest.RandomData(data);
            byte[] decrypted = aesHelper.DecryptBytes(aesHelper.EncryptBytes(data));
            Assert.IsTrue(decrypted.SequenceEqual(data));
            Assert.IsTrue(decrypted
                .SequenceEqual(aesHelperDirect.DecryptBytes(aesHelperDirect.EncryptBytes(data))));
            byte[] decrypted2 = aesHelperDirect.DecryptBytes(aesHelper.EncryptBytes(data));
            decrypted2.SequenceEqual(data);
            Assert.IsTrue(decrypted2
                .SequenceEqual(aesHelper.DecryptBytes(aesHelperDirect.EncryptBytes(data))));
        }

        [TestMethod]
        public void TestEncrypt()
        {
            AesHelper aesHelper = AesHelper.CreateFromPassword("12");
            byte[] data = new byte[0];
            byte[] encrypted = aesHelper.EncryptBytes(data);
            Assert.IsNotNull(encrypted);
            Assert.IsTrue(encrypted.Length == data.Length);

            data = new byte[1];
            CoreTest.RandomData(data);
            encrypted = aesHelper.EncryptBytes(data);
            Assert.IsNotNull(encrypted);
            Assert.IsTrue(encrypted.Length > data.Length);

            data = new byte[1000];
            CoreTest.RandomData(data);
            encrypted = aesHelper.EncryptBytes(data);
            Assert.IsNotNull(encrypted);
            Assert.IsTrue(encrypted.Length > data.Length);
        }

        [TestMethod]
        public void TestDecrypt()
        {
            AesHelper aesHelper = AesHelper.CreateFromPassword("12");
            byte[] data = new byte[0];
            byte[] encrypted = aesHelper.EncryptBytes(data);
            byte[] decrypted = aesHelper.DecryptBytes(encrypted);
            Assert.IsNotNull(decrypted);
            Assert.IsTrue(encrypted.Length == decrypted.Length);
            Assert.IsTrue(data.SequenceEqual(decrypted));

            data = new byte[1];
            CoreTest.RandomData(data);
            encrypted = aesHelper.EncryptBytes(data);
            decrypted = aesHelper.DecryptBytes(encrypted);
            Assert.IsNotNull(decrypted);
            Assert.IsTrue(encrypted.Length > decrypted.Length);
            Assert.IsTrue(data.SequenceEqual(decrypted));

            data = new byte[1000];
            CoreTest.RandomData(data);
            encrypted = aesHelper.EncryptBytes(data);
            decrypted = aesHelper.DecryptBytes(encrypted);
            Assert.IsNotNull(decrypted);
            Assert.IsTrue(encrypted.Length > decrypted.Length);
            Assert.IsTrue(data.SequenceEqual(decrypted));
        }
    }
}
