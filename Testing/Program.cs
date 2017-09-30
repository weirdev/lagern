using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreTest;
using System.Diagnostics;

namespace Testing
{
    class Program
    {
        static void Main(string[] args)
        {
            //BPlusTreeTest bpt_test = new BPlusTreeTest();
            //bpt_test.TestSerializeDeserialize();

            GetStatus("test", @"C:\Users\Wesley\Desktop\test\src", @"C:\Users\Wesley\Desktop\test\dst");
            BackupRun("test", @"C:\Users\Wesley\Desktop\test\src", @"C:\Users\Wesley\Desktop\test\dst");

            //BinaryEncodingTest betest = new BinaryEncodingTest();
            //betest.TestDictEncodeDecode();
            //betest.TestEnumEncodeDecode();

            //BlobStoreTest bstest = new BlobStoreTest();
            //bstest.TestSplitData();

            //CoreTest ctest = new CoreTest();
            //ctest.TestCheckTrackFile();
            //ctest.TestCheckTrackAnyDirectoryChild();

            //MakeManyFiles(1000, 1000000, @"D:\src");
            //Console.WriteLine(TimeSimpleCopy(@"D:\src", @"D:\dst"));

            //Console.WriteLine(BackupRun("test", @"D:\src", @"D:\dst"));
            GetStatus("test", @"C:\Users\Wesley\Desktop\test\src", @"C:\Users\Wesley\Desktop\test\dst");
            Console.ReadLine();
        }

        static double BackupRun(string bsname, string src, string dst)
        {
            var backupper = new BackupCore.Core(src, dst); // Dont count initial setup in time
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
            var core = new BackupCore.Core(src, dst);
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
                MakeRandomFile(Path.Combine(dst, (i + startnum).ToString()), size);
            }
        }

        static void MakeRandomFile(string path, int size)
        {
            byte[] data = new byte[size];
            Random rng = new Random();
            rng.NextBytes(data);
            File.WriteAllBytes(path, data);
        }
    }
}
