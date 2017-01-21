using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BackupCoreTest;

namespace Testing
{
    class Program
    {
        static void Main(string[] args)
        {
            //BPlusTreeTest bpt_test = new BPlusTreeTest();
            //bpt_test.TestSerializeDeserialize();
            BackupRun();
        }

        static void BackupRun()
        {
            //MakeRandomFile(@"C:\Users\Wesley\Desktop\test\src\random.dat");
            var backupper = new BackupCore.Core(@"C:\Users\Wesley\Desktop\test\src", @"C:\Users\Wesley\Desktop\test\dst");
            //backupper.RunBackupAsync();
            backupper.RunBackupSync();
            Console.Out.WriteLine("Done.");
            Console.In.ReadLine();
            backupper.ReconstructFile("random.dat");
            Console.Out.WriteLine("Done.");
            Console.In.ReadLine();
        }

        static void MakeRandomFile(string path)
        {
            byte[] data = new byte[4000];
            Random rng = new Random();
            rng.NextBytes(data);
            File.WriteAllBytes(path, data);
        }
    }
}
