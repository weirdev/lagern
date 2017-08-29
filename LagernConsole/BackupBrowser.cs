using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using CommandLine;

namespace BackupConsole
{
    class BackupBrowser
    {
        private bool continueloop;

        // Current directory (where user launches from)
        public static string cwd = Environment.CurrentDirectory;
        
        BackupCore.Core BCore { get; set; }
        string BackupHash { get; set; }
        string BackupStoreName { get; set; }
        BackupCore.MetadataNode BackupTree { get; set; }
        BackupCore.MetadataNode CurrentNode { get; set; }
        
        public BackupBrowser(string backupstorename, string backuphash)
        {
            BCore = Program.GetCore();
            Tuple<string, BackupCore.BackupRecord> targetbackuphashandrecord;
            if (backuphash == null)
            {
                targetbackuphashandrecord = BCore.DefaultBackups.GetBackupHashAndRecord(backupstorename);
            }
            else
            {
                targetbackuphashandrecord = BCore.DefaultBackups.GetBackupHashAndRecord(backupstorename, backuphash, 0);
            }
            BackupHash = targetbackuphashandrecord.Item1;
            BackupStoreName = backupstorename;
            BackupCore.BackupRecord backuprecord = targetbackuphashandrecord.Item2;
            BackupTree = BackupCore.MetadataNode.Load(BCore.DefaultBlobs, backuprecord.MetadataTreeHash);
            CurrentNode = BackupTree;
        }

        [Verb("cd", HelpText = "Change the current directory")]
        class CDOptions
        {
            [Value(0, Required = true, HelpText = "The path of the directory to change to")]
            public string Directory { get; set; }
        }

        [Verb("ls", HelpText = "List the contents of a directory")]
        class LSOptions { }

        [Verb("exit", HelpText = "Exit the command loop")]
        class ExitOptions { }

        [Verb("cb", HelpText = "Change the backup being browsed")]
        class CBOptions
        {
            [Option('o', "offset", Required = false, Default = 0, HelpText = "The number of backups foward or backward to change")]
            public int Offset { get; set; }

            [Option('b', "backup", Required = false, Default = null, HelpText = "The hash of the backup to switch to.")]
            public string Backup { get; set; }
        }

        public void CommandLoop()
        {
            continueloop = true;
            while (continueloop)
            {
                int hashdisplen = BackupHash.Length <= 6 ? BackupHash.Length : 6;
                string cachewarning = "";
                if (BCore.DefaultBlobs.IsCache)
                {
                    cachewarning = "(cache)";
                }
                Console.Write(String.Format("backup {0}{1}:{2}> ", BackupHash.Substring(0, hashdisplen), cachewarning, CurrentNode.Path));
                string command = Console.ReadLine();
                string[] args;
                if (command != "")
                {
                    args = SplitArguments(command);
                }
                else
                {
                    args = new string[0];
                }
                Parser.Default.ParseArguments<CDOptions, LSOptions, ExitOptions, Program.RestoreOptions, CBOptions, Program.ListNoNameOptions>(args)
                    .WithParsed<CDOptions>(opts => ChangeDirectory(opts))
                    .WithParsed<LSOptions>(opts => ListDirectory())
                    .WithParsed<ExitOptions>(opts => Exit())
                    .WithParsed<Program.RestoreOptions>(opts => Program.RestoreFile(opts))
                    .WithParsed<CBOptions>(opts => ChangeBackup(opts))
                    .WithParsed<Program.ListNoNameOptions>(opts => Program.ListBackups(opts, BackupStoreName, BCore));
            }
        }

        /// <summary>
        /// Splits a command string, respecting args enclosed in double quotes
        /// </summary>
        /// <param name="commandLine"></param>
        /// <returns></returns>
        static string[] SplitArguments(string commandLine)
        {
            char[] parmChars = commandLine.ToCharArray();
            bool inQuote = false;
            for (int index = 0; index < parmChars.Length; index++)
            {
                if (parmChars[index] == '"')
                    inQuote = !inQuote;
                if (!inQuote && parmChars[index] == ' ')
                    parmChars[index] = '\n';
            }
            string split = new string(parmChars);
            while (split.Contains("\n\n"))
            {
                split = split.Replace("\n\n", "\n");
            }
            split = split.Replace("\"", "");
            return split.Split('\n');
        }

        private  string GetBUDestinationDir()
        {
            string dir = cwd;
            do
            {
                if (Directory.Exists(Path.Combine(dir, "backup")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            } while (dir != null);
            return null;
        }

        private void ListDirectory()
        {
            BackupCore.MetadataNode dir = CurrentNode;

            List<BackupCore.MetadataNode> childdirectories = new List<BackupCore.MetadataNode>(from cdir in dir.Directories select cdir.Value);
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

        private void ChangeDirectory(CDOptions opts)
        {
            BackupCore.MetadataNode dir;
            try
            {
                if (opts.Directory.StartsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    dir = BackupTree.GetDirectory(opts.Directory);
                }
                else
                {
                    dir = BackupTree.GetDirectory(Path.Combine(CurrentNode.Path, opts.Directory));
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

        private void ChangeBackup(CBOptions opts)
        {
            string curpath = CurrentNode.Path;
            string backuphash = opts.Backup;
            if (opts.Backup == null)
            {
                backuphash = BackupHash;
            }
            var targetbackuphashandrecord = BCore.DefaultBackups.GetBackupHashAndRecord(backuphash, opts.Offset);
            BackupHash = targetbackuphashandrecord.Item1;
            BackupCore.BackupRecord backuprecord = targetbackuphashandrecord.Item2;
            BackupTree = BackupCore.MetadataNode.Load(BCore.DefaultBlobs, backuprecord.MetadataTreeHash);
            CurrentNode = BackupTree.GetDirectory(curpath);
            if (CurrentNode == null)
            {
                CurrentNode = BackupTree;
            }
            Console.WriteLine("Switching to backup {0}: \"{1}\"", BackupHash.Substring(0, 6), backuprecord.BackupMessage);
        }

        private void Exit()
        {
            Environment.Exit(0);
        }
    }
}
