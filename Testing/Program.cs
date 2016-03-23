using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Testing
{
    class Program
    {
        static void Main(string[] args)
        {
            var backupper = new BackupCore.Core(@"C:\Users\Wesley\Desktop\test\src", @"C:\Users\Wesley\Desktop\test\dst");
            backupper.RunBackup();
            Console.Out.WriteLine("Done.");
            Console.In.ReadLine();
            backupper.ReconstructFile("Testing.docx");
            Console.Out.WriteLine("Done.");
            Console.In.ReadLine();
        }
    }
}
