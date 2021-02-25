using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;

namespace CoreTest
{
    /// <summary>
    /// Summary description for BackupLocationTest
    /// </summary>
    [TestClass]
    public class BlobLocationTest
    {
        public BlobLocationTest() { }

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
        public void TestSerializeDeserialize()
        {
            BlobLocation old;
            BlobLocation deser;
            byte[] serialized;
            byte[] reserialized;

            old = new BlobLocation(null, "somewhere1", 40);
            serialized = old.serialize();
            deser = BlobLocation.deserialize(serialized);
            reserialized = deser.serialize();
            Assert.AreEqual(old, deser);
            Assert.IsTrue(serialized.AsSpan().SequenceEqual(reserialized.AsSpan()));

            old = new BlobLocation(new byte[] {100, 23, 6}, "somewhere1", 40);
            serialized = old.serialize();
            deser = BlobLocation.deserialize(serialized);
            reserialized = deser.serialize();
            Assert.AreEqual(old, deser);
            Assert.IsTrue(serialized.AsSpan().SequenceEqual(reserialized.AsSpan()));

            old = new BlobLocation(new List<byte[]>() { new byte[] { 100, 40 }, new byte[] { 23 }, new byte[] { 6, 70, 10, 205 } });
            serialized = old.serialize();
            deser = BlobLocation.deserialize(serialized);
            reserialized = deser.serialize();
            Assert.AreEqual(old, deser);
            Assert.IsTrue(serialized.AsSpan().SequenceEqual(reserialized.AsSpan()));

            old = new BlobLocation(new byte[] { 100, 23, 6 }, "somewhere1", 40);
            old.SetBSetReferenceCount("dst", false, 10);
            old.SetBSetReferenceCount("dst2", false, 20);
            serialized = old.serialize();
            deser = BlobLocation.deserialize(serialized);
            reserialized = deser.serialize();
            Assert.AreEqual(old, deser);
            Assert.IsTrue(serialized.AsSpan().SequenceEqual(reserialized.AsSpan()));
            Assert.AreEqual(deser.GetBSetReferenceCount("dst", false), 10);
            Assert.AreEqual(deser.GetBSetReferenceCount("dst2", false), 20);
        }
    }
}
