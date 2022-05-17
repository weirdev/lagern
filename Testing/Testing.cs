using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Testing
{
    class Testing
    {
        public static async Task Main(string[] args)
        {
            //BPlusTreeTest bpt_test = new BPlusTreeTest();
            //bpt_test.TestSerializeDeserialize();

            //GetStatus("test", @"C:\Users\Wesley\Desktop\test\src", @"C:\Users\Wesley\Desktop\test\dst");
            //BackupRun("test", @"C:\Users\Wesley\Desktop\test\src", @"C:\Users\Wesley\Desktop\test\dst");

            //BinaryEncodingTest betest = new BinaryEncodingTest();
            //betest.TestDictEncodeDecode();
            //betest.TestEnumEncodeDecode();

            //BlobStoreTest bstest = new BlobStoreTest();
            //bstest.TestSplitData();
            //bstest.TestBlobStoreDeserialize();

            CoreTest.CoreTest ctest = new();
            //ctest.TestCheckTrackFile();
            //ctest.TestCheckTrackAnyDirectoryChild();
            //ctest.TestInitializeNew();
            //ctest.TestRunBackup();
            //ctest.TestRestore();
            await ctest.TestRemoveBackup();
            //ctest.TestInitializeNew();
            //ctest.TestLoadCore_NewlyInitialized();

            //BPlusTreeTest bptt = new BPlusTreeTest();
            //bptt.TestAddRemove();
            //bptt.TestAddMany();
            //bptt.TestAddRemoveMany();

            //HashToolsTest htt = new HashToolsTest();
            //htt.TestByteArrayLessThan();

            //MakeManyFiles(1000, 1000000, @"D:\src");
            //Console.WriteLine(TimeSimpleCopy(@"D:\src", @"D:\dst"));

            //Console.WriteLine(BackupRun("test", @"D:\src", @"D:\dst"));
            //GetStatus("test", @"C:\Users\Wesley\Desktop\test\src", @"C:\Users\Wesley\Desktop\test\dst");

            //var bbi = new BackupCore.BackblazeInterop();
            //(var hash, var data) = MakeRandomFile(30);
            //Console.WriteLine(await bbi.FileExists("hashindex"));
            //Console.WriteLine((await bbi.DownloadFile("hashindex")).Length);
            //Console.ReadLine();
            
            /*
            BackupCore.FSCoreSrcDependencies srcdeps = BackupCore.FSCoreSrcDependencies.Load(@"C:\Users\Wesley\Desktop\test\src", new BackupCore.DiskFSInterop());
            BackupCore.BackblazeCoreDstDependencies bbdestdeps = BackupCore.BackblazeCoreDstDependencies.Load(new BackupCore.BackblazeInterop("BBConnection.json"), false);
            BackupCore.Core core = new BackupCore.Core(srcdeps, bbdestdeps);
            core.RunBackup("test", "try");
            Console.ReadLine();
            Console.WriteLine(core.GetBackups("test").backups.First().message);
            Console.ReadLine(); */
        }

        static async Task<double> BackupRun(string bsname, string src, string dst)
        {
            var backupper = await BackupCore.Core.LoadDiskCore(src, new List<(string, string?)>(1) { (dst, null) }); // Dont count initial setup in time
            Stopwatch stopwatch = Stopwatch.StartNew();
            //MakeRandomFile(@"C:\Users\Wesley\Desktop\test\src\random.dat");
            
            await backupper.RunBackup(bsname, "", true);
            //backupper.RunBackupSync(null);
            
            //Console.Out.WriteLine("Done.");
            //Console.In.ReadLine();
            //backupper.RestoreFileOrDirectory("moreshit.txt", Path.Combine(backupper.BackuppathDst, "moreshit.txt"));
            //Console.Out.WriteLine("Done.");
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds / 1000.0;
        }

        static async Task GetStatus(string bsname, string src, string dst)
        {
            var core = await BackupCore.Core.LoadDiskCore(src, new List<(string, string?)>(1) { (dst, null) });
            foreach (var (path, change) in await core.GetWTStatus(bsname, core.DefaultDstDependencies[0]))
            {
                Console.WriteLine(string.Format("{0}:\t{1}", path, change));
            }
        }

        static double TimeSimpleCopy(string src, string dst)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string[] files = Directory.GetFiles(src);
            foreach (var file in files)
            {
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
            }
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds / 1000.0;
        }

        static void MakeManyFiles(int count, int size, string dst, int startnum=0)
        {
            for (int i = 0; i < count; i++)
            {
                SaveRandomFile(Path.Combine(dst, (i + startnum).ToString()), size);
            }
        }

        static void SaveRandomFile(string path, int size)
        {
            (_, byte[] data) = MakeRandomFile(size);
            File.WriteAllBytes(path, data);
        }

        static (byte[] hash, byte[] data) MakeRandomFile(int size)
        {
            byte[] data = new byte[size];
            Random rng = new Random();
            rng.NextBytes(data);
            byte[] hash = BackupCore.HashTools.GetSHA1Hasher().ComputeHash(data);
            return (hash, data);
        }
    }
}
