using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;

namespace BackupCoreTest
{
    [TestClass]
    public class HashToolsTest
    {
        [TestMethod]
        public void TestBytesSum()
        {
            string seq1 = "11111111";
            string seq2 = "2222222F";
            byte[] bin1 = HashTools.HexStringToByteArray(seq1);
            byte[] bin2 = HashTools.HexStringToByteArray(seq2);
            HashTools.BytesSum(bin1, bin2);
            Console.WriteLine(HashTools.ByteArrayToHexViaLookup32(bin1));
            Assert.IsTrue(HashTools.ByteArrayToHexViaLookup32(bin1) == "33333340");
        }
    }
}
