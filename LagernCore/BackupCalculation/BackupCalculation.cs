using BackupCore;
using LagernCore.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        /// <param name="previousmtrees"></param>
        /// <returns>A delta tree mapping </returns>
        public static List<MetadataNode> GetDeltaMetadataTree(
            Core core, string backupsetname, List<(int trackclass, string pattern)>? trackpatterns = null,
            List<MetadataNode?>? previousmtrees = null)
        {
            BackupSetReference backupSetReference = new(backupsetname, false, false, false);
            if (!core.DestinationAvailable)
            {
                backupSetReference = backupSetReference with { Cache = true };
            }

            Queue<string> deltamnodequeue = new();
            FileMetadata rootdirmetadata = core.SrcDependencies.GetFileMetadata("");

            // Non differential backup equivalent to differential backup to single destination without a previous tree
            if (previousmtrees == null)
            {
                previousmtrees = new List<MetadataNode?>() { null };
            }

            List<MetadataNode> deltamtrees = previousmtrees.Select((_) => new MetadataNode(rootdirmetadata, null)).ToList();

            foreach (var (previousmtree, deltamtree) in previousmtrees.Zip(deltamtrees, (p, d) => (p, d)))
            {
                if (previousmtree != null)
                {
                    // We always assume the matching of deltatree root to fs backup root is valid
                    // So make the name equal, and set status to metadatachange if they were different
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

            deltamnodequeue.Enqueue(Path.DirectorySeparatorChar.ToString());

            while (deltamnodequeue.Count > 0)
            {
                string reldirpath = deltamnodequeue.Dequeue();
                List<MetadataNode?> posdeltanodes = deltamtrees.Select((dmt) => dmt.GetDirectory(reldirpath)).ToList();
                List<MetadataNode?> previousmnodes = previousmtrees.Select((mt) => mt?.GetDirectory(reldirpath)).ToList();

                // Null delta nodes indicate that a directory is not to be backed up for that backup,
                // so we exclude the deltanode and corresponding previousmnode
                List<MetadataNode> filtereddn = new();
                List<MetadataNode?> filteredpn = new();
                for (int i = 0; i < posdeltanodes.Count; i++)
                {
                    MetadataNode? deltaNode = posdeltanodes[i];
                    if (deltaNode != null)
                    {
                        filtereddn.Add(deltaNode);
                        filteredpn.Add(previousmnodes[i]);
                    }
                }
                List<MetadataNode> deltanodes = filtereddn;
                previousmnodes = filteredpn;

                // Now handle files
                List<string> fsfiles;
                try
                {
                    fsfiles = new List<string>(core.SrcDependencies.GetDirectoryFiles(reldirpath));
                    fsfiles.Sort();
                }
                catch (Exception e) when (e is DirectoryNotFoundException || e is UnauthorizedAccessException) // TODO: More user friendly output here
                {
                    throw new Exception("Fetching file system files failed", e);
                }
                catch (Exception e)
                {
                    throw new Exception("Fetching file system files failed", e);
                }

                // Used this slightly ackward cache pattern to more easily efficiently handle per-destination tracking classes in a future release
                Dictionary<string, FileMetadata> filemetadatacache = new();

                for (int prevmnidx = 0; prevmnidx < previousmnodes.Count; prevmnidx++)
                {
                    var previousmnode = previousmnodes[prevmnidx];
                    var deltamnode = deltanodes[prevmnidx];
                    List<string> previousfiles;


                    int previdx = 0;
                    int fsidx = 0;
                    if (previousmnode != null)
                    {
                        previousfiles = new List<string>(previousmnode.Files.Keys);
                        previousfiles.Sort();

                        while (previdx < previousfiles.Count && fsidx < fsfiles.Count)
                        {
                            if (previousfiles[previdx] == fsfiles[fsidx]) // Names match
                            {
                                string filename = previousfiles[previdx];
                                int trackclass = 2; // TODO: make this an application wide constant
                                if (trackpatterns != null)
                                {
                                    trackclass = FileTrackClass(Path.Combine(reldirpath[1..], filename), trackpatterns);
                                }
                                try // We (may) read the file's metadata here so wrap errors
                                {
                                    if (trackclass != 0)
                                    {
                                        FileMetadata prevfm = previousmnode.Files[filename];
                                        FileMetadata curfm;
                                        if (filemetadatacache.ContainsKey(filename))
                                        {
                                            curfm = filemetadatacache[filename];
                                        }
                                        else
                                        {
                                            curfm = core.SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, filename));
                                            filemetadatacache[filename] = curfm;
                                        }
                                        // Create a copy FileMetada to hold the changes
                                        curfm = new FileMetadata(curfm);

                                        switch (trackclass)
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
                                                break;
                                            case 2: // Dont scan if we have a previous version and its metadata indicates no change
                                                    // If file size and datemodified unchanged we assume no change
                                                if (prevfm.FileSize == curfm.FileSize && prevfm.DateModifiedUTC == curfm.DateModifiedUTC)
                                                {
                                                    // Still update metadata if necessary (ie dateaccessed changed)
                                                    if (curfm.FileDifference(prevfm))
                                                    {
                                                        curfm.Status = FileMetadata.FileStatus.MetadataChange;
                                                    }
                                                    else
                                                    {
                                                        curfm.Status = FileMetadata.FileStatus.Unchanged;
                                                    }
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
                                        FileMetadata prevfm = previousmnode.Files[filename];
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
                                previdx++;
                                fsidx++;
                            }
                            else if (previousfiles[previdx].CompareTo(fsfiles[fsidx]) < 0) // deltafiles[deltaidx] earlier in alphabet than fsfiles[fsidx]
                            {
                                // File in old tree but no longer in filesystem
                                string filename = previousfiles[previdx];
                                FileMetadata prevfm = previousmnode.Files[filename];
                                prevfm = new FileMetadata(prevfm)
                                {
                                    Status = FileMetadata.FileStatus.Deleted
                                };
                                deltamnode.AddFile(prevfm);
                                previdx++;
                            }
                            else // deltafiles[deltaidx] later in alphabet than fsfiles[fsidx]
                            {
                                // File on filesystem not in old tree
                                string filename = fsfiles[fsidx];
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
                                            if (filemetadatacache.ContainsKey(filename))
                                            {
                                                curfm = filemetadatacache[filename];
                                            }
                                            else
                                            {
                                                curfm = core.SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, filename));
                                                filemetadatacache[filename] = curfm;
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
                                    Console.WriteLine(e.Message);
                                }
                                fsidx++;
                            }
                        }
                        for (; previdx < previousfiles.Count; previdx++)
                        {
                            // File in old tree but no longer in filesystem
                            string filename = previousfiles[previdx];
                            FileMetadata prevfm = previousmnode.Files[filename];
                            prevfm = new FileMetadata(prevfm)
                            {
                                Status = FileMetadata.FileStatus.Deleted
                            };
                            deltamnode.AddFile(prevfm);
                        }
                    }
                    else
                    {
                        previousfiles = new List<string>(0);
                    }

                    for (; fsidx < fsfiles.Count; fsidx++)
                    {
                        // File on filesystem not in old tree
                        string filename = fsfiles[fsidx];
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
                                    if (filemetadatacache.ContainsKey(filename))
                                    {
                                        curfm = filemetadatacache[filename];
                                    }
                                    else
                                    {
                                        curfm = core.SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, filename));
                                        filemetadatacache[filename] = curfm;
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
                            Console.WriteLine(e.Message);
                        }
                    }
                }


                // Handle directories
                List<string> fssubdirs;
                try
                {
                    // Use GetFileName because GetDirectories doesnt return trailing backslashes, so GetDirectoryName will return the partent directory
                    fssubdirs = new List<string>(core.SrcDependencies.GetSubDirectories(reldirpath));
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

                for (int prevmnidx = 0; prevmnidx < previousmnodes.Count; prevmnidx++)
                {
                    MetadataNode? previousmnode = previousmnodes[prevmnidx];
                    var deltamnode = deltanodes[prevmnidx];

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
                                        fssubdirmetadata = core.SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
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
                                        fssubdirmetadata = core.SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
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
                                fssubdirmetadata = core.SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
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
                    deltamnodequeue.Enqueue(Path.Combine(reldirpath, dirname));
                }
            }
            return deltamtrees;
        }

        /// <summary>
        /// Checks whether this file is tracked based on trackpattern
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
                            string prefix = trackpattern.pattern.Substring(0, wildpos);
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
                string prefix = pattern.Substring(0, wildpos);
                if (prefix.Length > 0)
                {
                    if (path.Length >= prefix.Length && prefix == path.Substring(0, prefix.Length))
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
