using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using LagernCore.Utilities;
using LagernCore.Models;

namespace CoreTest
{
#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.
    /// <summary>
    /// Summary description for BlobStoreTest
    /// </summary>
    [TestClass]
    public class BlobStoreTest
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public BlobStore BS { get; set; }
        private MetadataNode VirtualFS { get; set; }
        private BPlusTree<byte[]> VFSDataStore { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        // Runs before each test
        [TestInitialize]
        public async Task Initialize()
        {
            VirtualFS = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            VFSDataStore = new BPlusTree<byte[]>(10);
            BS = new BlobStore(new BlobStoreDependencies(await VirtualFSInterop.InitializeNewDst(VirtualFS, VFSDataStore, "dst")));
        }

        [TestMethod]
        public void TestSplitData()
        {
            // We test by first making 8 total random blocks of data a-h

            // The Data are arranged as follows
            //  [ small block_a ] = 2 KB
            //  [ large block_b ] = 2 MB
            //  [ small block_c ] = 8 bytes
            //  [ large block_d ] = 4 MB
            //  [ small block_e ] = 4 KB

            // These are then concenated then split by BS.SplitData()

            // Small blocks a and e are exchanged for equally sized random blocks f, h
            // Small block c is replaced with g, a block of size 1
            // Concenated and split again
            // Make sure when hashes match data is same length
            // Verify size of data represented by "new" hashes is < 20 MB

            byte[] small_a = new byte[2048];
            CoreTest.RandomData(small_a);
            byte[] large_b = new byte[2_971_520];
            CoreTest.RandomData(large_b);
            byte[] small_c = new byte[8];
            CoreTest.RandomData(small_c);
            byte[] large_d = new byte[4_943_040];
            CoreTest.RandomData(large_d);
            byte[] small_e = new byte[4096];
            CoreTest.RandomData(small_e);
            byte[] small_f = new byte[2048];
            CoreTest.RandomData(small_f);
            byte[] small_g = new byte[1];
            CoreTest.RandomData(small_g);
            byte[] small_h = new byte[4096];
            CoreTest.RandomData(small_h);

            byte[] file1 = ConcenateFile(new byte[][] { small_a, large_b, small_c, large_d, small_e });
            byte[] file2 = ConcenateFile(new byte[][] { small_f, large_b, small_g, large_d, small_h });

            BlockingCollection<HashBlobPair> fileblockqueue = new();
            byte[] file1hash = new byte[20]; // Overall hash of file
            BlobStore.SplitData(file1, file1hash, fileblockqueue);

            List<HashBlobPair> f1blockshashes = new();
            while (!fileblockqueue.IsCompleted)
            {
                if (fileblockqueue.TryTake(out HashBlobPair block))
                {
                    f1blockshashes.Add(block);
                }
            }

            fileblockqueue = new BlockingCollection<HashBlobPair>();
            byte[] file2hash = new byte[20]; // Overall hash of file
            BlobStore.SplitData(file2, file2hash, fileblockqueue);

            List<HashBlobPair> f2blockshashes = new();
            while (!fileblockqueue.IsCompleted)
            {
                if (fileblockqueue.TryTake(out HashBlobPair block))
                {
                    f2blockshashes.Add(block);
                }
            }

            Assert.IsFalse(file1hash.SequenceEqual(file2hash));

            Assert.IsTrue(ConcenateFile(f1blockshashes.Select(p => p.Block)).SequenceEqual(file1));
            Assert.IsTrue(ConcenateFile(f2blockshashes.Select(p => p.Block)).SequenceEqual(file2));

            int sizeaddition = VFSDataStore.Select((kvp, _) => kvp.Value.Length).Sum();
            Assert.IsTrue(sizeaddition < file1.Length + file2.Length); // TODO: Magic number?
        }

        // TODO: test serialize and deserialize with explicitly added blobs of
        // all blob types as well as after backup
        [TestMethod]
        public async Task TestBlobStoreSerializeDeserialize()
        {
            var (core, verifyfilepaths, vfsroot, vfsdatastore) = await CoreTest.InitializeNewCoreWithStandardFiles(1, 0);
            await core.RunBackup("test", "initialrun");
            byte[] serialized = core.DefaultDstDependencies[0].Blobs.Serialize();

            // Test that something was serialized
            Assert.IsTrue(serialized.Length > 0);
            // Test that some bytes are nonzero
            Assert.IsFalse(serialized.SequenceEqual(new byte[serialized.Length]));

            var deserialized = BlobStore.Deserialize(serialized, core.DefaultDstDependencies[0].Blobs.Dependencies);
            Assert.IsTrue(core.DefaultDstDependencies[0].Blobs.IndexStore
                .Select(kv => kv.Key)
                .DeepSequenceEqual(deserialized.IndexStore.Select(kv => kv.Key)));
            Assert.IsTrue(core.DefaultDstDependencies[0].Blobs.IndexStore
                .Select(kv => kv.Value)
                .SequenceEqual(deserialized.IndexStore.Select(kv => kv.Value)));
        }

        [TestMethod]
        public async Task TestStoreBlobAndReferencesFile()
        {
            // FileBlob
            byte[] randomFile = new byte[100];
            CoreTest.RandomData(randomFile);
            var bsetRef = new BackupSetReference("test", false, false, false);
            byte[] smallFileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, bsetRef, randomFile);
            await BS.FinalizeBlobAddition(bsetRef, smallFileHash, BlobLocation.BlobType.FileBlob);
            Assert.IsTrue((await BS.RetrieveData(smallFileHash)).SequenceEqual(randomFile));
            var bloc = BS.GetBlobLocation(smallFileHash);
            Assert.IsTrue(bloc.TotalNonShallowReferenceCount == 1);
            Assert.IsTrue(bloc.GetBSetReferenceCount(bsetRef) == 1);

            // Likely multiblock
            randomFile = new byte[12_000_000];
            CoreTest.RandomData(randomFile, new Random(58));
            byte[] fileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, bsetRef, randomFile);
            await BS.FinalizeBlobAddition(bsetRef, fileHash, BlobLocation.BlobType.FileBlob);
            Assert.IsTrue((await BS.RetrieveData(fileHash)).SequenceEqual(randomFile));
            bloc = BS.GetBlobLocation(fileHash);
            Assert.IsTrue(bloc.TotalNonShallowReferenceCount == 1);
            Assert.IsTrue(bloc.GetBSetReferenceCount(bsetRef) == 1);
            Assert.IsTrue(bloc.BlockHashes != null && bloc.BlockHashes.Count >= 1);
            Assert.IsTrue(bloc.BlockHashes.All(hash => BS.GetBlobLocation(hash).GetBSetReferenceCount(bsetRef) == 1));
        }

        [TestMethod]
        public async Task TestStoreBlobTwiceAndReferencesFile()
        {
            var bsetRef = new BackupSetReference("test", false, false, false);

            // FileBlob
            byte[] randomFile = new byte[100];
            CoreTest.RandomData(randomFile);
            byte[] smallFileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, bsetRef, randomFile);
            await BS.FinalizeBlobAddition(bsetRef, smallFileHash, BlobLocation.BlobType.FileBlob);
            Assert.IsTrue((await BS.RetrieveData(smallFileHash)).SequenceEqual(randomFile));
            
            byte[] smallFileHashRound2 = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, bsetRef, randomFile);
            Assert.IsTrue(smallFileHash.SequenceEqual(smallFileHashRound2));
            await BS.FinalizeBlobAddition(bsetRef, smallFileHash, BlobLocation.BlobType.FileBlob);
            var bloc = BS.GetBlobLocation(smallFileHash);
            Assert.IsTrue(bloc.TotalNonShallowReferenceCount == 2);
            Assert.IsTrue(bloc.GetBSetReferenceCount(bsetRef) == 2);

            // Likely multiblock
            randomFile = new byte[12_000_000];
            CoreTest.RandomData(randomFile, new Random(58));
            byte[] fileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, bsetRef, randomFile);
            await BS.FinalizeBlobAddition(bsetRef, fileHash, BlobLocation.BlobType.FileBlob);
            Assert.IsTrue((await BS.RetrieveData(fileHash)).SequenceEqual(randomFile));

            byte[] largeFileHashRound2 = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, bsetRef, randomFile);
            Assert.IsTrue(fileHash.SequenceEqual(largeFileHashRound2));
            await BS.FinalizeBlobAddition(bsetRef, fileHash, BlobLocation.BlobType.FileBlob);
            bloc = BS.GetBlobLocation(fileHash);
            Assert.IsTrue(bloc.TotalNonShallowReferenceCount == 2);
            Assert.IsTrue(bloc.GetBSetReferenceCount(bsetRef) == 2);
            Assert.IsTrue(bloc.BlockHashes != null && bloc.BlockHashes.Count >= 1);
            Assert.IsTrue(bloc.BlockHashes.All(h => BS.GetBlobLocation(h).TotalNonShallowReferenceCount == 1));
        }

        [TestMethod]
        public async Task TestTransferBlobAndReferencesFile()
        {
            var dstVirtualFS = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            var dstVFSDataStore = new BPlusTree<byte[]>(10);
            var dstBS = new BlobStore(new BlobStoreDependencies(await VirtualFSInterop.InitializeNewDst(dstVirtualFS, dstVFSDataStore, "dst")));
            var bsetRef = new BackupSetReference("test", false, false, false);

            // FileBlob
            byte[] randomFile = new byte[100];
            CoreTest.RandomData(randomFile);
            byte[] smallFileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, bsetRef, randomFile);
            await BS.FinalizeBlobAddition(bsetRef, smallFileHash, BlobLocation.BlobType.FileBlob);
            await BS.TransferBlobAndReferences(dstBS, bsetRef, smallFileHash, BlobLocation.BlobType.FileBlob, true);
            var srcBloc = BS.GetBlobLocation(smallFileHash);
            var dstBloc = dstBS.GetBlobLocation(smallFileHash);
            Assert.IsTrue(dstBloc.TotalNonShallowReferenceCount == 1);
            Assert.IsTrue(dstBloc.GetBSetReferenceCount(bsetRef) == 1);
            Assert.IsTrue((await dstBS.RetrieveData(smallFileHash)).SequenceEqual(randomFile));

            // Likely multiblock
            randomFile = new byte[12_000_000];
            CoreTest.RandomData(randomFile, new Random(58));
            byte[] fileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, bsetRef, randomFile);
            await BS.FinalizeBlobAddition(bsetRef, fileHash, BlobLocation.BlobType.FileBlob);
            await BS.TransferBlobAndReferences(dstBS, bsetRef, fileHash, BlobLocation.BlobType.FileBlob, true);
            srcBloc = BS.GetBlobLocation(fileHash);
            dstBloc = dstBS.GetBlobLocation(fileHash);
            Assert.IsTrue(dstBloc.TotalNonShallowReferenceCount == 1);
            Assert.IsTrue(dstBloc.GetBSetReferenceCount(bsetRef) == 1);
            Assert.IsTrue(dstBloc.BlockHashes != null && srcBloc.BlockHashes.Count >= 1);
            Assert.IsTrue(dstBloc.BlockHashes.All(hash => dstBS.GetBlobLocation(hash).GetBSetReferenceCount(bsetRef) == 1));
            Assert.IsTrue((await dstBS.RetrieveData(fileHash)).SequenceEqual(randomFile));
        }

        // TODO: Test transferring and then removing.
        [TestMethod]
        public async Task TestTransferBlobAndReferencesMetadataNode()
        {
            var dstVirtualFS = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            var dstVFSDataStore = new BPlusTree<byte[]>(10);
            var dstBS = new BlobStore(new BlobStoreDependencies(await VirtualFSInterop.InitializeNewDst(dstVirtualFS, dstVFSDataStore, "dst")));

            byte[] randomFile = new byte[100];
            CoreTest.RandomData(randomFile);
            byte[] smallFileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, new BackupSetReference("test", false, false, false), randomFile);
            MetadataNode metadataNode = new(new FileMetadata("src", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, 100, null), null);
            metadataNode.AddFile(new FileMetadata("smallFile", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, randomFile.Length, smallFileHash));

            (byte[] nodehash, BackupCore.Models.HashTreeNode node) = 
                await metadataNode.Store(data => BlobStore.StoreData(new List<BlobStore>(1) { BS }, new BackupSetReference("test", false, false, false), data));
            await BS.TransferBlobAndReferences(dstBS, new BackupSetReference("test", true, false, false), nodehash, BlobLocation.BlobType.MetadataNode, false);
            
            MetadataNode deserializedNode = await MetadataNode.Load(dstBS, nodehash, null);
            Assert.IsTrue(metadataNode.NodeEquals(deserializedNode));
            await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () => await dstBS.RetrieveData(deserializedNode.GetFile("smallFile").FileHash));
            
            await BS.TransferBlobAndReferences(dstBS, new BackupSetReference("test", false, false, false), nodehash, BlobLocation.BlobType.MetadataNode, true);
            Assert.IsTrue((await dstBS.RetrieveData(smallFileHash)).SequenceEqual(randomFile));
        }

        [TestMethod]
        public async Task TestTransferBlobAndReferencesMetadataNodeTree()
        {
            var dstVirtualFS = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            var dstVFSDataStore = new BPlusTree<byte[]>(10);
            var dstBS = new BlobStore(new BlobStoreDependencies(await VirtualFSInterop.InitializeNewDst(dstVirtualFS, dstVFSDataStore, "dst")));

            MetadataNode rootMetadataNode = new(new FileMetadata("src", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, 100, null), null);
            MetadataNode level2MetadataNode = new(new FileMetadata("2", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, 100, null), rootMetadataNode);
            rootMetadataNode.Directories.TryAdd("2", level2MetadataNode);
            MetadataNode level3MetadataNode = new(new FileMetadata("3", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, 100, null), level2MetadataNode);
            level2MetadataNode.Directories.TryAdd("3", level3MetadataNode);
            byte[] randomFile = new byte[100];
            CoreTest.RandomData(randomFile);
            byte[] smallFileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, new BackupSetReference("test", false, false, false), randomFile);
            level3MetadataNode.AddFile(new FileMetadata("smallFile", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, randomFile.Length, smallFileHash));

            (byte[] nodehash, BackupCore.Models.HashTreeNode node) = 
                await rootMetadataNode.Store(data => BlobStore.StoreData(new List<BlobStore>(1) { BS }, new BackupSetReference("test", false, false, false), data));
            await BS.TransferBlobAndReferences(dstBS, new BackupSetReference("test", true, false, false), nodehash, BlobLocation.BlobType.MetadataNode, false);
            
            MetadataNode deserializedNode = await MetadataNode.Load(dstBS, nodehash, null);
            Assert.IsTrue(rootMetadataNode.NodeEquals(deserializedNode));
            MetadataNode? level2Deserialized = deserializedNode.GetDirectory("2");
            Assert.IsNotNull(level2Deserialized);
            Assert.IsTrue(level2Deserialized.NodeEquals(level2MetadataNode));
            MetadataNode? level3Deserialized = level2Deserialized.GetDirectory("3");
            Assert.IsNotNull(level3Deserialized);
            Assert.IsTrue(level3Deserialized.NodeEquals(level3MetadataNode));
            await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () => await dstBS.RetrieveData(level3Deserialized.GetFile("smallFile").FileHash));
            
            await BS.TransferBlobAndReferences(dstBS, new BackupSetReference("test", false, false, false), nodehash, BlobLocation.BlobType.MetadataNode, true);
            Assert.IsTrue((await dstBS.RetrieveData(smallFileHash)).SequenceEqual(randomFile));
        }

        [TestMethod]
        public async Task TestTransferBlobAndReferencesBackupRecord()
        {
            var dstVirtualFS = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            var dstVFSDataStore = new BPlusTree<byte[]>(10);
            var dstBS = new BlobStore(new BlobStoreDependencies(await VirtualFSInterop.InitializeNewDst(dstVirtualFS, dstVFSDataStore, "dst")));

            MetadataNode rootMetadataNode = new(new FileMetadata("src", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, 100, null), null);
            MetadataNode level2MetadataNode = new(new FileMetadata("2", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, 100, null), rootMetadataNode);
            rootMetadataNode.Directories.TryAdd("2", level2MetadataNode);
            MetadataNode level3MetadataNode = new(new FileMetadata("3", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, 100, null), level2MetadataNode);
            level2MetadataNode.Directories.TryAdd("3", level3MetadataNode);
            Random random = new(22);
            byte[] randomFile = new byte[100];
            CoreTest.RandomData(randomFile, random);
            byte[] smallFileHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, new BackupSetReference("test", false, false, false), randomFile);
            level3MetadataNode.AddFile(new FileMetadata("smallFile", DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, randomFile.Length, smallFileHash));
            (byte[] nodehash, BackupCore.Models.HashTreeNode node) = 
                await rootMetadataNode.Store(data => BlobStore.StoreData(new List<BlobStore>(1) { BS }, new BackupSetReference("test", false, false, false), data));
            BackupRecord backupRecord = new("save dis", nodehash, DateTime.Now);

            byte[] backupRecordHash = await BlobStore.StoreData(new List<BlobStore>(1) { BS }, new BackupSetReference("test", false, false, false), backupRecord.Serialize());
            await BS.TransferBlobAndReferences(dstBS, new BackupSetReference("test", true, false, false), backupRecordHash, BlobLocation.BlobType.BackupRecord, false);

            BlobLocation backupRecordLocation = dstBS.GetBlobLocation(backupRecordHash);
            Assert.AreEqual(0, backupRecordLocation.TotalNonShallowReferenceCount);
            Assert.AreEqual(1, backupRecordLocation.TotalReferenceCount);

            BackupRecord deserializedBackupRecord = BackupRecord.Deserialize(await dstBS.RetrieveData(backupRecordHash, backupRecordLocation));
            Assert.AreEqual(backupRecord, deserializedBackupRecord);
            MetadataNode deserializedNode = await MetadataNode.Load(dstBS, deserializedBackupRecord.MetadataTreeHash, null);
            Assert.IsTrue(rootMetadataNode.NodeEquals(deserializedNode));
            MetadataNode? level2Deserialized = deserializedNode.GetDirectory("2");
            Assert.IsNotNull(level2Deserialized);
            Assert.IsTrue(level2Deserialized.NodeEquals(level2MetadataNode));
            MetadataNode? level3Deserialized = level2Deserialized.GetDirectory("3");
            Assert.IsNotNull(level3Deserialized);
            Assert.IsTrue(level3Deserialized.NodeEquals(level3MetadataNode));
            await Assert.ThrowsExceptionAsync<KeyNotFoundException>(async () => await dstBS.RetrieveData(level3Deserialized.GetFile("smallFile").FileHash));

            await BS.TransferBlobAndReferences(dstBS, new BackupSetReference("test", false, false, false), nodehash, BlobLocation.BlobType.MetadataNode, true);
            Assert.IsTrue((await dstBS.RetrieveData(smallFileHash)).SequenceEqual(randomFile));
        }

        public static byte[] ConcenateFile(IEnumerable<byte[]> fileblocks)
        {
            int size = 0;
            foreach (var block in fileblocks)
            {
                size += block.Length;
            }
            byte[] file = new byte[size];
            int index = 0;
            foreach (var block in fileblocks)
            {
                Array.Copy(block, 0, file, index, block.Length);
                index += block.Length;
            }
            return file;
        }
    }
#pragma warning restore CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.
}
