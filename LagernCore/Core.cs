using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using BackupCore.Models;
using LagernCore.Models;
using LagernCore.BackupCalculation;
using LagernCore.Utilities;

namespace BackupCore
{
    // Calling a public method that modifies blobs or backups automatically triggers an index save
    public class Core
    {
        /// <summary>
        /// True if the regular backup destination is available.
        /// If the destination is not available we attempt to use the cache.
        /// </summary>
        public bool DestinationAvailable { get; set; }

        public ICoreSrcDependencies SrcDependencies { get; set; }
        // TODO: Rename to DstsDependencies
        public List<ICoreDstDependencies> DefaultDstDependencies { get; set; }
        public ICoreDstDependencies? CacheDependencies { get; set; }

        public static readonly string BackupBlobIndexFile = "hashindex";

        public static readonly string SettingsFilename = ".settings";

        public Core(ICoreSrcDependencies src, List<ICoreDstDependencies> destinations, ICoreDstDependencies? cache = null)
        {
            SrcDependencies = src;
            if (destinations == null || destinations.Count == 0)
            {
                DestinationAvailable = false;
                if (cache != null)
                {
                    DefaultDstDependencies = new List<ICoreDstDependencies>(1) { cache };
                }
                else
                {
                    throw new ArgumentNullException(nameof(cache), "Dst and cache are null, cannot initialize");
                }
            }
            else
            {
                DestinationAvailable = true;
                DefaultDstDependencies = destinations;
            }
            CacheDependencies = cache;
        }
        
        /// <summary>
        /// Loads and existing lagern Core. Core surfaces all public methods for use by programs implementing this library.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="src">Backup source directory</param>
        /// <param name="dst">Backup destination directory</param>
        public static async Task<Core> LoadDiskCore(string? src, 
            IEnumerable<(string dst_path, string? password)> dsts, string? cache=null)
        {
            FSCoreSrcDependencies srcdep = FSCoreSrcDependencies.Load(src, new DiskFSInterop());
            List<ICoreDstDependencies> dstdeps = new();
            foreach (var (dst_path, password) in dsts)
            {
                try
                {
                    dstdeps.Add(await CoreDstDependencies.Load(await DiskDstFSInterop.Load(dst_path, password), cache != null));
                }
                catch (Exception)
                {
                    Console.WriteLine($"Failed to load {dst_path}");
                }
            }
            
            CoreDstDependencies? cachedep = null;
            if (cache != null)
            {
                cachedep = await CoreDstDependencies.Load(await DiskDstFSInterop.Load(cache));
            }
            return new Core(srcdep, dstdeps, cachedep);
        }

        // TODO: This method shouldn't exist in Core, it should be in its own class of similar helper methods,
        // or simply not exist at all
        public static async Task<Core> InitializeNewDiskCore(string bsname, string? src, 
            IEnumerable<(string dst_path, string? password)> dsts, string? cache = null)
        {
            ICoreSrcDependencies srcdep = 
                await FSCoreSrcDependencies.InitializeNew(bsname, src, new DiskFSInterop(), cache);
            List<ICoreDstDependencies> dstdeps = new();
            foreach (var (dst_path, password) in dsts)
            {
                dstdeps.Add(await CoreDstDependencies.InitializeNew(bsname, false, 
                    await DiskDstFSInterop.InitializeNew(dst_path, password), cache != null));
            }
            CoreDstDependencies? cachedep = null;
            if (cache != null)
            {
                cachedep = await CoreDstDependencies.InitializeNew(bsname, true, await DiskDstFSInterop.InitializeNew(cache), false);
            }
            return new Core(srcdep, dstdeps, cachedep);
        }

        /// <summary>
        /// Gets list of changes, relative to at most one previous backup
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="differentialbackup"></param>
        /// <param name="trackpatterns"></param>
        /// <param name="prev_backup_hash_prefix"></param>
        /// <returns></returns>
        public async Task<List<(string path, FileMetadata.FileStatus change)>> GetWTStatus(
            string backupsetname, bool differentialbackup = true,
            List<(int trackclass, string pattern)>? trackpatterns = null, 
            string? prev_backup_hash_prefix = null)
        {
            BackupSetReference backupSetReference = new(backupsetname, false, false, false);
            if (!DestinationAvailable)
            {
                backupSetReference = backupSetReference with { Cache = true };
            }

            MetadataNode deltatree;

            if (differentialbackup)
            {
                BackupRecord? previousbackup;
                try
                {
                    // Assume all destinations have the same most recent backup, so just use the first backup
                    previousbackup = await DefaultDstDependencies[0].Backups
                        .GetBackupRecord(backupSetReference, prev_backup_hash_prefix);
                }
                catch
                {
                    try
                    {
                        previousbackup = await DefaultDstDependencies[0].Backups.GetBackupRecord(backupSetReference);
                    }
                    catch
                    {
                        previousbackup = null;
                    }
                }
                if (previousbackup != null)
                {
                    MetadataNode previousmtree = await MetadataNode.Load(DefaultDstDependencies[0].Blobs, 
                        previousbackup.MetadataTreeHash);
                    deltatree = (await BackupCalculation.GetDeltaMetadataTrees(SrcDependencies, DestinationAvailable, backupsetname,
                        new List<(ICoreDstDependencies dst, MetadataNode? node)>() { (DefaultDstDependencies[0], previousmtree) },
                        trackpatterns))[0].node; // TODO: Dont arbitrarily select the first destination
                }
                else
                {
                    deltatree = (await BackupCalculation.GetDeltaMetadataTree(SrcDependencies, DestinationAvailable, backupsetname, trackpatterns)).node;
                }
            }
            else
            {
                deltatree = (await BackupCalculation.GetDeltaMetadataTree(SrcDependencies, DestinationAvailable, backupsetname, trackpatterns)).node;
            }
            List<(string path, FileMetadata.FileStatus change)> changes = new();
            GetDeltaNodeChanges(Path.DirectorySeparatorChar.ToString(), deltatree, changes);

            return changes;
        }

        /// <summary>
        /// Recursive helper function to traverse the delta tree and accumulate changes.
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="node"></param>
        /// <param name="changes"></param>
        private static void GetDeltaNodeChanges(string relpath, MetadataNode node, 
            List<(string path, FileMetadata.FileStatus change)> changes)
        {
            if (node.DirMetadata.Status == null)
            {
                throw new Exception("Reached metadata without delta");
            }
            var status = node.DirMetadata.Status;
            if (status != null)
            {
                var valstatus = status.Value;
                if (status == FileMetadata.FileStatus.Deleted)
                {
                    changes.Add((relpath, valstatus));
                    // If deleted dont handle children
                }
                else
                {
                    // Not deleted so will handle children
                    if (status != FileMetadata.FileStatus.Unchanged)
                    {
                        changes.Add((relpath, valstatus));
                    }
                    foreach (var file in node.Files.Values)
                    {
                        if (file.Status == null)
                        {
                            throw new Exception("Reached metadata without delta");
                        }
                        var fstatus = file.Status;
                        if (fstatus != null)
                        {
                            var valfstatus = fstatus.Value;
                            if (fstatus != FileMetadata.FileStatus.Unchanged)
                            {
                                changes.Add((Path.Combine(relpath, file.FileName), valfstatus));
                            }
                        }
                    }
                    foreach (var dir in node.Directories.Values)
                    {
                        GetDeltaNodeChanges($"{Path.Combine(relpath, dir.DirMetadata.FileName)}{Path.DirectorySeparatorChar}", 
                            dir, changes);
                    }
                }
            }
        }

        /// <summary>
        /// Performs a backup, parallelizing backup tasks and using all CPU cores as much as possible.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="message"></param>
        /// <param name="differential">True if we attempt to avoid scanning file data when the 
        /// data appears not to have been modified based on its metadata, otherwise all file data is scanned.</param>
        /// <param name="trackpatterns">Rules determining which files</param>
        /// <param name="prev_backup_hash_prefixes">Optional list of backup hash prefixes, 
        /// each index-aligned with a destination from DefaultDstDependencies.
        /// All destinations are backed up to regardless of the length of this list.</param>
        /// <returns>The hash of the new backup</returns>
        public async Task<Result<byte[], KeyNotFoundException>> RunBackup(
            string backupsetname, string message, bool parallel = true, bool differential = true,
            List<(int trackclass, string pattern)>? trackpatterns = null,
            List<string?>? prev_backup_hash_prefixes = null)
        {
            await SyncCacheSaveBackupSets(backupsetname);

            BackupSetReference backupSetReference = new(backupsetname, false, false, false);
            if (!DestinationAvailable)
            {
                backupSetReference = backupSetReference with { Cache = true };
            }

            // Aggregate a list of stored MetadataTrees,
            // Individual trees are null if the backup doesnt exist.
            List<(ICoreDstDependencies dst, MetadataNode? node)> previousMTrees = new();
            foreach ((ICoreDstDependencies dst, int d) in DefaultDstDependencies.Zip(Enumerable.Range(0, DefaultDstDependencies.Count)))
            {
                if (differential)
                {
                    if (prev_backup_hash_prefixes != null && d < prev_backup_hash_prefixes.Count && prev_backup_hash_prefixes[d] != null)
                    {
                        try
                        {
                            BackupRecord previousbackup =
                                await dst.Backups.GetBackupRecord(backupSetReference, prev_backup_hash_prefixes[d]);
                            previousMTrees.Add((dst, await MetadataNode.Load(dst.Blobs, previousbackup.MetadataTreeHash)));
                        }
                        catch (KeyNotFoundException)
                        {
                            return Result<byte[], KeyNotFoundException>.Err(new KeyNotFoundException("No backup matches the specified hash"));
                        }
                    }
                    else
                    {
                        try
                        {
                            // If null hash given, default to a differential backup relative to the last backup done in this destination
                            BackupRecord previousbackup = await dst.Backups.GetBackupRecord(backupSetReference);
                            previousMTrees.Add((dst, await MetadataNode.Load(dst.Blobs, previousbackup.MetadataTreeHash)));
                        } 
                        catch (IndexOutOfRangeException)
                        {
                            // No backup records in this destination, so do a non-differential backup
                            previousMTrees.Add((dst, null));
                        }
                    }
                }
                else
                {
                    previousMTrees.Add((dst, null));
                }
            }

            // Calculate the difference between the current file system state and each of the previous Metadata Trees
            List<(ICoreDstDependencies dst, MetadataNode node)> deltatrees = 
                await BackupCalculation.GetDeltaMetadataTrees(SrcDependencies, DestinationAvailable, backupsetname, previousMTrees, trackpatterns);

            List<Task> backupops = new();
            await BackupDeltaNode(Path.DirectorySeparatorChar.ToString(),
                deltatrees, backupsetname, parallel, backupops);
            
            if (parallel)
            {
                await Task.WhenAll(backupops); // These tasks should already be executing in parallel
                // Another option could be something like:
                //await Parallel.ForEachAsync(backupops, (f, _) => f.Invoke()); // Using Parallel here should help us limit the degree of parallelism
                // Where each item in backupops is a Func<ValueTask>
            }

            /*
            // Add new metadatatree to metastore
            byte[] newmtreebytes = newmetatree.serialize();
            //byte[] newmtreehash = Blobs.StoreDataAsync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);
            byte[] newmtreehash = Blobs.StoreDataSync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);
            */

            List<(byte[] mtreehash, HashTreeNode references)> newmtreehashes =
                (await Task.WhenAll(deltatrees.Select(deltatree => 
                    deltatree.node.Store((byte[] data) =>
                        BlobStore.StoreData(new List<BlobStore> { deltatree.dst.Blobs }, backupSetReference, data)))))
                .ToList();

            byte[]? backuphash = null;
            DateTime backupTime = DateTime.UtcNow;
            for (int i = 0; i < DefaultDstDependencies.Count; i++)
            {
                var dst = DefaultDstDependencies[i];
                var defaultbset = await dst.Backups.LoadBackupSet(backupSetReference);
                backuphash = await dst.Backups.AddBackup(backupSetReference, message, newmtreehashes[i].mtreehash, 
                    backupTime, defaultbset);
                await dst.Backups.SaveBackupSet(defaultbset, backupSetReference);
                // Backup record has just been stored, all data now stored

                // Finalize backup by incrementing reference counts in blobstore as necessary
                await dst.Blobs.FinalizeBackupAddition(backupSetReference, backuphash, newmtreehashes[i].mtreehash, 
                    newmtreehashes[i].references);
            }

            // TODO: Only really need to sync cache with one destination
            await SyncCacheSaveBackupSets(backupsetname);

            await SaveBlobIndices();
            if (backuphash == null)
            {
                throw new NullReferenceException("backuphash cannot be null after a backup");
            }
            return Result<byte[], KeyNotFoundException>.Ok(backuphash);
        }

        private async Task BackupDeltaNode(
            string relpath, List<(ICoreDstDependencies dst, MetadataNode node)> indexednodes, string backupsetname, bool parallel,
            List<Task> backupops)
        {
            var allfiles = new Dictionary<string, List<(ICoreDstDependencies dst, FileMetadata fileMetadata)>>(); // Filename to destinations that need that file
            foreach (var (dst, node) in indexednodes)
            {
                foreach (var filenameandmeta in node.Files)
                {
                    // Prune out deleted files, they dont affect the backup
                    if (filenameandmeta.Value.Status == FileMetadata.FileStatus.Deleted)
                    {
                        node.Files.TryRemove(filenameandmeta.Key, out _);
                    }
                    else
                    {
                        if (allfiles.ContainsKey(filenameandmeta.Key))
                        {
                            allfiles[filenameandmeta.Key].Add((dst, filenameandmeta.Value));
                        }
                        else
                        {
                            allfiles[filenameandmeta.Key] = new List<(ICoreDstDependencies, FileMetadata)> { (dst, filenameandmeta.Value) };
                        }
                    }
                }
            }
            foreach (var filename in allfiles.Keys)
            {
                // Data writes to destinations
                List<(ICoreDstDependencies dst, FileMetadata fileMetadata)> writedestinations = 
                    allfiles[filename]
                    .Where(difm =>
                        difm.fileMetadata.Status == FileMetadata.FileStatus.New || 
                        difm.fileMetadata.Status == FileMetadata.FileStatus.DataModified)
                    .Select((difm) => (difm.dst, difm.fileMetadata))
                    .ToList();

                if (writedestinations.Any())
                {
                    if (parallel)
                    {
                        backupops.Add(BackupFileSync(backupsetname, Path.Combine(relpath, filename), writedestinations));
                    }
                    else
                    {
                        await BackupFileSync(backupsetname, Path.Combine(relpath, filename), writedestinations);
                    }
                }
            }

            var alldirs = new Dictionary<string, List<(ICoreDstDependencies dst, MetadataNode metadataNode)>>(); // Filename to destinations that need that file
            foreach (var (dst, node) in indexednodes)
            {
                foreach (var dirnameandnode in node.Directories)
                {
                    // Prune out deleted dirs, they dont affect the backup
                    if (dirnameandnode.Value.DirMetadata.Status == FileMetadata.FileStatus.Deleted)
                    {
                        node.Directories.TryRemove(dirnameandnode.Key, out _);
                    }
                    else
                    {
                        if (alldirs.ContainsKey(dirnameandnode.Key))
                        {
                            alldirs[dirnameandnode.Key].Add((dst, dirnameandnode.Value));
                        }
                        else
                        {
                            alldirs[dirnameandnode.Key] = new List<(ICoreDstDependencies, MetadataNode)> { (dst, dirnameandnode.Value) };
                        }
                    }
                }
            }
            foreach (var dir in alldirs)
            {
                await BackupDeltaNode($"{Path.Combine(relpath, dir.Key)}{Path.DirectorySeparatorChar}", 
                    dir.Value, backupsetname, parallel, backupops);
            }
        }

        /// <summary>
        /// Saves both destination and cache blob indices (as available).
        /// </summary>
        public async Task SaveBlobIndices()
        {
            // Save "index"
            // Writeout all "dirty" cached index nodes
            try
            {
                await Task.WhenAll(DefaultDstDependencies.Select(x => x.SaveBlobStoreIndex()));
                if (CacheDependencies != null && DestinationAvailable)
                {
                    await CacheDependencies.SaveBlobStoreIndex();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        // TODO: Return backup sets from DefaultDstDependencies.Backups.SyncCache()
        // then save backup sets in a seperate method
        public async Task SyncCacheSaveBackupSets(string backupsetname, bool savebackupsets = true)
        {
            if (CacheDependencies != null && DestinationAvailable)
            {
                int count = 0;

                BackupSet bset;
                BackupSet cachebset;
                // When Issue #24 is resolved, syncing will be done with each of the destinations, for now the code below
                // leaves the cache in a synchronized state relative to the first backup destination
                //(bset, cachebset) = dst.Backups.SyncCache(CacheDependencies.Backups, backupsetname, cachebset: cachebset);
                (bset, cachebset) = await DefaultDstDependencies[0].Backups.SyncCache(CacheDependencies.Backups, backupsetname);
                await CacheDependencies.Backups.SaveBackupSet(cachebset, new BackupSetReference(backupsetname, false, true, false));

                if (savebackupsets)
                {
                    BackupSetReference backupSetReference = new(backupsetname, false, false, false);
                    await DefaultDstDependencies[0].Backups.SaveBackupSet(bset, backupSetReference);
                    foreach (var dst in DefaultDstDependencies.Skip(1))
                    {
                        bset = await dst.Backups.LoadBackupSet(backupSetReference);
                        await dst.Backups.SaveBackupSet(bset, backupSetReference);
                        count += 1;
                    }
                }
            }
        }

        /// <summary>
        /// Restore a backed up file. Includes metadata.
        /// </summary>
        /// <param name="relfilepath"></param>
        /// <param name="restorepath"></param>
        /// <param name="backupindex"></param>
        public async Task RestoreFileOrDirectory(string backupsetname, string relfilepath, string restorepath, 
            string? backuphashprefix=null, bool absoluterestorepath=false, int backupdst=0)
        {
            BackupSetReference backupSetReference = new(backupsetname, false, false, false);
            if (!DestinationAvailable)
            {
                backupSetReference = backupSetReference with { Cache = true };
            }

            try
            {
                var backup = await DefaultDstDependencies[backupdst].Backups.GetBackupRecord(backupSetReference, backuphashprefix);
                MetadataNode mtree = await MetadataNode.Load(DefaultDstDependencies[backupdst].Blobs, backup.MetadataTreeHash);
                FileMetadata? filemeta = mtree.GetFile(relfilepath);
                if (filemeta != null)
                {
                    if (filemeta.FileHash == null)
                    {
                        throw new Exception("FileMetadata of stored files should always contain the files hash");
                    }
                    byte[] filedata = await DefaultDstDependencies[backupdst].Blobs.RetrieveData(filemeta.FileHash);
                    await SrcDependencies.OverwriteOrCreateFile(restorepath, filedata, filemeta, absoluterestorepath);
                }
                else
                {
                    MetadataNode? dir = mtree.GetDirectory(relfilepath);
                    if (dir != null)
                    {
                        await SrcDependencies.CreateDirectory(restorepath, absoluterestorepath);
                        foreach (var childfile in dir.Files.Values)
                        {
                            await RestoreFileOrDirectory(backupsetname, Path.Combine(relfilepath, childfile.FileName), 
                                Path.Combine(restorepath, childfile.FileName), backuphashprefix, absoluterestorepath);
                        }
                        foreach (var childdir in dir.Directories.Keys)
                        {
                            await RestoreFileOrDirectory(backupsetname, Path.Combine(relfilepath, childdir), 
                                Path.Combine(restorepath, childdir), backuphashprefix, absoluterestorepath);
                        }
                        await SrcDependencies.WriteOutMetadata(restorepath, dir.DirMetadata, absoluterestorepath); // Set metadata after finished changing contents (postorder)
                    }
                    else
                    {
                        throw new Exception("File/directory to restore not found");
                    }
                }
            }
            catch (Exception)
            {
                if (!DestinationAvailable)
                {
                    Console.WriteLine("Error restoring file or directory." +
                        "Try restoring again with backup drive present");
                }
                else
                {
                    Console.WriteLine("Error restoring file or directory.");
                }
                throw;
            }
        }

        public static async Task<List<(int trackclass, string pattern)>> ReadTrackClassFile(string trackfilepath)
        {
            List<(int, string)> trackclasses = new();
            using (FileStream fs = new(trackfilepath, FileMode.Open))
            {
                using StreamReader reader = new(fs);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    string[] ctp = line.Split(' ');
                    trackclasses.Add((Convert.ToInt32(ctp[0]), ctp[1]));
                }
            }
            return trackclasses;
        }

        /// <summary>
        /// Calculates the size of the blobs and child blobs referenced by the given hash.
        /// </summary>
        /// <param name="backuphashstring"></param>
        /// <returns>(Size of all referenced blobs, size of blobs referenced only by the given hash and its children)</returns>
        public async Task<(int allreferencesizes, int uniquereferencesizes)> GetBackupSizes(
            BackupSetReference bsname, string backuphashstring, int backupdst=0)
        {
            var br = await DefaultDstDependencies[backupdst].Backups.GetBackupRecord(bsname, backuphashstring);
            return await DefaultDstDependencies[backupdst].Blobs.GetSizes(br.MetadataTreeHash, BlobLocation.BlobType.MetadataNode);
        }

        /// <summary>
        /// Calculates the size of the blobs and child blobs referenced by the given hash.
        /// </summary>
        /// <param name="backuphashstring"></param>
        /// <returns>(Size of all referenced blobs, size of blobs referenced only by the given hash and its children)</returns>
        public async Task<(int allreferencesizes, int uniquereferencesizes)> GetBackupSizes(
            string bsname, string backuphashstring, int backupdst = 0)
        {
            return await GetBackupSizes(new BackupSetReference(bsname, false, false, false), backuphashstring, backupdst);
        }

        /// <summary>
        /// Backup a file to all destinations and save its hash to the given filemetadata
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="mtree"></param>
        protected async Task BackupFileSync(string backupset, string relpath, 
            List<(ICoreDstDependencies dst, FileMetadata fileMetadata)> writedestinations)
        {
            try
            {
                if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    relpath = relpath[1..];
                }
                using Stream readerbuffer = await SrcDependencies.GetFileData(relpath);
                byte[] filehash = await BlobStore.StoreData(
                    writedestinations.Select(difm => difm.dst.Blobs), 
                    new BackupSetReference(backupset, false, false, false), readerbuffer);
                foreach (var (_, fileMetadata) in writedestinations)
                {
                    fileMetadata.FileHash = filehash;
                }
            }
            catch (Exception e)
            {
                throw new IOException($"Failed to backup {relpath}", e);
            }
        }
        
        /// <summary>
        /// Retrieves list of backups from a backupset.
        /// </summary>
        /// <returns>A list of tuples representing the backup times and their associated messages.</returns>
        public async Task<(IEnumerable<(string backuphash, DateTime backuptime, string message)> backups, bool cache)> GetBackups(
            string backupsetname, int backupdst)
        {
            BackupSetReference backupSetReference = new(backupsetname, false, false, false);
            if (!DestinationAvailable)
            {
                backupSetReference = backupSetReference with { Cache = true };
            }

            List<(string, DateTime, string)> backups = new();
            foreach (var (hash, _) in (await DefaultDstDependencies[backupdst].Backups.LoadBackupSet(backupSetReference)).Backups)
            {
                var br = await DefaultDstDependencies[backupdst].Backups.GetBackupRecord(backupSetReference, hash);
                backups.Add((HashTools.ByteArrayToHexViaLookup32(hash).ToLower(),
                    br.BackupTime, br.BackupMessage));
            }
            return (backups, !DestinationAvailable);
        }

        /// <summary>
        /// Remove a backup from the BackupStore and its data from the BlobStore.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="backuphashprefix"></param>
        public async Task RemoveBackup(string backupsetname, string backuphashprefix, bool forcedelete = false, int backupdst = 0)
        {
            BackupSetReference backupSetReference = new(backupsetname, false, false, false);
            await SyncCacheSaveBackupSets(backupsetname); // Sync cache first to prevent deletion of data in dst relied on by an unmerged backup in cache
            await DefaultDstDependencies[backupdst].Backups.RemoveBackup(backupSetReference, backuphashprefix, 
                DestinationAvailable && CacheDependencies == null, forcedelete);
            await SyncCacheSaveBackupSets(backupsetname);
            await SaveBlobIndices();
        }

        /// <summary>
        /// Transfer a backupset and its data to a new location.
        /// </summary>
        /// <param name="src">The Core containing the backup store (and backing blobstore) to be transferred.</param>
        /// <param name="dst">The lagern directory you wish to transfer to.</param>
        public async Task TransferBackupSet(BackupSetReference backupsetname, Core dstCore, bool includefiles, 
            int backupDstTransferSrc=0, int backupDstTransferDst=0)
        {
            // TODO: This function probably makes more sense transferring between backup destinations within the current Core object
            BackupSet backupSet = await DefaultDstDependencies[backupDstTransferSrc].Backups.LoadBackupSet(backupsetname);
            // Transfer backup set
            await dstCore.DefaultDstDependencies[backupDstTransferDst].Backups.SaveBackupSet(backupSet, backupsetname);
            // Transfer backing data
            foreach (var (hash, shallow) in backupSet.Backups)
            {
                await DefaultDstDependencies[backupDstTransferSrc].Blobs.TransferBackup(
                    dstCore.DefaultDstDependencies[backupDstTransferDst].Blobs, backupsetname, hash, includefiles & !shallow);
            }
            await dstCore.DefaultDstDependencies[backupDstTransferDst].SaveBlobStoreIndex();
        }

        /// <summary>
        /// Transfer a backupset and its data to a new location.
        /// </summary>
        /// <param name="src">The Core containing the backup store (and backing blobstore) to be transferred.</param>
        /// <param name="dst">The lagern directory you wish to transfer to.</param>
        public async Task TransferBackupSet(string backupsetname, Core dstCore, bool includefiles,
            int backupDstTransferSrc = 0, int backupDstTransferDst = 0)
        {
            await TransferBackupSet(new BackupSetReference(backupsetname, false, false, false), dstCore, includefiles, backupDstTransferSrc, backupDstTransferDst);
        }

        // TODO: Add method for transferring individual backup

        public class BackupRemoveException : Exception
        {
            public BackupRemoveException(string message) : base(message) { }
        }
    }
    
    public enum BackupSetting
    {
        dests,
        cache,
        name
    }

    public enum IndexFileType
    {
        BlobIndex,
        BackupSet,
        SettingsFile,
        EncryptorKeyFile
    }
}
