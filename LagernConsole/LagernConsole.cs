using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CommandLine;
using BackupCore;

namespace BackupConsole
{
    // TODO: Replace custom parsing with CommandLine nuget library
    // https://github.com/gsscoder/commandline
    public class LagernConsole
    {
        // Current directory (where user launches from)
        private static readonly string CWD = Environment.CurrentDirectory;

        public static readonly string LagernDirectory = ".lagern";
        public static readonly string LagernSettingsFile = Path.Combine(LagernDirectory, ".settings");
        public static readonly string TrackClassFile = Path.Combine(LagernDirectory, ".track");
        
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].Trim() == "loop")
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
                var p = Parser.Default.ParseArguments<InitOptions, AddDestinationOptions, ShowOptions, SetOptions, ClearOptions,
                    StatusOptions, RunOptions, DeleteOptions, RestoreOptions, ListOptions,
                    BrowseOptions, TransferOptions, SyncCacheOptions, ExitOptions>(args)
                  .WithParsed<InitOptions>(opts => Initialize(opts))
                  .WithParsed<AddDestinationOptions>(AddDestination)
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
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        [Verb("init", HelpText = "Setup this directory to be backed up into a new BackupSet")]
        class InitOptions
        {
            [Option('n', "bsname", Required = true, HelpText = "The name of the new backup set")]
            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string BSName { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.

            /*
            [Value(0, Required = true, HelpText = "The destination paths or names of the cloud services in which to store the new backup set")]
            public IEnumerable<string> Destinations { get; set; }
            */

            [Option('c', "cache", Required = false, HelpText = "The path of the cache, should be on the same disk " +
                "as the files being backed up")]
            public string? Cache { get; set; }

            [Option("cloud-config", Required = false, HelpText = "Path to a JSON-formatted file containing configuration settings " +
                "for cloud backup provider")]
            public string? CloudConfigFile { get; set; }

            [Option('p', "password", Required = false, HelpText = "Encrypt stored backups with given password")]
            public bool PromptForPassword { get; set; }
        }

        [Verb("dest", HelpText = "Add a destinations to which new backups will be saved")]
        class AddDestinationOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }

            [Value(0, Required = true, HelpText = "The destination path or name of the cloud service in which to store the backup set")]
            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string Destination { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.

            [Option("cloud-config", Required = false, HelpText = "Path to a JSON-formatted file containing configuration settings " +
                "for cloud backup provider")]
            public string? CloudConfigFile { get; set; }

            [Option('p', "password", Required = false, HelpText = "Encrypt stored backups with given password")]
            public bool PromptForPassword { get; set; }
        }

        [Verb("exit", HelpText = "Exit the command loop")]
        public class ExitOptions { }

        [Verb("show", HelpText = "Show lagern settings")]
        class ShowOptions
        {
            [Value(0, Required = false, HelpText = "The setting to show")]
            public BackupCore.BackupSetting? Setting { get; set; }
        }

        [Verb("set", HelpText = "Set a lagern setting")]
        class SetOptions
        {
            [Value(0, Required = true, HelpText = "The setting to set")]
            public BackupCore.BackupSetting Setting { get; set; }

            [Value(0, Required = true, HelpText = "The value to give setting")]
            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string Value { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        }

        [Verb("clear", HelpText = "Clear a lagern setting")]
        class ClearOptions
        {
            [Value(0, Required = true, HelpText = "The setting to clear")]
            public BackupCore.BackupSetting Setting { get; set; }
        }

        [Verb("status", HelpText = "Show the working tree status")]
        class StatusOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }

            [Option('b', "backup", Required = false, HelpText = "The hash of the backup to use for a differential backup")]
            public string? BackupHash { get; set; }
        }

        [Verb("run", HelpText = "Run a backup.")]
        class RunOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }

            [Option('b', "backup", Required = false, SetName = "differential", HelpText = "The hash of the backup to use for a differential backup")]
            public string? BackupHash { get; set; }

            [Option('s', "scan", SetName = "differential", HelpText = "Forces scan of all files (makes backup non-differential)")]
            public bool Scan { get; set; }

            [Option('m', "message", Default = "", HelpText = "A message describing the backup")]
            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string Message { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        }

        [Verb("delete", HelpText = "Delete a backup")]
        class DeleteOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }

            [Option('b', "backup", Required = true, HelpText = "The backup hash (or its prefix) of the backup to be deleted")]
            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string BackupHash { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.

            [Option('f', "force", Required = false, Default = false, HelpText = "Force deleting backup from destination when destination inaccessible")]
            public bool Force { get; set; }
        }

        [Verb("restore", HelpText = "Restore a file or directory")]
        public class RestoreOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }

            [Option('b', "backup", Required = false, HelpText = "The hash of the backup to restore from, defaults to most recent backup")]
            public string? BackupHash { get; set; }

            [Option('r', "restorepath", Required = false, HelpText = "The path which to restore the file or directory, defaults to current directory")]
            public string? RestorePath { get; set; }

            [Value(0, Required = true, HelpText = "The path, relative to the backup root of the file or directory to restore")]
            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string Path { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
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
            public string? BSName { get; set; }
        }

        [Verb("browse", HelpText = "Browse a previous backup")]
        public class BrowseOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }

            [Option('b', "backup", Required = false, HelpText = "The hash of the backup to restore from, defaults to most recent backup")]
            public string? BackupHash { get; set; }
        }

        [Verb("transfer", HelpText = "Transfer a backup store to another location")]
        public class TransferOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }

            [Value(0, Required = true, HelpText = "The destination which to transfer the backup store")]
            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string Destination { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        }

        [Verb("synccache", HelpText = "Sync the cache to the destination")]
        class SyncCacheOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }
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
                ICoreSrcDependencies srcdep = FSCoreSrcDependencies.InitializeNew(opts.BSName, CWD, new DiskFSInterop(), opts.Cache);

                if (opts.Cache != null)
                {
                    var cachedep = CoreDstDependencies.InitializeNew(opts.BSName, true, DiskDstFSInterop.InitializeNew(opts.Cache).Result, false);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static void AddDestination(AddDestinationOptions opts)
        {
            var srcdep = FSCoreSrcDependencies.Load(CWD, new DiskFSInterop());
            var settings = srcdep.ReadSettings();
            bool cache_used = settings.ContainsKey(BackupSetting.cache);
            string bsname = GetBackupSetName(opts.BSName, srcdep);

            string? password = null;
            if (opts.PromptForPassword)
            {
                password = PasswordPrompt();
            }

            string destination = opts.Destination.Trim();
            if (destination.ToLower() == "backblaze")
            {
                destination = "backblaze";
                if (opts.CloudConfigFile == null)
                {
                    throw new ArgumentException("Cloud config file needed to initialize backblaze backup.");
                }
                CoreDstDependencies.InitializeNew(bsname, false, BackblazeDstInterop.InitializeNew(opts.CloudConfigFile, password).Result, cache_used);
            }
            else
            {
                CoreDstDependencies.InitializeNew(bsname, false, DiskDstFSInterop.InitializeNew(destination, password).Result, cache_used);
            }


            List<string> dstlistings;
            if (settings.ContainsKey(BackupSetting.dests))
            {
                dstlistings = settings[BackupSetting.dests].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();
            }
            else
            {
                dstlistings = new List<string>();
            }

            List<string[]> dsts_passopts_cc = dstlistings.Select(dl => dl.Split('|')).ToList();

            dsts_passopts_cc.Add(new string[] { destination, password != null ? "p" : "n",  opts.CloudConfigFile ?? ""});
            dstlistings = dsts_passopts_cc.Select(dpc => string.Join('|', dpc)).ToList();
            srcdep.WriteSetting(BackupSetting.dests, string.Join(';', dstlistings));
        }

        private static void ShowSettings(ShowOptions opts)
        {
            if (opts.Setting != null)
            {
                var settingval = ReadSetting(LoadCore().SrcDependencies, opts.Setting.Value);
                if (settingval != null)
                {
                    Console.WriteLine(settingval);
                }
            }
            else
            {
                var core = LoadCore();
                var settings = core.SrcDependencies.ReadSettings();
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
            WriteSetting(LoadCore().SrcDependencies, opts.Setting, opts.Value);
        }

        private static void Status(StatusOptions opts)
        {
            try
            {
                var bcore = LoadCore();
                string bsname = GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                TablePrinter table = new();
                table.AddHeaderRow(new string[] { "Path", "Status" });
                List<(int, string)>? trackclasses;
                try
                {
                    trackclasses = Core.ReadTrackClassFile(Path.Combine(GetBUSourceDir(), TrackClassFile));
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
                var bcore = LoadCore();
                string bsname = GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                List<(int, string)>? trackclasses;
                try
                {
                    trackclasses = Core.ReadTrackClassFile(Path.Combine(GetBUSourceDir(), TrackClassFile));
                }
                catch
                {
                    trackclasses = null;
                }
                bcore.RunBackup(bsname, opts.Message, true, !opts.Scan, trackclasses, new List<string?> { opts.BackupHash });
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
                var bcore = LoadCore();
                string bsname = GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                try
                {
                    bcore.RemoveBackup(bsname, opts.BackupHash, opts.Force);
                }
                catch (Core.BackupRemoveException ex)
                {
                    Console.WriteLine(ex.Message);
                }
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
                var bcore = LoadCore();
                string bsname = GetBackupSetName(opts.BSName, bcore.SrcDependencies);
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

        public static void ListBackups(ListNoNameOptions opts, string bsname, Core? bcore = null)
        {
            ListOptions opts2 = new()
            {
                BSName = bsname,
                MaxBackups = opts.MaxBackups,
                ShowSizes = opts.ShowSizes
            };
            ListBackups(opts2, bcore);
        }

        public static void ListBackups(ListOptions opts, Core? bcore = null)
        {
            if (bcore == null)
            {
                bcore = LoadCore();
            }
            string bsname = GetBackupSetName(opts.BSName, bcore.SrcDependencies);
            (var backupsenum, bool cache) = bcore.GetBackups(bsname);
            var backups = backupsenum.ToArray();
            var show = opts.MaxBackups == -1 ? backups.Length : opts.MaxBackups;
            show = backups.Length < show ? backups.Length : show;
            TablePrinter table = new();
            if (opts.ShowSizes)
            {
                table.AddHeaderRow(new string[] { "Hash", "Saved", "RestoreSize", "BackupSize", "Message" });
                for (int i = backups.Length - 1; i >= backups.Length - show; i--)
                {
                    var (allreferencesizes, uniquereferencesizes) = bcore.GetBackupSizes(bsname, backups[i].backuphash);
                    string message = backups[i].message;
                    int mlength = 40;
                    if (mlength > message.Length)
                    {
                        mlength = message.Length;
                    }
                    table.AddBodyRow(new string[] {backups[i].backuphash[..7],
                        backups[i].backuptime.ToLocalTime().ToString(), Utilities.BytesFormatter(allreferencesizes),
                        Utilities.BytesFormatter(uniquereferencesizes), message[..mlength] });
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
                    table.AddBodyRow(new string[] { backups[i].backuphash[..7],
                        backups[i].backuptime.ToLocalTime().ToString(), message[..mlength] });
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
                var bcore = LoadCore();
                string bsname = GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                bcore.SyncCacheSaveBackupSets(bsname);
                bcore.SaveBlobIndices();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public static string GetBackupSetName(string? bsname, ICoreSrcDependencies srcDependencies)
        {
            if (bsname == null)
            {
                bsname = ReadSetting(srcDependencies, BackupCore.BackupSetting.name);
                if (bsname == null)
                {
                    Console.WriteLine("A backup store name must be specified with \"set name <name>\"");
                    Console.WriteLine("or the store name must be specified with the -n flag.");
                    throw new Exception(); // TODO: more specific exceptions
                }
            }
            return bsname;
        }

        public static Core LoadCore()
        {
            var srcdep = FSCoreSrcDependencies.Load(CWD, new DiskFSInterop());
            string? cache;

            string? destinations;
            try
            {
                destinations = srcdep.ReadSetting(BackupSetting.dests);
            }
            catch (KeyNotFoundException)
            {
                destinations = null;
            }


            try
            {
                cache = srcdep.ReadSetting(BackupSetting.cache);
            }
            catch (KeyNotFoundException)
            {
                cache = null;
            }

            if (destinations == null)
            {
                string? destination = GetBUDestinationDir();
                if (destination != null) // We are in a backup destination
                {
                    try
                    {
                        // TODO: password support here
                        return Core.LoadDiskCore(null, new List<(string, string?)>(1) { (destination, null) }, null);
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
                ICoreDstDependencies? cachedep = null;
                if (cache != null)
                {
                    cachedep = CoreDstDependencies.Load(DiskDstFSInterop.Load(cache).Result);
                }

                List<ICoreDstDependencies> dstdeps = new();
                foreach (var destination in destinations.Split(';'))
                {
                    string[] dst_passopt_cc = destination.Split('|');
                    string dst_path = dst_passopt_cc[0].Trim();
                    bool use_pass = dst_passopt_cc[1].Trim().ToLower() == "p";
                    string? cloud_config = dst_passopt_cc[2].Trim() == "" ? null : dst_passopt_cc[2].Trim();

                    string? password = null;
                    if (use_pass)
                    {
                        password = PasswordPrompt();
                    }

                    if (dst_path.ToLower() == "backblaze") // TODO: Backblaze should be a specific implementation of general cloud sync support
                    {
                        try
                        {
                            if (cloud_config == null)
                            {
                                throw new Exception("Backblaze backups require a cloud config file to be specified");
                            }
                            dstdeps.Add(CoreDstDependencies.Load(BackblazeDstInterop.Load(cloud_config, password).Result, cache != null));
                        }
                        catch
                        {
                            Console.WriteLine("Failed to load backblaze");
                        }
                    }
                    else
                    {
                        try
                        {
                            dstdeps.Add(CoreDstDependencies.Load(DiskDstFSInterop.Load(dst_path, password).Result, cache != null));
                        }
                        catch (Exception)
                        {
                            Console.WriteLine($"Failed to load {dst_path}");
                        }
                    }
                }
                return new Core(srcdep, dstdeps, cachedep);
            }
        }

        public static string PasswordPrompt()
        {
            string inputpass = "";
            while (inputpass == "")
            {
                Console.Write("Password: ");
                inputpass = ReadLineNonNull();
            }
            return inputpass;
        }

        private static string ReadLineNonNull()
        {
            return Console.ReadLine() ?? "";
        }

        public static void BrowseBackup(BrowseOptions opts)
        {
            Core bcore = LoadCore();
            string bsname = GetBackupSetName(opts.BSName, bcore.SrcDependencies);
            var browser = new BackupBrowser(bsname, opts.BackupHash, bcore);
            browser.CommandLoop();
        }

        public static void TransferBackupStore(TransferOptions opts)
        {
            var bcore = LoadCore();
            string backupsetname = GetBackupSetName(opts.BSName, bcore.SrcDependencies);
            // TODO: password support
            bcore.TransferBackupSet(backupsetname, Core.InitializeNewDiskCore(backupsetname, null, new List<(string, string?)>(1) { (opts.Destination, null) }), true);
        }

        public static string ReadSetting(ICoreSrcDependencies src, BackupSetting key) => src.ReadSetting(key);

        private static void WriteSetting(ICoreSrcDependencies src, BackupSetting key, string value) => src.WriteSetting(key, value);

        private static void ClearSetting(ClearOptions opts)
        {
            Core core = LoadCore();
            core.SrcDependencies.ClearSetting(opts.Setting);
        }

        private static string GetBUSourceDir()
        {
            string? dir = CWD;
            do
            {
                if (File.Exists(Path.Combine(dir, LagernSettingsFile)))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            } while (dir != null);
            return CWD;
        }

        private static string? GetBUDestinationDir()
        {
            string? dir = CWD;
            do
            {
                if (Directory.Exists(Path.Combine(dir, "index")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            } while (dir != null);
            return null;
        }

        public static void Exit() => Environment.Exit(0);
        
        public static string[] ReadArgs()
        {
            string command = ReadLineNonNull();
            if (command != "")
            {
                return SplitArguments(command);
            }
            return Array.Empty<string>();
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
            string split = new(parmChars);
            while (split.Contains("\n\n"))
            {
                split = split.Replace("\n\n", "\n");
            }
            split = split.Replace("\"", "");
            return split.Split('\n');
        }
    }
}
