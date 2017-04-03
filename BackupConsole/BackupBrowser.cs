using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace BackupConsole
{
    class BackupBrowser
    {
        // Current directory (where user launches from)
        public static string cwd = Environment.CurrentDirectory;

        private static readonly ArgumentScanner browse_scanner = BrowseArgScannerFactory();

        BackupCore.Core BCore { get; set; }
        string BackupHash { get; set; }
        BackupCore.MetadataTree BackupTree { get; set; }
        BackupCore.MetadataNode CurrentNode { get; set; }

        public BackupBrowser(string backuphash)
        {
            string destination = Program.ReadSetting("dest");
            if (destination == null)
            {
                if (Directory.Exists("backup")) // We are in a backup destination
                {
                    destination = cwd;
                }
                else
                {
                    Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                    Console.WriteLine("or this command must be run from an existing backup destination.");
                    return;
                }
            }
            BCore = new BackupCore.Core(Program.cwd, destination);
            Tuple<string, BackupCore.BackupRecord> targetbackuphashandrecord;
            if (backuphash == null)
            {
                targetbackuphashandrecord = BCore.BUStore.GetFirstBackupHashAndRecord();
            }
            else
            {
                targetbackuphashandrecord = BCore.BUStore.GetBackupHashAndRecord(backuphash, 0);
            }
            BackupHash = targetbackuphashandrecord.Item1;
            BackupCore.BackupRecord backuprecord = targetbackuphashandrecord.Item2;
            BackupTree = BackupCore.MetadataTree.deserialize(BCore.Blobs.GetBlob(backuprecord.MetadataTreeHash));
            CurrentNode = BackupTree.Root;
        }

        public void CommandLoop()
        {
            while (true)
            {
                int hashdisplen = BackupHash.Length <= 6 ? BackupHash.Length : 6;
                Console.Write(String.Format("backup {0}:{1}> ", BackupHash.Substring(0, hashdisplen), CurrentNode.Path));
                string command = Console.ReadLine();
                string[] args = command.Split();
                try
                {
                    var parsed = browse_scanner.ParseInput(args);
                    if (parsed.Item1 == "cd")
                    {
                        // "cd <directory>"
                        ChangeDirectory(parsed.Item2["directory"]);
                    }
                    else if (parsed.Item1 == "ls")
                    {
                        // "ls"
                        ListDirectory();
                    }
                    else if (parsed.Item1 == "exit")
                    {
                        // "exit"
                        return;
                    }
                    else if (parsed.Item1 == "restore")
                    {
                        // "restore <filerelpath> [-r <>]"
                        string filerelpath = Path.Combine(CurrentNode.Path, parsed.Item2["filerelpath"]);
                        // If no restoreto path given, restore
                        // to cwd / its relative path
                        string restorepath = Path.Combine(Program.cwd, filerelpath);
                        if (parsed.Item3.ContainsKey("r"))
                        {
                            if (parsed.Item3["r"] == ".")
                            {
                                restorepath = Path.Combine(Program.cwd, Path.GetFileName(filerelpath));
                            }
                            else
                            {
                                restorepath = Path.Combine(parsed.Item3["r"], Path.GetFileName(filerelpath));
                            }
                        }
                        Program.RestoreFile(filerelpath, restorepath, BackupHash);
                    }
                    else if (parsed.Item1 == "cb")
                    {
                        // "cb [<backuphash>] [-o <>]"
                        string backuphash = BackupHash;
                        int offset = 0;
                        if (!parsed.Item2.ContainsKey("backuphash") && !parsed.Item3.ContainsKey("o"))
                        {
                            ShowCommands();
                            return;
                        }
                        if (parsed.Item2.ContainsKey("backuphash"))
                        {
                            backuphash = parsed.Item2["backuphash"];
                        }
                        if (parsed.Item3.ContainsKey("o"))
                        {
                            offset = Convert.ToInt32(parsed.Item3["o"]);
                        }
                        ChangeBackup(backuphash, offset);
                    }
                    else if (parsed.Item1 == "list")
                    {
                        // "list [<listcount>] [-s]"
                        int listcount = -1;
                        if (parsed.Item2.ContainsKey("listcount"))
                        {
                            listcount = Convert.ToInt32(parsed.Item2["listcount"]);
                        }
                        bool calculatesizes = parsed.Item3.ContainsKey("s");
                        Program.ListBackups(BCore, parsed.Item3.ContainsKey("s"), listcount);
                    }
                    else if (parsed.Item1 == "help")
                    {
                        ShowCommands();
                    }
                }
                catch (Exception)
                {
                    ShowCommands();
                }
            }
        }

        private static ArgumentScanner BrowseArgScannerFactory()
        {
            ArgumentScanner scanner = new ArgumentScanner();
            scanner.AddCommand("cd <directory>");
            scanner.AddCommand("ls");
            scanner.AddCommand("restore <filerelpath> [-r <>]");
            scanner.AddCommand("exit");
            scanner.AddCommand("list [<listcount>] [-s]");
            scanner.AddCommand("help");
            scanner.AddCommand("cb [<backuphash>] [-o <>]");
            return scanner;
        }

        private static void ShowCommands()
        {
            foreach (var command in browse_scanner.CommandStrings)
            {
                Console.WriteLine(command);
            }
        }

        private void ListDirectory()
        {
            BackupCore.MetadataNode dir = CurrentNode;

            List<BackupCore.MetadataNode> childdirectories = new List<BackupCore.MetadataNode>(from cdir in dir.Directories where cdir.Key!=".." select cdir.Value);
            childdirectories.Sort(delegate (BackupCore.MetadataNode x, BackupCore.MetadataNode y)
            {
               return x.DirMetadata.FileName.CompareTo(y.DirMetadata.FileName);
            });
            foreach (var childdir in childdirectories)
            {
                Console.WriteLine(childdir.DirMetadata.FileName + Path.DirectorySeparatorChar.ToString());
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

        private void ChangeDirectory(string directory)
        {
            BackupCore.MetadataNode dir;
            try
            {
                if (directory.StartsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    dir = BackupTree.GetDirectory(directory);
                }
                else
                {
                    dir = BackupTree.GetDirectory(Path.Combine(CurrentNode.Path, directory));
                }
            }
            catch (Exception)
            {
                dir = null;
            }
            if (dir == null)
            {
                Console.WriteLine("Cannot find directory specified");
                return;
            }
            CurrentNode = dir;
        }

        private void ChangeBackup(string backuphash, int offset=0)
        {
            string curpath = CurrentNode.Path;
            var targetbackuphashandrecord = BCore.BUStore.GetBackupHashAndRecord(backuphash, offset);
            BackupHash = targetbackuphashandrecord.Item1;
            BackupCore.BackupRecord backuprecord = targetbackuphashandrecord.Item2;
            BackupTree = BackupCore.MetadataTree.deserialize(BCore.Blobs.GetBlob(backuprecord.MetadataTreeHash));
            CurrentNode = BackupTree.GetDirectory(curpath);
            if (CurrentNode == null)
            {
                CurrentNode = BackupTree.Root;
            }
            Console.WriteLine("Switching to backup {0}: \"{1}\"", BackupHash.Substring(0, 6), backuprecord.BackupMessage);
        }
    }
}
