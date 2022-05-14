using BackupCore;
using LagernCore.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LagernCore.BackupCalculation
{
    public class BackupCalculation
    {
        /// <summary>
        /// Calculates the difference between the current filesystem status 
        /// and the previously saved metadata trees, if any.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="trackpatterns"></param>
        /// <returns>A delta tree mapping</returns>
        public static async Task<(ICoreDstDependencies? dst, MetadataNode node)> GetDeltaMetadataTree(
            ICoreSrcDependencies source, bool destinationAvailable, string backupsetname,
            List<(int trackclass, string pattern)>? trackpatterns = null)
        {
            return (await GetDeltaMetadataTrees(source, destinationAvailable, backupsetname, trackpatterns, null))[0];
        }

        /// <summary>
        /// Calculates the difference between the current filesystem status 
        /// and the previously saved metadata trees, if any.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="previousmtrees">Optional list of previously backed up metadata trees,
        /// each tree in the list is also optional.</param>
        /// <param name="trackpatterns"></param>
        /// <returns>A delta tree mapping</returns>
        public static async Task<List<(ICoreDstDependencies dst, MetadataNode node)>> GetDeltaMetadataTrees(
            ICoreSrcDependencies source, bool destinationAvailable, string backupsetname,
            List<(ICoreDstDependencies dst, MetadataNode? node)> previousmtrees,
            List<(int trackclass, string pattern)>? trackpatterns = null)
        {
            // The left item in the tuple (ICoreDstDependencies dst), is never null when we pass the private method a non-null list of previous mtrees
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
            return await GetDeltaMetadataTrees(source, destinationAvailable, backupsetname, trackpatterns, previousmtrees);
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
        }

        /// <summary>
        /// Calculates the difference between the current filesystem status 
        /// and the previously saved metadata trees, if any.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="trackpatterns"></param>
        /// <param name="previousmtrees">Optional list of previously backed up metadata trees,
        /// each tree in the list is also optional.</param>
        /// <returns>A delta tree mapping</returns>
        private static async Task<List<(ICoreDstDependencies? dst, MetadataNode node)>> GetDeltaMetadataTrees(
            ICoreSrcDependencies source, bool destinationAvailable, string backupsetname, 
            List<(int trackclass, string pattern)>? trackpatterns = null, 
            List<(ICoreDstDependencies dst, MetadataNode? node)>? previousmtrees = null)
        {
            BackupSetReference backupSetReference = new(backupsetname, false, false, false);
            if (!destinationAvailable)
            {
                backupSetReference = backupSetReference with { Cache = true };
            }

            // Non differential backup equivalent to differential backup to single destination without a previous tree
            List<(ICoreDstDependencies dst, MetadataNode? node)?> previousMTreesToDiff;
            if (previousmtrees == null)
            {
                previousMTreesToDiff = new List<(ICoreDstDependencies dst, MetadataNode? node)?>() { null };
            }
            else
            {
                previousMTreesToDiff = previousmtrees
                    .Select(val => ((ICoreDstDependencies dst, MetadataNode? node)?)val)
                    .ToList();
            }

            FileMetadata rootdirmetadata = await source.GetFileMetadata("");

            // Create a new tree to hold the deltas for each of the previous metadata trees
            List<(ICoreDstDependencies? dst, MetadataNode? previousMTree, MetadataNode diffMTree)> deltamtrees = previousMTreesToDiff
                .Select(diffPair => diffPair != null ? 
                    (diffPair.Value.dst, diffPair.Value.node, new MetadataNode(rootdirmetadata, null)) : 
                    (null, null, new MetadataNode(rootdirmetadata, null)))
                .ToList();

            // Now we will compare these delta trees to the current filesystem state.
            // This involves matching directories and files in the old tree to their new versions.
            // This is mostly done on the basis of the directory/file name, but there is some provision for detecting renames

            foreach (var (dst, previousmtree, deltamtree) in deltamtrees)
            {
                if (dst != null && previousmtree != null)
                {
                    // We always assume the matching of deltatree root to fs backup root is valid
                    // So make the name equal, and set status to metadatachange if they were different
                    // TODO: We should probably just be ignoring the name of the root directory
                    if (previousmtree.DirMetadata.FileName != rootdirmetadata.FileName)
                    {
                        deltamtree.DirMetadata.Status = FileMetadata.FileStatus.MetadataChange;
                    }
                    else
                    {
                        deltamtree.DirMetadata.Status = FileMetadata.FileStatus.Unchanged;
                    }
                }
                else
                {
                    deltamtree.DirMetadata.Status = FileMetadata.FileStatus.New;
                }
            }

            // A queue of paths to directories for which to examine and generate deltas
            Queue<string> deltaMNodeQueue = new();
            // Begin with the root node
            deltaMNodeQueue.Enqueue(Path.DirectorySeparatorChar.ToString());

            while (deltaMNodeQueue.Count > 0)
            {
                string reldirpath = deltaMNodeQueue.Dequeue();
                List<MetadataNode?> posDeltaNodes = deltamtrees.Select(dmt => dmt.diffMTree.GetDirectory(reldirpath)).ToList();
                List<MetadataNode?> previousMNodes = previousMTreesToDiff
                    .Select(mt => mt?.node?.GetDirectory(reldirpath))
                    .ToList();

                // Cleanup the previous metadata nodes we need to compare to
                // Null delta nodes indicate that a directory is not to be backed up for that backup,
                // so we exclude the deltanode and corresponding previousmnode
                List<MetadataNode> filteredDN = new();
                List<MetadataNode?> filteredPN = new();
                for (int i = 0; i < posDeltaNodes.Count; i++)
                {
                    MetadataNode? deltaNode = posDeltaNodes[i];
                    if (deltaNode != null)
                    {
                        filteredDN.Add(deltaNode);
                        filteredPN.Add(previousMNodes[i]);
                    }
                }
                List<MetadataNode> deltaNodes = filteredDN;
                previousMNodes = filteredPN;

                // Files
                // 1. Get list of file names from disk
                // 2. For each previous backup, match the previous backup files with the filesystem files
                // 3. Based on the track class and the metadata comparison between 
                List<string> fsFiles;
                try
                {
                    fsFiles = new List<string>(await source.GetDirectoryFiles(reldirpath));
                    fsFiles.Sort();
                }
                catch (Exception e) when (e is DirectoryNotFoundException || e is UnauthorizedAccessException) // TODO: More user friendly output here
                {
                    throw new Exception("Fetching file system files failed", e);
                }
                catch (Exception e)
                {
                    throw new Exception("Fetching file system files failed", e);
                }

                // The files on the file system are checked in the inner loop, to avoid reloading metadata for each
                // previous backup we are comparing against in the outer loop, we cache the loaded file metadata
                // TODO: Reverse the loop order, so that we get the one on disk file and compare it with each of the previous backups
                Dictionary<string, FileMetadata> fileMetadataCache = new();

                for (int prevMNIdx = 0; prevMNIdx < previousMNodes.Count; prevMNIdx++)
                {
                    var previousMNode = previousMNodes[prevMNIdx];
                    var deltamnode = deltaNodes[prevMNIdx];

                    List<string> previousFiles;
                    int prevIdx = 0;
                    int fsIdx = 0;
                    if (previousMNode != null)
                    {
                        previousFiles = new List<string>(previousMNode.Files.Keys);
                        previousFiles.Sort(); // Both previousFiles and fsFiles now sorted by name, iterate through them testing for matches

                        while (prevIdx < previousFiles.Count && fsIdx < fsFiles.Count)
                        {
                            if (previousFiles[prevIdx] == fsFiles[fsIdx]) // Names match
                            {
                                string fileName = previousFiles[prevIdx];
                                int trackClass = 2; // TODO: make this an application wide constant
                                if (trackpatterns != null)
                                {
                                    trackClass = FileTrackClass(Path.Combine(reldirpath[1..], fileName), trackpatterns);
                                }
                                try // We (may) read the file's metadata here so wrap errors
                                {
                                    if (trackClass != 0)
                                    {
                                        FileMetadata prevfm = previousMNode.Files[fileName]; // NOTE: This previous file always exists, see above
                                        FileMetadata curfm;
                                        if (fileMetadataCache.ContainsKey(fileName))
                                        {
                                            curfm = fileMetadataCache[fileName];
                                        }
                                        else
                                        {
                                            curfm = await source.GetFileMetadata(Path.Combine(reldirpath, fileName));
                                            fileMetadataCache[fileName] = curfm;
                                        }
                                        // Create a copy FileMetada to hold the changes
                                        curfm = new FileMetadata(curfm);

                                        switch (trackClass)
                                        {
                                            case 1: // Dont scan if we have a previous version
                                                if (curfm.FileDifference(prevfm))
                                                {
                                                    curfm.Status = FileMetadata.FileStatus.MetadataChange;
                                                }
                                                else
                                                {
                                                    curfm.Status = FileMetadata.FileStatus.Unchanged;
                                                }
                                                curfm.FileHash = prevfm.FileHash;
                                                break;
                                            case 2: // Dont scan if we have a previous version and its metadata indicates no change
                                                    // If file size and datemodified unchanged we assume no change
                                                if (prevfm.FileSize == curfm.FileSize && prevfm.DateModifiedUTC == curfm.DateModifiedUTC)
                                                {
                                                    // Still update metadata if changed
                                                    if (curfm.FileDifference(prevfm))
                                                    {
                                                        curfm.Status = FileMetadata.FileStatus.MetadataChange;
                                                    }
                                                    else
                                                    {
                                                        curfm.Status = FileMetadata.FileStatus.Unchanged;
                                                    }
                                                    curfm.FileHash = prevfm.FileHash;
                                                }
                                                else // May have been a change to data
                                                {
                                                    curfm.Status = FileMetadata.FileStatus.DataModified;
                                                }
                                                break;
                                            case 3: // Scan file
                                                curfm.Status = FileMetadata.FileStatus.DataModified;
                                                break;
                                            default:
                                                break;
                                        }
                                        deltamnode.AddFile(curfm);
                                    }
                                    else // file exists in previous, but now has tracking class 0, thus is effectively deleted
                                    {
                                        FileMetadata prevfm = previousMNode.Files[fileName];
                                        prevfm = new FileMetadata(prevfm)
                                        {
                                            Status = FileMetadata.FileStatus.Deleted
                                        };
                                        deltamnode.AddFile(prevfm);
                                    }
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e.Message);
                                }
                                prevIdx++;
                                fsIdx++;
                            }
                            else if (previousFiles[prevIdx].CompareTo(fsFiles[fsIdx]) < 0) // deltafiles[deltaidx] earlier in alphabet than fsfiles[fsidx]
                            {
                                // File in old tree but no longer in filesystem
                                string filename = previousFiles[prevIdx];
                                FileMetadata prevfm = previousMNode.Files[filename];
                                prevfm = new FileMetadata(prevfm)
                                {
                                    Status = FileMetadata.FileStatus.Deleted
                                };
                                deltamnode.AddFile(prevfm);
                                prevIdx++;
                            }
                            else // deltafiles[deltaidx] later in alphabet than fsfiles[fsidx]
                            {
                                // File on filesystem not in old tree
                                string filename = fsFiles[fsIdx];
                                int trackclass = 2;
                                if (trackpatterns != null)
                                {
                                    trackclass = FileTrackClass(Path.Combine(reldirpath[1..], filename), trackpatterns);
                                }

                                try
                                {
                                    switch (trackclass)
                                    {
                                        case 0: // dont add if untracked
                                            break;
                                        default:
                                            FileMetadata curfm;
                                            if (fileMetadataCache.ContainsKey(filename))
                                            {
                                                curfm = fileMetadataCache[filename];
                                            }
                                            else
                                            {
                                                curfm = await source.GetFileMetadata(Path.Combine(reldirpath, filename));
                                                fileMetadataCache[filename] = curfm;
                                            }
                                            curfm = new FileMetadata(curfm)
                                            {
                                                Status = FileMetadata.FileStatus.New
                                            };
                                            deltamnode.AddFile(curfm);
                                            break;
                                    }
                                }
                                catch (Exception e)
                                {
                                    // TODO: Get an actual logger
                                    Console.WriteLine(e.Message);
                                }
                                fsIdx++;
                            }
                        }
                        for (; prevIdx < previousFiles.Count; prevIdx++)
                        {
                            // File in old tree but no longer in filesystem
                            string filename = previousFiles[prevIdx];
                            FileMetadata prevfm = previousMNode.Files[filename];
                            prevfm = new FileMetadata(prevfm)
                            {
                                Status = FileMetadata.FileStatus.Deleted
                            };
                            deltamnode.AddFile(prevfm);
                        }
                    }
                    else
                    {
                        previousFiles = new List<string>(0);
                    }

                    for (; fsIdx < fsFiles.Count; fsIdx++)
                    {
                        // File on filesystem not in old tree
                        string filename = fsFiles[fsIdx];
                        int trackclass = 2;
                        if (trackpatterns != null)
                        {
                            trackclass = FileTrackClass(Path.Combine(reldirpath[1..], filename), trackpatterns);
                        }
                        try
                        {
                            if (trackclass != 0) // dont add if untracked
                            {
                                FileMetadata curfm;
                                if (fileMetadataCache.ContainsKey(filename))
                                {
                                    curfm = fileMetadataCache[filename];
                                }
                                else
                                {
                                    curfm = await source.GetFileMetadata(Path.Combine(reldirpath, filename));
                                    fileMetadataCache[filename] = curfm;
                                }
                                curfm = new FileMetadata(curfm)
                                {
                                    Status = FileMetadata.FileStatus.New
                                };
                                deltamnode.AddFile(curfm);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
                }

                // Directories
                // Directories processed similar to files
                // Previously backup up directories compared to filesystem directories directories
                // Only metadata is relevant for directories so the status update is either New, Deleted, MetadataChange, Or Unchanged
                List<string> fssubdirs;
                try
                {
                    // Use GetFileName because GetDirectories doesnt return trailing backslashes, so GetDirectoryName will return the partent directory
                    fssubdirs = new List<string>(await source.GetSubDirectories(reldirpath));
                    fssubdirs.Sort();
                }
                catch (UnauthorizedAccessException e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }
                catch (DirectoryNotFoundException e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }

                Dictionary<string, FileMetadata> dirmetadatacache = new();

                HashSet<string> dirsToQueue = new();

                for (int prevmnidx = 0; prevmnidx < previousMNodes.Count; prevmnidx++)
                {
                    MetadataNode? previousmnode = previousMNodes[prevmnidx];
                    var deltamnode = deltaNodes[prevmnidx];

                    int previdx = 0;
                    int fsidx = 0;
                    List<string> previoussubdirs;
                    if (previousmnode != null)
                    {
                        previoussubdirs = new List<string>(previousmnode.Directories.Keys);
                        previoussubdirs.Sort();

                        while (previdx < previoussubdirs.Count && fsidx < fssubdirs.Count)
                        {
                            if (previoussubdirs[previdx] == fssubdirs[fsidx]) // Names match
                            {
                                string dirname = fssubdirs[fsidx];
                                if (trackpatterns == null || CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, dirname), trackpatterns))
                                {
                                    FileMetadata fssubdirmetadata;
                                    if (dirmetadatacache.ContainsKey(dirname))
                                    {
                                        fssubdirmetadata = dirmetadatacache[dirname];
                                    }
                                    else
                                    {
                                        fssubdirmetadata = await source.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
                                        dirmetadatacache[dirname] = fssubdirmetadata;
                                    }
                                    FileMetadata previousdirmetadata = previousmnode.Directories[dirname].DirMetadata;
                                    FileMetadata.FileStatus status = fssubdirmetadata.DirectoryDifference(previousdirmetadata);
                                    fssubdirmetadata = new FileMetadata(fssubdirmetadata)
                                    {
                                        Status = status
                                    };
                                    deltamnode.AddDirectory(fssubdirmetadata);
                                    dirsToQueue.Add(dirname);
                                }
                                else // We are no longer tracking this directory's children so it has been effectively deleted
                                {
                                    FileMetadata prevfm = previousmnode.Directories[dirname].DirMetadata;
                                    if (!dirmetadatacache.ContainsKey(dirname))
                                    {
                                        dirmetadatacache[dirname] = prevfm;
                                    }
                                    prevfm = new FileMetadata(prevfm)
                                    {
                                        Status = FileMetadata.FileStatus.Deleted
                                    };
                                    deltamnode.AddDirectory(prevfm);
                                }
                                previdx++;
                                fsidx++;
                            }
                            else if (previoussubdirs[previdx].CompareTo(fssubdirs[fsidx]) < 0) // deltasubdirs[deltaidx] earlier in alphabet than fssubdirs[fsidx]
                            {
                                // Directory in oldmtree not but no longer in filesystem
                                string dirname = previoussubdirs[previdx];
                                FileMetadata prevfm = previousmnode.Directories[dirname].DirMetadata;
                                if (!dirmetadatacache.ContainsKey(dirname))
                                {
                                    dirmetadatacache[dirname] = prevfm;
                                }
                                prevfm = new FileMetadata(prevfm)
                                {
                                    Status = FileMetadata.FileStatus.Deleted
                                };
                                deltamnode.AddDirectory(prevfm);
                                // Dont queue because deleted
                                previdx++;
                            }
                            else
                            {
                                // Directory in filesystem not in old tree
                                if (trackpatterns == null || CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, fssubdirs[fsidx]), trackpatterns))
                                {
                                    string dirname = fssubdirs[fsidx];
                                    FileMetadata fssubdirmetadata;
                                    if (dirmetadatacache.ContainsKey(dirname))
                                    {
                                        fssubdirmetadata = dirmetadatacache[dirname];
                                    }
                                    else
                                    {
                                        fssubdirmetadata = await source.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
                                        dirmetadatacache[dirname] = fssubdirmetadata;
                                    }
                                    fssubdirmetadata = new FileMetadata(fssubdirmetadata)
                                    {
                                        Status = FileMetadata.FileStatus.New
                                    };
                                    deltamnode.AddDirectory(fssubdirmetadata);
                                    dirsToQueue.Add(dirname);
                                }
                                fsidx++;
                            }
                        }
                        for (; previdx < previoussubdirs.Count; previdx++)
                        {
                            // Directory in oldmtree not but no longer in filesystem
                            string dirname = previoussubdirs[previdx];
                            FileMetadata prevfm = previousmnode.Directories[dirname].DirMetadata;
                            if (!dirmetadatacache.ContainsKey(dirname))
                            {
                                dirmetadatacache[dirname] = prevfm;
                            }
                            prevfm = new FileMetadata(prevfm)
                            {
                                Status = FileMetadata.FileStatus.Deleted
                            };
                            deltamnode.AddDirectory(prevfm);
                            // Dont queue because deleted
                        }
                    }
                    else
                    {
                        previoussubdirs = new List<string>(0);
                    }

                    for (; fsidx < fssubdirs.Count; fsidx++)
                    {
                        // Directory in filesystem not in old tree
                        if (trackpatterns == null || CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, fssubdirs[fsidx]), trackpatterns))
                        {
                            string dirname = fssubdirs[fsidx];
                            FileMetadata fssubdirmetadata;
                            if (dirmetadatacache.ContainsKey(dirname))
                            {
                                fssubdirmetadata = dirmetadatacache[dirname];
                            }
                            else
                            {
                                fssubdirmetadata = await source.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
                                dirmetadatacache[dirname] = fssubdirmetadata;
                            }
                            fssubdirmetadata = new FileMetadata(fssubdirmetadata)
                            {
                                Status = FileMetadata.FileStatus.New
                            };
                            deltamnode.AddDirectory(fssubdirmetadata);
                            dirsToQueue.Add(dirname);
                        }
                    }
                }

                // Record the changes
                foreach (var dirname in dirsToQueue)
                {
                    deltaMNodeQueue.Enqueue(Path.Combine(reldirpath, dirname));
                }
            }
            return deltamtrees
                .Select(diffTriple => (diffTriple.dst, diffTriple.diffMTree))
                .ToList();
        }

        /// <summary>
        /// Checks the level at which this file is tracked based on its trackpattern.
        /// Defaults to 2 if no pattern matches.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="trackpatterns"></param>
        /// <returns></returns>
        public static int FileTrackClass(string file, List<(int trackclass, string pattern)> trackpatterns)
        {
            int trackclass = 2;
            foreach (var trackpatter in trackpatterns)
            {
                if (PatternMatchesPath(file, trackpatter.pattern))
                {
                    trackclass = trackpatter.trackclass;
                }
            }
            return trackclass;
        }

        /// <summary>
        /// Checks whether any directory child (files, sub directories, files in sub directories...) might possibly be tracked.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="trackpatterns"></param>
        /// <returns></returns>
        public static bool CheckTrackAnyDirectoryChild(string directory, List<(int trackpattern, string pattern)> trackpatterns)
        {
            bool track = true;
            foreach (var trackpattern in trackpatterns)
            {
                if (track)
                {
                    if (trackpattern.trackpattern == 0)
                    {
                        // Can only exclude if there is a trailing wildcard after 
                        // the rest of the pattern matches to this directory
                        if (trackpattern.pattern[^1] == '*')
                        {
                            if (trackpattern.pattern.Length == 1) // just "*"
                            {
                                track = false;
                            }
                            else if (PatternMatchesPath(directory, trackpattern.pattern))
                            {
                                track = false;
                            }
                        }
                    }
                }
                else
                {
                    if (trackpattern.trackpattern != 0)
                    {
                        int wildpos = trackpattern.pattern.IndexOf('*');
                        if (wildpos == 0)
                        {
                            track = true;
                        }
                        else if (wildpos > 0)
                        {
                            string prefix = trackpattern.pattern[..wildpos];
                            if (prefix.Length >= directory.Length)
                            {
                                if (prefix.StartsWith(directory))
                                {
                                    track = true;
                                }
                            }
                            else
                            {
                                if (directory.StartsWith(prefix))
                                {
                                    track = true;
                                }
                            }
                        }

                    }
                }
            }
            return track;
        }

        /// <summary>
        /// Matches a wildcard pattern to path
        /// </summary>
        /// <param name="path"></param>
        /// <param name="pattern"></param>
        /// <returns></returns>
        public static bool PatternMatchesPath(string path, string pattern)
        {
            if (pattern == "*")
            {
                return true;
            }
            if (pattern.EndsWith("/"))
            {
                pattern = pattern[0..^1];
            }
            int wildpos = pattern.IndexOf('*');
            if (wildpos >= 0)
            {
                string prefix = pattern[..wildpos];
                if (prefix.Length > 0)
                {
                    if (path.Length >= prefix.Length && prefix == path[..prefix.Length])
                    {
                        string wsuffix = pattern[wildpos..];
                        return PatternMatchesPath(path[prefix.Length..], wsuffix);
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    // Strip wildcard
                    pattern = pattern[1..];
                    while (path.Length > 0)
                    {
                        if (PatternMatchesPath(path, pattern))
                        {
                            return true;
                        }
                        path = path[1..];
                    }
                    return false;
                }
            }
            else
            {
                return path == pattern;
            }
        }
    }
}
