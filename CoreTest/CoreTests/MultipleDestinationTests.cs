using BackupCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CoreTest
{
    [TestClass]
    public class MultipleDestinationTests
    {
        [TestMethod]
        public async Task MultiDestinationTest()
        {
            Random r = new(420);
            var source = await CreateNewSrc(r);

            var destinations = (await Task.WhenAll(Enumerable.Range(0, 5)
                                         .Select(_ => CreateNewDst(r))))
                                         .ToList();

            // Backup to destination #1
            Core core = new(source, destinations.GetRange(0, 1));
            await core.RunBackup("test", "to destination 1");

            // Add some more data
            var (hash, file) = CoreTest.MakeRandomFile(1000, r);
            await source.OverwriteOrCreateFile("mdTestFile", file);

            // Backup to all
            core = new(source, destinations);
            await core.RunBackup("test", "to all destinations");

            // Remove a file
            await source.DeleteFile("mdTestFile");

            // Backup to destination #2
            core = new(source, destinations.GetRange(1, 1));
            await core.RunBackup("test", "to destination 2");

            // Backup to all again
            core = new(source, destinations);
            await core.RunBackup("test", "to all destinations again");

            System.Collections.Generic.List<BackupRecord> backupRecords = await destinations[3].Backups.GetAllBackupRecords(
                new LagernCore.Models.BackupSetReference("test", false, false, false));

            Assert.IsTrue(backupRecords.Count == 2);
        }

        private static async Task<ICoreDstDependencies> CreateNewDst(Random random)
        {
            DateTime dateTime = CoreTest.RandomDateTime(random);
            MetadataNode vfsroot = new(VirtualFSInterop.MakeNewDirectoryMetadata("dst", dateTime), null);

            IDstFSInterop dstFSInterop = await VirtualFSInterop.InitializeNewDst(vfsroot, new BPlusTree<byte[]>(10), "");

            return await CoreDstDependencies.InitializeNew("test", false, dstFSInterop);
        }

        private static async Task<ICoreSrcDependencies> CreateNewSrc(Random random)
        {
            DateTime dateTime = CoreTest.RandomDateTime(random);
            MetadataNode vfsroot = new(VirtualFSInterop.MakeNewDirectoryMetadata("src", dateTime), null);
            BPlusTree<byte[]> datastore = new(10);

            CoreTest.AddStandardVFSFiles(vfsroot, datastore, random);

            IFSInterop srcFSInterop = new VirtualFSInterop(vfsroot, datastore);

            return await FSCoreSrcDependencies.InitializeNew("test", "", srcFSInterop);
        }
    }
}
