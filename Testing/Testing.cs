using System;
using System.IO;
using CoreTest;
using System.Diagnostics;
using System.Linq;

namespace Testing
{
    class Testing
    {
        static void Main(string[] args)
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

            CoreTest.CoreTest ctest = new CoreTest.CoreTest();
            //ctest.TestCheckTrackFile();
            //ctest.TestCheckTrackAnyDirectoryChild();
            //ctest.TestInitializeNew();
            //ctest.TestRunBackup();
            //ctest.TestRestore();
            ctest.TestRemoveBackup();
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
            //Console.WriteLine(bbi.FileExists("hashindex").Result);
            //Console.WriteLine(bbi.DownloadFile("hashindex").Result.Length);
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

        static double BackupRun(string bsname, string src, string dst)
        {
            var backupper = BackupCore.Core.LoadDiskCore(src, dst); // Dont count initial setup in time
            Stopwatch stopwatch = Stopwatch.StartNew();
            //MakeRandomFile(@"C:\Users\Wesley\Desktop\test\src\random.dat");
            
            backupper.RunBackup(bsname, null, true);
            //backupper.RunBackupSync(null);
            
            //Console.Out.WriteLine("Done.");
            //Console.In.ReadLine();
            //backupper.RestoreFileOrDirectory("moreshit.txt", Path.Combine(backupper.BackuppathDst, "moreshit.txt"));
            //Console.Out.WriteLine("Done.");
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds / 1000.0;
        }

        static void GetStatus(string bsname, string src, string dst)
        {
            var core = BackupCore.Core.LoadDiskCore(src, dst);
            foreach (var item in core.GetWTStatus(bsname))
            {
                Console.WriteLine(string.Format("{0}:\t{1}", item.path, item.change));
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
