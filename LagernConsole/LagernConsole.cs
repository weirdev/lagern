using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CommandLine;
using BackupCore;
using System.Threading.Tasks;
using static LagernCore.Models.SettingsFileModel;

namespace BackupConsole
{
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
                  .WithParsed<InitOptions>(opts => Initialize(opts).Wait())
                  .WithParsed<AddDestinationOptions>(opts => AddDestination(opts).Wait())
                  .WithParsed<ShowOptions>(opts => ShowSettings(opts).Wait())
                  .WithParsed<SetOptions>(opts => SetSetting(opts).Wait())
                  .WithParsed<ClearOptions>(opts => ClearSetting(opts).Wait())
                  .WithParsed<StatusOptions>(opts => Status(opts).Wait())
                  .WithParsed<RunOptions>(opts => RunBackup(opts).Wait())
                  .WithParsed<DeleteOptions>(opts => DeleteBackup(opts).Wait())
                  .WithParsed<RestoreOptions>(opts => RestoreFile(opts).Wait())
                  .WithParsed<ListOptions>(opts => ListBackups(opts).Wait())
                  .WithParsed<BrowseOptions>(opts => BrowseBackup(opts).Wait())
                  .WithParsed<TransferOptions>(opts => TransferBackupStore(opts).Wait())
                  .WithParsed<SyncCacheOptions>(opts => SyncCache(opts).Wait());
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
            public BackupSetting? Setting { get; set; }
        }

        [Verb("set", HelpText = "Set a lagern setting")]
        class SetOptions
        {
            [Value(0, Required = true, HelpText = "The setting to set")]
            public BackupSetting Setting { get; set; }

            [Value(0, Required = true, HelpText = "The value to give setting")]

            #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            public string Value { get; set; }
            #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        }

        [Verb("clear", HelpText = "Clear a lagern setting")]
        class ClearOptions
        {
            [Value(0, Required = true, HelpText = "The setting to clear")]
            public BackupSetting Setting { get; set; }
        }

        [Verb("status", HelpText = "Show the working tree status")]
        class StatusOptions
        {
            [Option('n', "bsname", Required = false, HelpText = "The name of the backup set")]
            public string? BSName { get; set; }

            [Option('b', "backup", Required = false, HelpText = "The hash of the backup to use for a differential comparison with the working tree. Defaults to the last backup.")]
            public string? BackupHash { get; set; }

            [Option('d', "destination", Default = null, Required = false, HelpText = "Backup destination to use for differential comparison. Defaults to the first configured destination.")]
            public string? Destination { get; set; }
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
            public string Message { get; set; } = "";

            [Option('d', "destinations", Separator = ',', Default = null, Required = false, HelpText = "Comma separated list of backup destinations to backup to")] // TODO: Null default not currently being applied
            public IEnumerable<string>? Destinations { get; set; }
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

            [Option('d', "destinations", Separator = ',', Default = null, Required = false, HelpText = "Comma separated list of backup destinations to remove the backup from")] // TODO: Null default not currently being applied
            public IEnumerable<string>? Destinations { get; set; }
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

            [Option('d', "destinations", Separator = ',', Default = null, Required = false, HelpText = "Comma separated list of backup destinations for which to list backups")]  // TODO: Null default not currently being applied
            public IEnumerable<string>? Destinations { get; set; }
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

        private static async Task Initialize(InitOptions opts)
        {
            try
            {
                ICoreSrcDependencies srcdep = await FSCoreSrcDependencies.InitializeNew(opts.BSName, CWD, new DiskFSInterop(), opts.Cache);

                if (opts.Cache != null)
                {
                    var cachedep = CoreDstDependencies.InitializeNew(opts.BSName, true, await DiskDstFSInterop.InitializeNew(opts.Cache), false);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static async Task AddDestination(AddDestinationOptions opts)
        {
            var srcdep = FSCoreSrcDependencies.Load(CWD, new DiskFSInterop());
            var settings = await srcdep.ReadSettings();
            bool cache_used = settings.ContainsKey(BackupSetting.cache);
            string bsname = await GetBackupSetName(opts.BSName, srcdep);

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
                await CoreDstDependencies.InitializeNew(bsname, false, await BackblazeDstInterop.InitializeNew(opts.CloudConfigFile, password), cache_used);
            }
            else
            {
                destination = new UriBuilder(destination).Uri.LocalPath;
                await CoreDstDependencies.InitializeNew(bsname, false, await DiskDstFSInterop.InitializeNew(destination, password), cache_used);
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

            dsts_passopts_cc.Add(new string[] { destination, password != null ? "p" : "n", opts.CloudConfigFile ?? ""});
            dstlistings = dsts_passopts_cc.Select(dpc => string.Join('|', dpc)).ToList();
            await srcdep.WriteSetting(BackupSetting.dests, string.Join(';', dstlistings));
        }

        private static async Task ShowSettings(ShowOptions opts)
        {
            if (opts.Setting != null)
            {
                var settingval = await ReadSetting((await LoadCore()).core.SrcDependencies, opts.Setting.Value);
                if (settingval != null)
                {
                    Console.WriteLine(settingval);
                }
            }
            else
            {
                var core = (await LoadCore()).core;
                var settings = await core.SrcDependencies.ReadSettings();
                if (settings != null)
                {
                    foreach (var setval in settings)
                    {
                        Console.WriteLine(setval.Key + ": " + setval.Value);
                    }
                }
            }
        }

        private static async Task SetSetting(SetOptions opts)
        {
            await WriteSetting((await LoadCore()).core.SrcDependencies, opts.Setting, opts.Value);
        }

        /// <summary>
        /// Displays the working tree status relative to a single backup.
        /// </summary>
        /// <param name="opts"></param>
        /// <returns></returns>
        private static async Task Status(StatusOptions opts)
        {
            try
            {
                var (bcore, dests) = (await LoadCore(opts.Destination != null ? new HashSet<string> { opts.Destination } : null));
                if (dests.Count == 0)
                {
                    Console.WriteLine("No destination available");
                    return;
                }
                string bsname = await GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                TablePrinter table = new();
                table.AddHeaderRow(new string[] { "Path", "Status" });
                List<(int, string)>? trackclasses;
                try
                {
                    trackclasses = await Core.ReadTrackClassFile(Path.Combine(GetBUSourceDir(), TrackClassFile));
                }
                catch
                {
                    trackclasses = null;
                }
                foreach (var change in await bcore.GetWTStatus(bsname, bcore.DefaultDstDependencies[0], true, trackclasses, opts.BackupHash)) // TODO: Defaulting using dest order in core, should explicitly rely on dest order in settings file
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

        private static async Task RunBackup(RunOptions opts)
        {
            try
            {
                var bcore = (await LoadCore(opts.Destinations != null && opts.Destinations.Any() ? opts.Destinations.ToHashSet() : null)).core;
                string bsname = await GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                List<(int, string)>? trackclasses;
                try
                {
                    trackclasses = await Core.ReadTrackClassFile(Path.Combine(GetBUSourceDir(), TrackClassFile));
                }
                catch
                {
                    trackclasses = null;
                }
                await bcore.RunBackup(bsname, opts.Message, true, !opts.Scan, trackclasses, new List<string?> { opts.BackupHash });
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static async Task DeleteBackup(DeleteOptions opts)
        {
            try
            { 
                var bcore = (await LoadCore(opts.Destinations != null && opts.Destinations.Any() ? opts.Destinations.ToHashSet() : null)).core;
                string bsname = await GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                try
                {
                    await bcore.RemoveBackup(bsname, opts.BackupHash, opts.Force);
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

        public static async Task RestoreFile(RestoreOptions opts)
        {
            try
            {
                var bcore = (await LoadCore()).core;
                string bsname = await GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                string restorepath = opts.Path;
                bool absolutepath = false;
                if (opts.RestorePath != null)
                {
                    restorepath = opts.RestorePath;
                    absolutepath = true;
                }
                await bcore.RestoreFileOrDirectory(bsname, opts.Path, restorepath, opts.BackupHash, absolutepath);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public static async Task ListBackups(ListNoNameOptions opts, string bsname, 
            (Core core, Dictionary<BackupDestinationSpecification, ICoreDstDependencies> destinations)? coreAndDestinations = null)
        {
            ListOptions opts2 = new()
            {
                BSName = bsname,
                MaxBackups = opts.MaxBackups,
                ShowSizes = opts.ShowSizes
            };
            await ListBackups(opts2, coreAndDestinations);
        }

        public static async Task ListBackups(ListOptions opts, 
            (Core core, Dictionary<BackupDestinationSpecification, ICoreDstDependencies> destinations)? coreAndDestinations = null)
        {
            if (coreAndDestinations == null)
            {
                coreAndDestinations = await LoadCore(opts.Destinations != null && opts.Destinations.Any() ? opts.Destinations.ToHashSet() : null);
            }
            var bcore = coreAndDestinations.Value.core;
            string bsname = await GetBackupSetName(opts.BSName, bcore.SrcDependencies);

            IEnumerable<(string, ICoreDstDependencies)> destinations;
            if (coreAndDestinations.Value.destinations.Any())
            {
                destinations = coreAndDestinations.Value.destinations
                    .Select(destination => (destination.Key.Name ?? "", destination.Value));
            }
            else if (bcore.CacheDependencies != null)
            {
                destinations = new List<(string, ICoreDstDependencies)>() { ("(cache)", bcore.CacheDependencies) };
            }
            else
            {
                Console.WriteLine("No destinations for which to list backups");
                return;
            }

            foreach (var destination in coreAndDestinations.Value.destinations)
            {
                Console.WriteLine($"Destination \"{destination.Key.Name}\"");

                (var backupsenum, bool cache) = await bcore.GetBackups(bsname, destination.Value);
                List<(string backuphash, DateTime backuptime, string message)>? backups = backupsenum.ToList();
                var show = opts.MaxBackups == -1 ? backups.Count : opts.MaxBackups;
                show = backups.Count < show ? backups.Count : show;
                TablePrinter table = new();
                if (opts.ShowSizes)
                {
                    table.AddHeaderRow(new string[] { "Hash", "Saved", "RestoreSize", "BackupSize", "Message" });
                    for (int i = backups.Count - 1; i >= backups.Count - show; i--)
                    {
                        var (allreferencesizes, uniquereferencesizes) = await Core.GetBackupSizes(bsname, backups[i].backuphash, destination.Value);
                        string message = backups[i].message;
                        int mlength = 40;
                        if (mlength > message.Length)
                        {
                            mlength = message.Length;
                        }
                        table.AddBodyRow(new string[] { backups[i].backuphash[..7],
                        backups[i].backuptime.ToLocalTime().ToString(), Utilities.BytesFormatter(allreferencesizes),
                        Utilities.BytesFormatter(uniquereferencesizes), message[..mlength] });
                    }
                }
                else
                {
                    table.AddHeaderRow(new string[] { "Hash", "Saved", "Message" });
                    for (int i = backups.Count - 1; i >= backups.Count - show; i--)
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
                Console.WriteLine(table);
            }
        }

        private static async Task SyncCache(SyncCacheOptions opts)
        {
            try
            {
                var bcore = (await LoadCore()).core;
                string bsname = await GetBackupSetName(opts.BSName, bcore.SrcDependencies);
                await bcore.SyncCacheSaveBackupSets(bsname);
                await bcore.SaveBlobIndices();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public static async Task<string> GetBackupSetName(string? bsname, ICoreSrcDependencies srcDependencies)
        {
            if (bsname == null)
            {
                bsname = await ReadSetting(srcDependencies, BackupSetting.name);
                if (bsname == null)
                {
                    Console.WriteLine("A backup store name must be specified with \"set name <name>\"");
                    Console.WriteLine("or the store name must be specified with the -n flag.");
                    throw new Exception(); // TODO: more specific exceptions
                }
            }
            return bsname;
        }

        public static async Task<(Core core, Dictionary<BackupDestinationSpecification, ICoreDstDependencies> destinations)> LoadCore(HashSet<string>? includeDestinations = null)
        {
            var srcdep = FSCoreSrcDependencies.Load(CWD, new DiskFSInterop());

            List<BackupDestinationSpecification>? destinationSpecifications = null;
            BackupDestinationSpecification? cacheSpecification = null;
            try
            {
                var settings = await srcdep.ReadSettingsV2();
                destinationSpecifications = settings.Destinations;
                cacheSpecification = settings.Cache;
            }
            catch (Exception) // V2 not available, read old settings file into new model
            {
                string? cache;
                string? destinations;
                try
                {
                    destinations = await srcdep.ReadSetting(BackupSetting.dests);
                }
                catch (KeyNotFoundException)
                {
                    destinations = null;
                }

                try
                {
                    cache = await srcdep.ReadSetting(BackupSetting.cache);
                }
                catch (KeyNotFoundException)
                {
                    cache = null;
                }

                if (destinations != null)
                {
                    if (cache != null)
                    {
                        cacheSpecification = new();
                        cacheSpecification.Name = "cache";
                        cacheSpecification.Type = DestinationType.Filesystem;
                        cacheSpecification.Path = cache;
                    }

                    Dictionary<DestinationType, int> unnamedDestinationTypeCounts = new();
                    destinationSpecifications = new();
                    foreach (var destination in destinations.Split(';'))
                    {
                        BackupDestinationSpecification destinationSpecification = new();
                        string[] dst_passopt_cc = destination.Split('|');
                        string dst_path = dst_passopt_cc[0].Trim();
                        if (dst_path.ToLower() == "backblaze")
                        {
                            destinationSpecification.Type = DestinationType.Backblaze;
                            destinationSpecification.CloudConfig = dst_passopt_cc[2].Trim() == "" ? null : dst_passopt_cc[2].Trim();

                            if (!unnamedDestinationTypeCounts.ContainsKey(DestinationType.Backblaze))
                            {
                                unnamedDestinationTypeCounts[DestinationType.Backblaze] = 1;
                            }
                            else
                            {
                                unnamedDestinationTypeCounts[DestinationType.Backblaze]++;
                            }
                            destinationSpecification.Name = $"Unnamed backblaze destination #{unnamedDestinationTypeCounts[DestinationType.Backblaze]}";
                        }
                        else
                        {
                            destinationSpecification.Type = DestinationType.Filesystem;
                            destinationSpecification.Path = dst_path;
                            destinationSpecification.Name = destinationSpecification.Path;
                        }
                        destinationSpecification.UsePassword = dst_passopt_cc[1].Trim().ToLower() == "p";

                        destinationSpecifications.Add(destinationSpecification);
                    }
                }
            }

            if (destinationSpecifications == null)
            {
                string? destination = GetBUDestinationDir();
                if (destination != null) // We are in a backup destination
                {
                    BackupDestinationSpecification destinationSpecification = new();
                    destinationSpecification.Name = destination;
                    destinationSpecification.Type = DestinationType.Filesystem;
                    destinationSpecification.Path = destination;

                    try
                    {
                        // TODO: password support here
                        var core = await Core.LoadDiskCore(null, new List<(string, string?)>(1) { (destination, null) }, null);
                        return 
                            (core, 
                            new Dictionary<BackupDestinationSpecification, ICoreDstDependencies>
                            {
                                { destinationSpecification, core.DefaultDstDependencies[0] }
                            });
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
                if (cacheSpecification != null)
                {
                    if (cacheSpecification.Type != DestinationType.Filesystem || cacheSpecification.Path == null)
                    {
                        throw new Exception("Cache must be of type filesystem and provide a path");
                    }
                    cachedep = await CoreDstDependencies.Load(await DiskDstFSInterop.Load(cacheSpecification.Path));
                }

                IEnumerable<BackupDestinationSpecification> selectedDestinations;
                if (includeDestinations != null)
                {
                    selectedDestinations = destinationSpecifications.Where(d => d.Name != null && includeDestinations.Contains(d.Name));
                }
                else
                {
                    selectedDestinations = destinationSpecifications;
                }
                Dictionary<BackupDestinationSpecification, ICoreDstDependencies> dstdeps = new();
                foreach (var destination in selectedDestinations)
                {
                    string? password = null;
                    if (destination.UsePassword)
                    {
                        password = PasswordPrompt();
                    }

                    if (destination.Type == DestinationType.Backblaze) // TODO: Backblaze should be a specific implementation of general cloud sync support
                    {
                        try
                        {
                            if (destination.CloudConfig == null)
                            {
                                throw new Exception("Backblaze backups require a cloud config file to be specified");
                            }
                            dstdeps.Add(destination, await CoreDstDependencies.Load(await BackblazeDstInterop.Load(destination.CloudConfig, password), cacheSpecification != null));
                        }
                        catch
                        {
                            Console.WriteLine("Failed to load backblaze");
                        }
                    }
                    else
                    {
                        if (destination.Path == null)
                        {
                            throw new Exception("Path must be provided with filesystem type destination");
                        }
                        try
                        {
                            dstdeps.Add(destination, await CoreDstDependencies.Load(await DiskDstFSInterop.Load(destination.Path, password), cacheSpecification != null));
                        }
                        catch (Exception)
                        {
                            Console.WriteLine($"Failed to load {destination.Name}");
                        }
                    }
                }
                return (new Core(srcdep, dstdeps.Values.ToList(), cachedep), dstdeps);
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

        public static async Task BrowseBackup(BrowseOptions opts)
        {
            var (bcore, dests) = await LoadCore();
            string bsname = await GetBackupSetName(opts.BSName, bcore.SrcDependencies);
            var browser = await BackupBrowser.Initialize(bsname, opts.BackupHash, bcore, dests);
            browser.CommandLoop();
        }

        public static async Task TransferBackupStore(TransferOptions opts)
        {
            var bcore = (await LoadCore()).core;
            string backupsetname = await GetBackupSetName(opts.BSName, bcore.SrcDependencies);
            // TODO: password support
            await bcore.TransferBackupSet(backupsetname, await Core.InitializeNewDiskCore(backupsetname, null, new List<(string, string?)>(1) { (opts.Destination, null) }), true);
        }

        public static async Task<string> ReadSetting(ICoreSrcDependencies src, BackupSetting key) => await src.ReadSetting(key);

        private static async Task WriteSetting(ICoreSrcDependencies src, BackupSetting key, string value) => await src.WriteSetting(key, value);

        private static async Task ClearSetting(ClearOptions opts)
        {
            Core core = (await LoadCore()).core;
            await core.SrcDependencies.ClearSetting(opts.Setting);
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
