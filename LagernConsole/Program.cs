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

        public static readonly string LagernSettingsFilename = ".lagern";
        public static readonly string TrackClassFilename = ".lagerntrack";
        
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "loop")
            {
                while (true)
                {
                    Console.Write("lagern> ");
                    string[] largs = ReadArgs();
                    ParseArgs(largs, true);
                }
            }
            else
            {
                ParseArgs(args, false);
            }
        }

        private static void ParseArgs(string[] args, bool loop)
        {
            // This overall try catch looks ugly, but helps crashes seem to occur gracefully to the user
            try
            {
                var p = Parser.Default.ParseArguments<InitOptions, ShowOptions, SetOptions, ClearOptions,
                    StatusOptions, RunOptions, DeleteOptions, RestoreOptions, ListOptions,
                    BrowseOptions, TransferOptions, SyncCacheOptions, ExitOptions>(args)
                  .WithParsed<InitOptions>(opts => Initialize(opts))
                  .WithParsed<ShowOptions>(opts => ShowSettings(opts))
                  .WithParsed<SetOptions>(opts => SetSetting(opts))
                  .WithParsed<ClearOptions>(opts => ClearSetting(opts))
                  .WithParsed<StatusOptions>(opts => Status(opts))
                  .WithParsed<RunOptions>(opts => RunBackup(opts))
                  .WithParsed<DeleteOptions>(opts => DeleteBackup(opts))
                  .WithParsed<RestoreOptions>(opts => RestoreFile(opts))
                  .WithParsed<ListOptions>(opts => ListBackups(opts))
                  .WithParsed<BrowseOptions>(opts => BrowseBackup(opts))
                  .WithParsed<TransferOptions>(opts => TransferBackupStore(opts))
                  .WithParsed<SyncCacheOptions>(opts => SyncCache(opts));
                if (loop)
                {
                    p.WithParsed<ExitOptions>(opts => Exit());
                }
            }
            catch { }
        }

        [Verb("init", HelpText = "Setup this directory to be backed up into a new BackupSet")]
        class InitOptions
        {
            [Option('n', "bsname", Required = true, HelpText = "The name of the new backup set")]
            public string BSName { get; set; }

            [Value(0, Required = true, HelpText = "The destination path in which to store the new backup set")]
            public string Destination { get; set; }

            [Option('c', "cache", Required = false, HelpText = "The path of the cache, should be on the same disk " +
                "as the files being backed up")]
            public string Cache { get; set; }
        }

        [Verb("exit", HelpText = "Exit the command loop")]
        public class ExitOptions { }

        [Verb("show", HelpText = "Show lagern settings")]
        class ShowOptions
        {
            [Value(0, Required = false, HelpText = "The setting to show")]
            public BackupSettings? Setting { get; set; }
        }

        [Verb("set", HelpText = "Set a lagern setting")]
        class SetOptions
        {
            [Value(0, Required = true, HelpText = "The setting to set")]
            public BackupSettings Setting { get; set; }

            [Value(0, Required = true, HelpText = "The value to give setting")]
            public string Value { get; set; }
        }

        [Verb("clear", HelpText = "Clear a lagern setting")]
        class ClearOptions
        {
            [Value(0, Required = true, HelpText = "The setting to clear")]
            public string Setting { get; set; }
        }

        [Verb("status", HelpText = "Show the working tree status")]
        class StatusOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string BSName { get; set; }

            [Option('b', "backup", Required = false, HelpText = "The hash of the backup to use for a differential backup")]
            public string BackupHash { get; set; }
        }

        [Verb("run", HelpText = "Run a backup.")]
        class RunOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
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
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string BSName { get; set; }

            [Option('b', "backup", Required = true, HelpText = "The backup hash (or its prefix) of the backup to be deleted")]
            public string BackupHash { get; set; }
        }

        [Verb("restore", HelpText = "Restore a file or directory")]
        public class RestoreOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
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
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string BSName { get; set; }
        }

        [Verb("browse", HelpText = "Browse a previous backup")]
        public class BrowseOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string BSName { get; set; }

            [Option('b', "backup", Required = false, HelpText = "The hash of the backup to restore from, defaults to most recent backup")]
            public string BackupHash { get; set; }
        }

        [Verb("transfer", HelpText = "Transfer a backup store to another location")]
        public class TransferOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string BSName { get; set; }

            [Value(0, Required = true, HelpText = "The destination which to transfer the backup store")]
            public string Destination { get; set; }
        }

        [Verb("synccache", HelpText = "Sync the cache to the destination")]
        class SyncCacheOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
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

        private static void Initialize(InitOptions opts)
        {
            try
            {
                using (File.Create(Path.Combine(cwd, LagernSettingsFilename))) { }
                WriteSetting(BackupSettings.dest, opts.Destination);
                WriteSetting(BackupSettings.name, opts.BSName);
                if (opts.Cache != null)
                {
                    WriteSetting(BackupSettings.cache, opts.Cache);
                }
                BackupCore.Core.InitializeNew(opts.BSName, cwd, opts.Destination, opts.Cache);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static void ShowSettings(ShowOptions opts)
        {
            if (opts.Setting != null)
            {
                var settingval = ReadSetting(opts.Setting.Value);
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

        private static void Status(StatusOptions opts)
        {
            try
            {
                var bcore = GetCore();
                string bsname = GetBackupSetName(opts.BSName);
                TablePrinter table = new TablePrinter();
                table.AddHeaderRow(new string[] { "Path", "Status" });
                List<(int, string)> trackclasses;
                try
                {
                    trackclasses = bcore.ReadTrackClassFile(Path.Combine(GetBUSourceDir(), TrackClassFilename));
                }
                catch
                {
                    trackclasses = null;
                }
                foreach (var change in bcore.GetWTStatus(bsname, true, trackclasses, opts.BackupHash))
                {
                    table.AddBodyRow(new string[] { change.path, change.change.ToString() });
                }
                Console.WriteLine(table);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static void RunBackup(RunOptions opts)
        {
            try
            {
                var bcore = GetCore();
                string bsname = GetBackupSetName(opts.BSName);
                List<(int, string)> trackclasses;
                try
                {
                    trackclasses = bcore.ReadTrackClassFile(Path.Combine(GetBUSourceDir(), TrackClassFilename));
                }
                catch
                {
                    trackclasses = null;
                }
                bcore.RunBackup(bsname, opts.Message, true, !opts.Scan, trackclasses, opts.BackupHash);
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
                string bsname = GetBackupSetName(opts.BSName);
                bcore.RemoveBackup(bsname, opts.BackupHash);
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
                string bsname = GetBackupSetName(opts.BSName);
                string restorepath = opts.Path;
                bool absolutepath = false;
                if (opts.RestorePath != null)
                {
                    restorepath = opts.RestorePath;
                    absolutepath = true;
                }
                bcore.RestoreFileOrDirectory(bsname, opts.Path, restorepath, opts.BackupHash, absolutepath);
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
            (var backupsenum, bool cache) = bcore.GetBackups(bsname);
            var backups = backupsenum.ToArray();
            var show = opts.MaxBackups == -1 ? backups.Length : opts.MaxBackups;
            show = backups.Length < show ? backups.Length : show;
            TablePrinter table = new TablePrinter();
            if (opts.ShowSizes)
            {
                table.AddHeaderRow(new string[] { "Hash", "Saved", "RestoreSize", "BackupSize", "Message" });
                for (int i = backups.Length - 1; i >= backups.Length - show; i--)
                {
                    var sizes = bcore.GetBackupSizes(backups[i].backuphash);
                    string message = backups[i].message;
                    int mlength = 40;
                    if (mlength > message.Length)
                    {
                        mlength = message.Length;
                    }
                    table.AddBodyRow(new string[] {backups[i].backuphash.Substring(0, 7),
                        backups[i].backuptime.ToLocalTime().ToString(), Utilities.BytesFormatter(sizes.allreferencesizes),
                        Utilities.BytesFormatter(sizes.uniquereferencesizes), message.Substring(0, mlength) });
                }
            }
            else
            {
                table.AddHeaderRow(new string[] { "Hash", "Saved", "Message" });
                for (int i = backups.Length - 1; i >= backups.Length - show; i--)
                {
                    string message = backups[i].message;
                    int mlength = 40;
                    if (mlength > message.Length)
                    {
                        mlength = message.Length;
                    }
                    table.AddBodyRow(new string[] { backups[i].backuphash.Substring(0, 7),
                        backups[i].backuptime.ToLocalTime().ToString(), message.Substring(0, mlength) });
                }
            }
            if (cache)
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
                bcore.SyncCache(bsname);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public static string GetBackupSetName(string bsname)
        {
            if (bsname == null)
            {
                bsname = ReadSetting(BackupSettings.name);
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
            string destination = ReadSetting(BackupSettings.dest);
            string cache = ReadSetting(BackupSettings.cache);
            if (destination == null)
            {
                destination = GetBUDestinationDir();
                if (destination != null) // We are in a backup destination
                {
                    try
                    {
                        return BackupCore.Core.LoadCore(null, destination, null);
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
                    return BackupCore.Core.LoadCore(cwd, destination, cache);
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
            bcore.TransferBackupSet(backupsetname, BackupCore.Core.InitializeNew(backupsetname, null, opts.Destination), true);
        }

        public static string ReadSetting(BackupSettings key)
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
        
        private static void WriteSetting(BackupSettings key, string value)
        {
            var settings = ReadSettings();
            if (settings == null)
            {
                settings = new Dictionary<BackupSettings, string>();
            }
            settings[key] = value;
            WriteSettings(settings);
        }

        private static void ClearSetting(ClearOptions opts)
        {
            var settings = ReadSettings();
            if (settings != null)
            {
                if (Enum.TryParse(opts.Setting, out BackupSettings setting))
                {
                    if (settings.ContainsKey(setting))
                    {
                        settings.Remove(setting);
                        WriteSettings(settings);
                    }
                }
            }
        }

        private static string GetBUSourceDir()
        {
            string dir = cwd;
            do
            {
                if (File.Exists(Path.Combine(dir, LagernSettingsFilename)))
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
                if (Directory.Exists(Path.Combine(dir, BackupCore.FSCoreDependencies.IndexDirName)))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            } while (dir != null);
            return null;
        }

        public static void Exit() => Environment.Exit(0);

        private static Dictionary<BackupSettings, string> ReadSettings()
        {
            try
            {
                Dictionary<BackupSettings, string> settings = new Dictionary<BackupSettings, string>();
                string src = GetBUSourceDir();
                if (src != null)
                {
                    using (FileStream fs = new FileStream(Path.Combine(src, LagernSettingsFilename), FileMode.Open))
                    {
                        using (StreamReader reader = new StreamReader(fs))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                string[] kv = line.Split(' ');
                                if (Enum.TryParse(kv[0], out BackupSettings key))
                                {
                                    settings[key] = kv[1];
                                }
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

        private static void WriteSettings(Dictionary<BackupSettings, string> settings)
        {
            using (FileStream fs = new FileStream(Path.Combine(GetBUSourceDir(), LagernSettingsFilename), FileMode.Create))
            {
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    foreach (var kv in settings)
                    {
                        writer.WriteLine(kv.Key.ToString() + " " + kv.Value);
                    }
                }
            }
        }

        public enum BackupSettings
        {
            dest,
            cache,
            name
        }

        public static string[] ReadArgs()
        {
            string command = Console.ReadLine();
            if (command != "")
            {
                return SplitArguments(command);
            }
            return new string[0];
        }

        /// <summary>
        /// Splits a command string, respecting args enclosed in double quotes
        /// </summary>
        /// <param name="commandLine"></param>
        /// <returns></returns>
        public static string[] SplitArguments(string commandLine)
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
    }
}
