using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.IO;
using System.Linq;

namespace CoreTest
{
    [TestClass]
    public class AesHelperTest
    {
        static Random random = new Random();

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
        public void TestCreateFromKeyfile()
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
            Assert.IsTrue(aesHelper.DecryptBytes(aesHelper.EncryptBytes(data))
                .SequenceEqual(aesHelper2.DecryptBytes(aesHelper2.EncryptBytes(data))));
            Assert.IsTrue(aesHelper2.DecryptBytes(aesHelper.EncryptBytes(data))
                .SequenceEqual(aesHelper.DecryptBytes(aesHelper2.EncryptBytes(data))));
        }

        [TestMethod]
        public void TestEncrypt()
        {
            AesHelper aesHelper = AesHelper.CreateFromPassword("12");
            byte[] data = new byte[0];
            byte[] encrypted = aesHelper.EncryptBytes(data);
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
            Assert.IsTrue(encrypted.Length > decrypted.Length);
            Assert.IsTrue(data.SequenceEqual(decrypted));

            data = new byte[1000];
            encrypted = aesHelper.EncryptBytes(data);
            decrypted = aesHelper.DecryptBytes(encrypted);
            Assert.IsNotNull(decrypted);
            Assert.IsTrue(encrypted.Length > decrypted.Length);
            Assert.IsTrue(data.SequenceEqual(decrypted));
        }
    }
}
