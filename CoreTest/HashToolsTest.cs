﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;

namespace CoreTest
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
            Assert.IsTrue(HashTools.ByteArrayToHexViaLookup32(bin1) == "33333340");
        }

        [TestMethod]
        public void TestByteArrayLessThan()
        {
            string seq1 = "11111111";
            string seq2 = "2222222F";
            byte[] bin1 = HashTools.HexStringToByteArray(seq1);
            byte[] bin2 = HashTools.HexStringToByteArray(seq2);
            byte[] bin2copy = HashTools.HexStringToByteArray(seq2);
            Assert.IsTrue(HashTools.ByteArrayLessThan(bin1, bin2));
            Assert.IsFalse(HashTools.ByteArrayLessThan(bin2, bin1));
            Assert.IsFalse(HashTools.ByteArrayLessThan(bin2, bin2copy));
        }
    }
}
