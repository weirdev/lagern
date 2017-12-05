using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BackupCore
{
    public class FSCoreSrcDependencies : ICoreSrcDependencies
    {
        /// <summary>
        /// The directory who's contents will be backed up.
        /// </summary>
        private string BackupPathSrc { get; set; }
        
        private string SrcSettingsFile { get; set; }

        private IFSInterop FSInterop { get; set; }

        public FSCoreSrcDependencies(string src, IFSInterop fsinterop)
        {
            FSInterop = fsinterop;
            BackupPathSrc = src;
            SrcSettingsFile = Path.Combine(SettingsDirectoryName, SettingsFilename);
        }

        public static FSCoreSrcDependencies Initialize(string bsname, string src, IFSInterop fsinterop, string dst = null, string cache = null)
        {
            var srcdep = new FSCoreSrcDependencies(src, fsinterop);
            srcdep.CreateDirectory(SettingsDirectoryName);

            srcdep.WriteSetting(BackupSetting.name, bsname);
            if (dst != null)
            {
                srcdep.WriteSetting(BackupSetting.dest, dst);
            }
            if (cache != null)
            {
                srcdep.WriteSetting(BackupSetting.cache, cache);
            }
            return srcdep;
        }
        
        public static readonly string SettingsDirectoryName = ".lagern";

        public static readonly string SettingsFilename = ".settings";

        public FileMetadata GetFileMetadata(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath.Substring(1);
            }
            return FSInterop.GetFileMetadata(Path.Combine(BackupPathSrc, relpath));
        }

        public IEnumerable<string> GetDirectoryFiles(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath.Substring(1);
            }
            return FSInterop.GetDirectoryFiles(Path.Combine(BackupPathSrc, relpath)).Select(filepath => Path.GetFileName(filepath));
        }

        public IEnumerable<string> GetSubDirectories(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath.Substring(1);
            }
            return FSInterop.GetSubDirectories(Path.Combine(BackupPathSrc, relpath)).Select(filepath => Path.GetFileName(filepath));
        }

        public Stream GetFileData(string relpath)
        {
            return FSInterop.GetFileData(Path.Combine(BackupPathSrc, relpath));
        }

        public void OverwriteOrCreateFile(string path, byte[] data, FileMetadata fileMetadata = null, bool absolutepath = false)
        {
            if (!absolutepath)
            {
                path = Path.Combine(BackupPathSrc, path);
            }
            try
            {
                FSInterop.OverwriteOrCreateFile(path, data, fileMetadata);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void CreateDirectory(string path, bool absolutepath = false)
        {
            if (!absolutepath)
            {
                path = Path.Combine(BackupPathSrc, path);
            }
            try
            {
                FSInterop.CreateDirectoryIfNotExists(path);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void WriteOutMetadata(string path, FileMetadata metadata, bool absolutepath = false)
        {
            if (!absolutepath)
            {
                path = Path.Combine(BackupPathSrc, path);
            }
            try
            {
                FSInterop.WriteOutMetadata(path, metadata);
            }
            catch (Exception)
            {

                throw;
            }
        }

        public string ReadSetting(BackupSetting key)
        {
            using (var fs = GetSettingsFileStream())
            {
                return SettingsFileTools.ReadSetting(fs, key);
            }
        }

        public Dictionary<BackupSetting, string> ReadSettings()
        {
            using (var fs = GetSettingsFileStream())
            {
                return SettingsFileTools.ReadSettings(fs);
            }
        }

        public void WriteSetting(BackupSetting key, string value)
        {
            using (var fs = GetSettingsFileStream())
            {
                WriteSettingsFileStream(SettingsFileTools.WriteSetting(fs, key, value));
            }
        }

        public void ClearSetting(BackupSetting key)
        {
            using (var fs = GetSettingsFileStream())
            {
                SettingsFileTools.ClearSetting(fs, key);
            }
        }

        private Stream GetSettingsFileStream()
        {
            try
            {
                return GetFileData(SrcSettingsFile);
            }
            catch (Exception)
            {
                return null;
            } 
        }

        private void WriteSettingsFileStream(byte[] data) => OverwriteOrCreateFile(SrcSettingsFile, data);
    }
}
