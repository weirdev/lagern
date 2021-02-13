using System;
using System.Text;
using System.Collections.Generic;
using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace CoreTest
{
    /// <summary>
    /// Summary description for BinaryEncodingTest
    /// </summary>
    [TestClass]
    public class BinaryEncodingTest
    {
        public BinaryEncodingTest() { }

        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Additional test attributes
        //
        // You can use the following additional attributes as you write your tests:
        //
        // Use ClassInitialize to run code before running the first test in the class
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup to run code after each test has run
        // [TestCleanup()]
        // public void MyTestCleanup() { }
        //
        #endregion

        [TestMethod]
        public void TestEncodeDecode()
        {
            byte[] data = new byte[4000];
            Random rng = new Random();
            rng.NextBytes(data);

            byte[] data2 = new byte[4000];
            rng.NextBytes(data2);

            List<byte> encoded = new List<byte>();
            BackupCore.BinaryEncoding.encode(data, encoded);
            BackupCore.BinaryEncoding.encode(data2, encoded);

            Assert.IsTrue(BackupCore.BinaryEncoding.decode(encoded.ToArray())[0].SequenceEqual(data));
            Assert.IsTrue(BackupCore.BinaryEncoding.decode(encoded.ToArray())[1].SequenceEqual(data2));
        }

        [TestMethod]
        public void TestDictEncodeDecode()
        {
            byte[] data = new byte[4000];
            Random rng = new Random();
            rng.NextBytes(data);

            byte[] data2 = new byte[4000];
            rng.NextBytes(data2);

            Dictionary<string, byte[]> toencode = new Dictionary<string, byte[]>();
            toencode.Add("data", data);
            toencode.Add("data2", data2);
            byte[] encoded = BackupCore.BinaryEncoding.dict_encode(toencode);
            Dictionary<string, byte[]> decoded = BackupCore.BinaryEncoding.dict_decode(encoded);

            Assert.IsTrue(decoded["data"].SequenceEqual(data));
            Assert.IsTrue(decoded["data2"].SequenceEqual(data2));
        }

        [TestMethod]
        public void TestEnumEncodeDecode()
        {
            byte[] data = new byte[4000];
            Random rng = new Random();
            rng.NextBytes(data);

            byte[] data2 = new byte[4000];
            rng.NextBytes(data2);

            byte[] data3 = null;

            byte[] data4 = new byte[0];

            byte[] data5 = new byte[200];
            rng.NextBytes(data5);


            List<byte[]> toencode = new List<byte[]>();
            toencode.Add(data);
            toencode.Add(data2);
            toencode.Add(data3);
            toencode.Add(data4);
            toencode.Add(data5);
            byte[] encoded = BackupCore.BinaryEncoding.enum_encode(toencode);
            List<byte[]> decoded = BackupCore.BinaryEncoding.enum_decode(encoded);

            Assert.IsTrue(decoded[0].SequenceEqual(data));
            Assert.IsTrue(decoded[1].SequenceEqual(data2));
            Assert.IsTrue(decoded[2] == null);
            Assert.IsTrue(decoded[3] == null);
            Assert.IsTrue(decoded[4].SequenceEqual(data5));
        }
    }
}
