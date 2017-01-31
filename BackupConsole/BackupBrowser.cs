using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupConsole
{
    class BackupBrowser
    {
        private static readonly ArgumentScanner browse_scanner = BrowseArgScannerFactory();

        BackupCore.Core BCore;
        int BackupIndex;
        string relpath = ".";

        public BackupBrowser(int backupindex)
        {
            BackupIndex = backupindex;
            string destination = Program.ReadSetting("dest");
            if (destination == null)
            { 
                Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                return;
            }
            BCore = new BackupCore.Core(Program.cwd, destination);

        }

        public void CommandLoop()
        {
            while (true)
            {
                Console.Write(String.Format("backup {0}:{1}> ", BackupIndex, relpath));
                string command = Console.ReadLine();
                string[] args = command.Split();
                try
                {
                    var parsed = browse_scanner.ParseInput(args);
                    if (parsed.Item1 == "ls")
                    {
                        // "ls"
                        ListDirectory(relpath);
                    }
                }
                catch (Exception)
                {

                    throw;
                }

            }
        }

        private static ArgumentScanner BrowseArgScannerFactory()
        {
            ArgumentScanner scanner = new ArgumentScanner();
            scanner.AddCommand("ls");
            scanner.AddCommand("help");
            return scanner;
        }

        private void ListDirectory(string relpath)
        {
            BackupCore.MetadataNode dir = BCore.GetDirectory(BackupIndex, relpath);

            List<BackupCore.MetadataNode> childdirectories = new List<BackupCore.MetadataNode>(dir.Directories.Values);
            childdirectories.Sort(delegate (BackupCore.MetadataNode x, BackupCore.MetadataNode y)
            {
               return x.DirMetadata.FileName.CompareTo(y.DirMetadata.FileName);
            });
            foreach (var childdir in childdirectories)
            {
                Console.WriteLine(childdir.DirMetadata.FileName + "\\");
            }

            List<BackupCore.FileMetadata> childfiles = new List<BackupCore.FileMetadata>(dir.Files.Values);
            childfiles.Sort(delegate (BackupCore.FileMetadata x, BackupCore.FileMetadata y)
            {
                return x.FileName.CompareTo(y.FileName);
            });
            foreach (var childfile in childfiles)
            {
                Console.WriteLine(childfile.FileName);
            }
        }
    }
}
