using System;
using System.Text;
using System.Collections.Generic;
using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace BackupCoreTest
{
    /// <summary>
    /// Summary description for BinaryEncodingTest
    /// </summary>
    [TestClass]
    public class BinaryEncodingTest
    {
        public BinaryEncodingTest()
        {
            //
            // TODO: Add constructor logic here
            //
        }

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
    }
}
