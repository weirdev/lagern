using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CommandLine;

namespace BackupConsole
{
    // TODO: Replace custom parsing with CommandLine nuget library
    // https://github.com/gsscoder/commandline
    public class Program
    {
        // Current directory (where user launches from)
        public static string cwd = Environment.CurrentDirectory;
        
        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<ShowOptions, SetOptions, ClearOptions,
                RunOptions, DeleteOptions, RestoreOptions, ListOptions, BrowseOptions,
                TransferOptions, SyncCacheOptions>(args)
              .WithParsed<ShowOptions>(opts => ShowSettings(opts))
              .WithParsed<SetOptions>(opts => SetSetting(opts))
              .WithParsed<ClearOptions>(opts => ClearSetting(opts))
              .WithParsed<RunOptions>(opts => RunBackup(opts))
              .WithParsed<DeleteOptions>(opts => DeleteBackup(opts))
              .WithParsed<RestoreOptions>(opts => RestoreFile(opts))
              .WithParsed<ListOptions>(opts => ListBackups(opts))
              .WithParsed<BrowseOptions>(opts => BrowseBackup(opts))
              .WithParsed<TransferOptions>(opts => TransferBackupStore(opts))
              .WithParsed<SyncCacheOptions>(opts => SyncCache(opts));
        }

        [Verb("show", HelpText = "Show lagern settings")]
        class ShowOptions
        {
            [Value(0, Required = false, HelpText = "The setting to show")]
            public string Setting { get; set; }
        }

        [Verb("set", HelpText = "Set a lagern setting")]
        class SetOptions
        {
            [Value(0, Required = true, HelpText = "The setting to set")]
            public string Setting { get; set; }

            [Value(0, Required = true, HelpText = "The value to give setting")]
            public string Value { get; set; }
        }

        [Verb("clear", HelpText = "Clear a lagern setting")]
        class ClearOptions
        {
            [Value(0, Required = true, HelpText = "The setting to clear")]
            public string Setting { get; set; }
        }

        [Verb("run", HelpText = "Run a backup.")]
        class RunOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup store")]
            public string BSName { get; set; }

            [Option('b', "backup", Required = false, SetName = "differential", HelpText = "The hash of the backup to use for a differential backup")]
            public string BackupHash { get; set; }

            [Option('s', "scan", SetName = "differential", HelpText = "Forces scan of all files (makes backup non-differential)")]
            public bool Scan { get; set; }

            [Option('m', "message", Default = "", HelpText = "A message describing the backup")]
            public string Message { get; set; }
        }

        [Verb("delete", HelpText = "Delete a backup")]
        class DeleteOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup store")]
            public string BSName { get; set; }

            [Value(0, Required = true, HelpText = "The backup hash (or its prefix) of the backup to be deleted")]
            public string BackupHash { get; set; }
        }

        [Verb("restore", HelpText = "Restore a file or directory")]
        public class RestoreOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup store")]
            public string BSName { get; set; }

            [Option('b', "backup", Required = false, HelpText = "The hash of the backup to restore from, defaults to most recent backup")]
            public string BackupHash { get; set; }

            [Option('r', "restorepath", Required = false, HelpText = "The path which to restore the file or directory, defaults to current directory")]
            public string RestorePath { get; set; }

            [Value(0, Required = true, HelpText = "The path, relative to the backup root of the file or directory to restore")]
            public string Path { get; set; }
        }

        [Verb("list", HelpText = "List saved backups")]
        public class ListNoNameOptions
        {
            [Option('s', "showsizes", Required = false, HelpText = "Display the backup sizes")]
            public bool ShowSizes { get; set; }

            [Option('m', "maxbackups", Required = false, Default = 10, HelpText = "The maximum number of backups to show, default 10")]
            public int MaxBackups { get; set; }
        }

        [Verb("list", HelpText = "List saved backups")]
        public class ListOptions : ListNoNameOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup store")]
            public string BSName { get; set; }
        }

        [Verb("browse", HelpText = "Browse a previous backup")]
        public class BrowseOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup store")]
            public string BSName { get; set; }

            [Option('b', "backup", Required = false, HelpText = "The hash of the backup to restore from, defaults to most recent backup")]
            public string BackupHash { get; set; }
        }

        [Verb("transfer", HelpText = "Transfer a backup store to another location")]
        public class TransferOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup store")]
            public string BSName { get; set; }

            [Value(0, Required = true, HelpText = "The destination which to transfer the backup store")]
            public string Destination { get; set; }
        }

        [Verb("synccache", HelpText = "Sync the cache to the destination")]
        class SyncCacheOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup store")]
            public string BSName { get; set; }
        }

        /*
        int Main(string[] args)
        {
            return CommandLine.Parser.Default.ParseArguments<AddOptions, CommitOptions, CloneOptions>(args)
              .MapResult(
                (AddOptions opts) => RunAddAndReturnExitCode(opts),
                (CommitOptions opts) => RunCommitAndReturnExitCode(opts),
                (CloneOptions opts) => RunCloneAndReturnExitCode(opts),
                errs => 1);
        }*/

        private static void ShowSettings(ShowOptions opts)
        {
            if (opts.Setting != null)
            {
                var settingval = ReadSetting(opts.Setting);
                if (settingval != null)
                {
                    Console.WriteLine(settingval);
                }
            }
            else
            {
                var settings = ReadSettings();
                if (settings != null)
                {
                    foreach (var setval in settings)
                    {
                        Console.WriteLine(setval.Key + ": " + setval.Value);
                    }
                }
            }
        }

        private static void SetSetting(SetOptions opts)
        {
            WriteSetting(opts.Setting, opts.Value);
        }

        private static void RunBackup(RunOptions opts)
        {
            //string backupstorename, string message=null, bool diffbackup=true, string backuphashprefix=null
            try
            {
                var bcore = GetCore();
                var trackclasses = GetTrackClasses();
                bcore.RunBackupAsync(opts.BSName, opts.Message, !opts.Scan, trackclasses, opts.BackupHash);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static void DeleteBackup(DeleteOptions opts)
        {
            try
            { 
                var bcore = GetCore();
                bcore.RemoveBackup(opts.BSName, opts.BackupHash);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public static void RestoreFile(RestoreOptions opts)
        {
            try
            {
                var bcore = GetCore();
                bcore.RestoreFileOrDirectory(opts.Path, opts.RestorePath, opts.BackupHash);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public static void ListBackups(ListNoNameOptions opts, string bsname, BackupCore.Core bcore = null)
        {
            ListOptions opts2 = new ListOptions
            {
                BSName = bsname,
                MaxBackups = opts.MaxBackups,
                ShowSizes = opts.ShowSizes
            };
            ListBackups(opts2, bcore);
        }

        public static void ListBackups(ListOptions opts, BackupCore.Core bcore = null)
        {
            if (bcore == null)
            {
                bcore = GetCore();
            }
            string bsname = GetBackupSetName(opts.BSName);
            var backups = bcore.GetBackups(bsname).ToArray();
            var show = opts.MaxBackups == -1 ? backups.Length : opts.MaxBackups;
            show = backups.Length < show ? backups.Length : show;
            TablePrinter table = new TablePrinter();
            if (opts.ShowSizes)
            {
                table.AddHeaderRow(new string[] { "Hash", "Saved", "RestoreSize", "BackupSize", "Message" });
                for (int i = backups.Length - 1; i >= backups.Length - show; i--)
                {
                    var sizes = bcore.GetBackupSizes(backups[i].Item1);
                    string message = backups[i].Item3;
                    int mlength = 40;
                    if (mlength > message.Length)
                    {
                        mlength = message.Length;
                    }
                    table.AddBodyRow(new string[] {backups[i].Item1.Substring(0, 7),
                        backups[i].Item2.ToLocalTime().ToString(), Utilities.BytesFormatter(sizes.allreferencesizes),
                        Utilities.BytesFormatter(sizes.uniquereferencesizes), message.Substring(0, mlength) });
                }
            }
            else
            {
                table.AddHeaderRow(new string[] { "Hash", "Saved", "Message" });
                for (int i = backups.Length - 1; i >= backups.Length - show; i--)
                {
                    string message = backups[i].Item3;
                    int mlength = 40;
                    if (mlength > message.Length)
                    {
                        mlength = message.Length;
                    }
                    table.AddBodyRow(new string[] { backups[i].Item1.Substring(0, 7),
                        backups[i].Item2.ToLocalTime().ToString(), message.Substring(0, mlength) });
                }
            }
            if (bcore.DefaultBlobs.IsCache)
            {
                Console.WriteLine("(cache)");
            }
            Console.WriteLine(table);
        }

        private static void SyncCache(SyncCacheOptions opts)
        {
            try
            {
                var bcore = GetCore();
                string bsname = GetBackupSetName(opts.BSName);
                bcore.SyncCache(bsname, true);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static List<Tuple<int, string>> GetTrackClasses()
        {
            try
            {
                List<Tuple<int, string>> trackclasses = new List<Tuple<int, string>>();
                using (FileStream fs = new FileStream(Path.Combine(GetBUSourceDir(), ".backuptrack"), FileMode.Open))
                {
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string[] ctp = line.Split(' ');
                            trackclasses.Add(new Tuple<int, string>(Convert.ToInt32(ctp[0]), ctp[1]));
                        }
                    }
                }
                return trackclasses;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetBackupSetName(string bsname)
        {
            if (bsname == null)
            {
                bsname = ReadSetting("name");
                if (bsname == null)
                {
                    Console.WriteLine("A backup store name must be specified with \"set name <name>\"");
                    Console.WriteLine("or the store name must be specified with the -n flag.");
                    throw new Exception(); // TODO: more specific exceptions
                }
            }
            return bsname;
        }

        public static BackupCore.Core GetCore()
        {
            string destination = ReadSetting("dest");
            string cache = ReadSetting("cache");
            if (destination == null)
            {
                destination = GetBUDestinationDir();
                if (destination != null) // We are in a backup destination
                {
                    try
                    {
                        return new BackupCore.Core(null, destination, null, ContinueOrExitPrompt);
                    }
                    catch
                    {
                        throw;
                    }
                }
                else
                {
                    Console.WriteLine("A backup destination must be specified with \"set dest <path>\"");
                    Console.WriteLine("or this command must be run from an existing backup destination.");
                    throw new Exception(); // TODO: more specific exceptions
                }
            }
            else
            {
                try
                {
                    return new BackupCore.Core(cwd, destination, cache, ContinueOrExitPrompt);
                }
                catch
                {
                    throw;
                }
            }
        }

        public static void BrowseBackup(BrowseOptions opts)
        {
            string bsname = GetBackupSetName(opts.BSName);
            var browser = new BackupBrowser(bsname, opts.BackupHash);
            browser.CommandLoop();
        }

        public static void TransferBackupStore(TransferOptions opts)
        {
            string backupsetname = GetBackupSetName(opts.BSName);
            var bcore = GetCore();
            bcore.TransferBackupSet(backupsetname, opts.Destination, true);
        }

        public static void ContinueOrExitPrompt(string error)
        {
            Console.WriteLine(error);
            Console.Write("Continue (y/n)? ");
            while (true)
            {
                ConsoleKeyInfo k = Console.ReadKey();
                if (k.KeyChar.ToString().ToLower() == "y")
                {
                    Console.WriteLine();
                    return;
                }
                else if (k.KeyChar.ToString().ToLower() == "n")
                {
                    Console.WriteLine();
                    throw new Exception("User chose not to continue");
                }
            }
        }

        public static string ReadSetting(string key)
        {
            var settings = ReadSettings();
            if (settings != null)
            {
                if (settings.ContainsKey(key))
                {
                    return settings[key];
                }
            }
            return null;
        }
        
        private static void WriteSetting(string key, string value)
        {
            var settings = ReadSettings();
            if (settings == null)
            {
                settings = new Dictionary<string, string>();
            }
            settings[key] = value;
            WriteSettings(settings);
        }

        private static void ClearSetting(ClearOptions opts)
        {
            var settings = ReadSettings();
            if (settings != null)
            {
                if (settings.ContainsKey(opts.Setting))
                {
                    settings.Remove(opts.Setting);
                    WriteSettings(settings);
                }
            }
        }

        private static string GetBUSourceDir()
        {
            string dir = cwd;
            do
            {
                if (File.Exists(Path.Combine(dir, ".backup")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            } while (dir != null);
            return cwd;
        }

        private static string GetBUDestinationDir()
        {
            string dir = cwd;
            do
            {
                if (Directory.Exists(Path.Combine(dir, BackupCore.Core.IndexDirName)))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            } while (dir != null);
            return null;
        }

        private static Dictionary<string, string> ReadSettings()
        {
            try
            {
                Dictionary<string, string> settings = new Dictionary<string, string>();
                string src = GetBUSourceDir();
                if (src != null)
                {
                    using (FileStream fs = new FileStream(Path.Combine(src, ".backup"), FileMode.Open))
                    {
                        using (StreamReader reader = new StreamReader(fs))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                string[] kv = line.Split(' ');
                                settings[kv[0]] = kv[1];
                            }
                        }
                    }
                    return settings;
                }
                return null;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private static void WriteSettings(Dictionary<string, string> settings)
        {
            using (FileStream fs = new FileStream(Path.Combine(GetBUSourceDir(), ".backup"), FileMode.Create))
            {
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    foreach (var kv in settings)
                    {
                        writer.WriteLine(kv.Key + " " + kv.Value);
                    }
                }
            }
        }
    }
}
