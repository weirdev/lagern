using BackupCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CoreTest.CoreTests
{
    [TestClass]
    public class MultipleDestinationTests
    {
        [TestMethod]
        public void MultiDestinationTest()
        {
            Random r = new(420);
            var source = CreateNewSrc(r);

            var destinations = Enumerable.Range(0, 5)
                                         .Select(_ => CreateNewDst(r).Result)
                                         .ToList();

            // Backup to destination #1
            Core core = new(source, destinations.GetRange(0, 1));
            core.RunBackup("test", "to destination 1");

            // Add some more data
            var (hash, file) = CoreTest.MakeRandomFile(1000, r);
            source.OverwriteOrCreateFile("mdTestFile", file);

            // Backup to all
            core = new(source, destinations);
            core.RunBackup("test", "to all destinations");

            // Remove a file
            source.DeleteFile("mdTestFile");

            // Backup to destination #2
            core = new(source, destinations.GetRange(1, 1));
            core.RunBackup("test", "to destination 2");

            // Backup to all again
            core = new(source, destinations);
            core.RunBackup("test", "to all destinations again");

            System.Collections.Generic.List<BackupRecord> backupRecords = destinations[3].Backups.GetAllBackupRecords(
                new LagernCore.Models.BackupSetReference("test", false, false, false)).Result;

            Assert.IsTrue(backupRecords.Count == 2);
        }

        private static async Task<ICoreDstDependencies> CreateNewDst(Random random)
        {
            DateTime dateTime = CoreTest.RandomDateTime(random);
            MetadataNode vfsroot = new(VirtualFSInterop.MakeNewDirectoryMetadata("dst", dateTime), null);

            IDstFSInterop dstFSInterop = await VirtualFSInterop.InitializeNewDst(vfsroot, new BPlusTree<byte[]>(10), "");

            return CoreDstDependencies.InitializeNew("test", false, dstFSInterop);
        }

        private static ICoreSrcDependencies CreateNewSrc(Random random)
        {
            DateTime dateTime = CoreTest.RandomDateTime(random);
            MetadataNode vfsroot = new(VirtualFSInterop.MakeNewDirectoryMetadata("src", dateTime), null);
            BPlusTree<byte[]> datastore = new(10);

            CoreTest.AddStandardVFSFiles(vfsroot, datastore, random);

            IFSInterop srcFSInterop = new VirtualFSInterop(vfsroot, datastore);

            return FSCoreSrcDependencies.InitializeNew("test", "", srcFSInterop);
        }
    }
}
