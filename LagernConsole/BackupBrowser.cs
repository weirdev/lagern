using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CommandLine;
using LagernCore.Models;
using System.Threading.Tasks;

namespace BackupConsole
{
    class BackupBrowser
    {
        // Current directory (where user launches from)
        public static string cwd = Environment.CurrentDirectory;
        
        BackupCore.Core BCore { get; set; }
        string BackupHash { get; set; }
        string BackupSet { get; set; }
        BackupCore.MetadataNode BackupTree { get; set; }
        BackupCore.MetadataNode CurrentNode { get; set; }
        bool ContinueLoop { get; set; }
        
        int BackupDst { get; set; }
        
        private BackupBrowser(BackupSetReference backupSetReference, string hash, BackupCore.Core bcore, BackupCore.MetadataNode backupTree, int backupdst=0)
        {
            ContinueLoop = true;
            BCore = bcore;
            BackupDst = backupdst;
            BackupHash = hash;
            BackupSet = backupSetReference.BackupSetName;
            BackupTree = backupTree;
            CurrentNode = backupTree;
        }

        public static async Task<BackupBrowser> Initialize(string backupset, string? backuphash, BackupCore.Core bcore, int backupdst = 0)
        {
            BackupSetReference backupSetReference = new(backupset, false, false, false);
            if (!bcore.DestinationAvailable)
            {
                backupSetReference = backupSetReference with { Cache = true };
            }
            (string hash, BackupCore.BackupRecord record) targetbackuphashandrecord;
            if (backuphash == null)
            {
                targetbackuphashandrecord = await bcore.DefaultDstDependencies[backupdst].Backups.GetBackupHashAndRecord(backupSetReference);
            }
            else
            {
                targetbackuphashandrecord = await bcore.DefaultDstDependencies[backupdst].Backups.GetBackupHashAndRecord(backupSetReference, backuphash, 0);
            }
            BackupCore.BackupRecord backuprecord = targetbackuphashandrecord.record;
            var backupTree = await BackupCore.MetadataNode.Load(bcore.DefaultDstDependencies[backupdst].Blobs, backuprecord.MetadataTreeHash);
            return new BackupBrowser(backupSetReference, targetbackuphashandrecord.hash, bcore, backupTree, backupdst);
        }

        [Verb("cd", HelpText = "Change the current directory")]
        class CDOptions
        {
            [Value(0, Required = true, HelpText = "The path of the directory to change to")]
            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string Directory { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        }

        [Verb("ls", HelpText = "List the contents of a directory")]
        class LSOptions { }
        
        [Verb("cb", HelpText = "Change the backup being browsed")]
        class CBOptions
        {
            [Option('o', "offset", Required = false, Default = 0, HelpText = "The number of backups foward or backward to change")]
            public int Offset { get; set; }

            [Option('b', "backup", Required = false, Default = null, HelpText = "The hash of the backup to switch to.")]
            public string? Backup { get; set; }
        }

        public void CommandLoop()
        {
            while (ContinueLoop)
            {
                int hashdisplen = BackupHash.Length <= 6 ? BackupHash.Length : 6;
                string cachewarning = "";
                if (!BCore.DestinationAvailable)
                {
                    cachewarning = "(cache)";
                }
                Console.Write(string.Format("backup {0}{1}:{2}> ", BackupHash[..hashdisplen], cachewarning, CurrentNode.Path));
                string[] args = LagernConsole.ReadArgs();
                try
                {
                    Parser.Default.ParseArguments<CDOptions, LSOptions, LagernConsole.ExitOptions, LagernConsole.RestoreOptions, CBOptions, LagernConsole.ListNoNameOptions>(args)
                        .WithParsed<CDOptions>(opts => ChangeDirectory(opts))
                        .WithParsed<LSOptions>(opts => ListDirectory())
                        .WithParsed<LagernConsole.ExitOptions>(opts => ContinueLoop = false)
                        .WithParsed<LagernConsole.RestoreOptions>(opts => LagernConsole.RestoreFile(opts).Wait())
                        .WithParsed<CBOptions>(opts => ChangeBackup(opts).Wait())
                        .WithParsed<LagernConsole.ListNoNameOptions>(opts => LagernConsole.ListBackups(opts, BackupSet, BCore).Wait());
                }
                catch (ChangeBackupException ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private static string? GetBUDestinationDir()
        {
            string? dir = cwd;
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

            List<BackupCore.MetadataNode> childdirectories = new(from cdir in dir.Directories select cdir.Value);
            childdirectories.Sort(delegate (BackupCore.MetadataNode x, BackupCore.MetadataNode y)
            {
               return x.DirMetadata.FileName.CompareTo(y.DirMetadata.FileName);
            });
            foreach (var childdir in childdirectories)
            {
                Console.WriteLine(childdir.DirMetadata.FileName + Path.DirectorySeparatorChar.ToString());
            }

            List<BackupCore.FileMetadata> childfiles = new(dir.Files.Values);
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
            BackupCore.MetadataNode? dir;
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

        private class ChangeBackupException : Exception
        {
            public ChangeBackupException(string message) : base(message) { }
        }

        private async Task ChangeBackup(CBOptions opts)
        {
            string curpath = CurrentNode.Path;
            string backuphash;
            if (opts.Backup == null)
            {
                if (opts.Offset == 0)
                {
                    throw new ChangeBackupException("Must set either or both backup or offset.");
                }
                backuphash = BackupHash;
            } else
            {
                backuphash = opts.Backup;
            }
            var targetbackuphashandrecord = await BCore.DefaultDstDependencies[BackupDst].Backups.GetBackupHashAndRecord(new BackupSetReference(BackupSet, false, false, false), backuphash, opts.Offset);
            BackupHash = targetbackuphashandrecord.Item1;
            BackupCore.BackupRecord backuprecord = targetbackuphashandrecord.Item2;
            BackupTree = await BackupCore.MetadataNode.Load(BCore.DefaultDstDependencies[BackupDst].Blobs, backuprecord.MetadataTreeHash);
            BackupCore.MetadataNode ? curnode = BackupTree.GetDirectory(curpath);
            if (curnode != null)
            {
                CurrentNode = curnode;
            } 
            else
            {
                CurrentNode = BackupTree;
            }
            Console.WriteLine("Switching to backup {0}: \"{1}\"", BackupHash[..6], backuprecord.BackupMessage);
        }
    }
}
