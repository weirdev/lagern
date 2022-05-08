using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BackupCore
{
    public class FSCoreSrcDependencies : ICoreSrcDependencies
    {
        public static readonly string SettingsDirectoryName = ".lagern";

        public static readonly string SettingsFilename = ".settings";

        public static readonly string AesKeyFilename = ".keyfile";

        /// <summary>
        /// The directory who's contents will be backed up.
        /// </summary>
        private string? BackupPathSrc { get; set; }
        
        private string SrcSettingsFile { get; set; }

        private string? AesKeyFile { get; set; }

        private IFSInterop FSInterop { get; set; }

        private AesHelper? AesTool { get; set; }

        private FSCoreSrcDependencies(string? src, IFSInterop fsinterop)
        {
            FSInterop = fsinterop;
            BackupPathSrc = src;
            SrcSettingsFile = Path.Combine(SettingsDirectoryName, SettingsFilename);
        }

        public static FSCoreSrcDependencies Load(string? src, IFSInterop fsinterop)
        {
            return new FSCoreSrcDependencies(src, fsinterop);
        }

        public static FSCoreSrcDependencies InitializeNew(string bsname, string? src, IFSInterop fsinterop, string? cache = null)
        {
            var srcdep = new FSCoreSrcDependencies(src, fsinterop);
            srcdep.CreateDirectory(SettingsDirectoryName);

            srcdep.WriteSetting(BackupSetting.name, bsname);
            
            if (cache != null)
            {
                srcdep.WriteSetting(BackupSetting.cache, cache);
            }
           
            return srcdep;
        }

        public FileMetadata GetFileMetadata(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath[1..];
            }
            if (BackupPathSrc == null)
            {
                throw new Exception("Must be configured with source path for this operation");
            }
            return FSInterop.GetFileMetadata(Path.Combine(BackupPathSrc, relpath)).Result;
        }

        public IEnumerable<string> GetDirectoryFiles(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath[1..];
            }
            if (BackupPathSrc == null)
            {
                throw new Exception("Must be configured with source path for this operation");
            }
            return FSInterop.GetDirectoryFiles(Path.Combine(BackupPathSrc, relpath)).Result
                .Select(filepath => Path.GetFileName(filepath));
        }

        public IEnumerable<string> GetSubDirectories(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath[1..];
            }
            if (BackupPathSrc == null)
            {
                throw new Exception("Must be configured with source path for this operation");
            }
            return FSInterop.GetSubDirectories(Path.Combine(BackupPathSrc, relpath)).Result
                .Select(filepath => Path.GetFileName(filepath));
        }

        public Stream GetFileData(string relpath)
        {
            if (AesTool != null)
            {
                return AesTool.GetEncryptedStream(GetRawFileData(relpath));
            }
            return GetRawFileData(relpath);
        }

        private Stream GetRawFileData(string relpath)
        {
            if (BackupPathSrc == null)
            {
                throw new Exception("Must be configured with source path for this operation");
            }
            return FSInterop.GetFileData(Path.Combine(BackupPathSrc, relpath)).Result;
        }

        public void OverwriteOrCreateFile(string path, byte[] data, FileMetadata? fileMetadata = null, bool absolutepath = false)
        {
            if (!absolutepath)
            {
                if (BackupPathSrc == null)
                {
                    throw new Exception("Must be configured with source path for this operation");
                }
                path = Path.Combine(BackupPathSrc, path);
            }
            try
            {
                FSInterop.OverwriteOrCreateFile(path, data, fileMetadata).Wait();
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void DeleteFile(string path, bool absolutepath = false)
        {
            if (!absolutepath)
            {
                if (BackupPathSrc == null)
                {
                    throw new Exception("Must be configured with source path for this operation");
                }
                path = Path.Combine(BackupPathSrc, path);
            }
            try
            {
                FSInterop.DeleteFile(path).Wait();
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
                if (BackupPathSrc == null)
                {
                    throw new Exception("Must be configured with source path for this operation");
                }
                path = Path.Combine(BackupPathSrc, path);
            }
            try
            {
                FSInterop.CreateDirectoryIfNotExists(path).Wait();
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
                if (BackupPathSrc == null)
                {
                    throw new Exception("Must be configured with source path for this operation");
                }
                path = Path.Combine(BackupPathSrc, path);
            }
            try
            {
                FSInterop.WriteOutMetadata(path, metadata).Wait();
            }
            catch (Exception)
            {

                throw;
            }
        }

        public string ReadSetting(BackupSetting key)
        {
            using var fs = GetSettingsFileStream();
            return SettingsFileTools.ReadSetting(fs, key);
        }

        public Dictionary<BackupSetting, string> ReadSettings()
        {
            using var fs = GetSettingsFileStream();
            return SettingsFileTools.ReadSettings(fs);
        }

        public void WriteSetting(BackupSetting key, string value)
        {
            try
            {
                using Stream fs = GetSettingsFileStream();
                WriteSettingsFileStream(SettingsFileTools.WriteSetting(fs, key, value));
            }
            catch (Exception)
            {
                WriteSettingsFileStream(SettingsFileTools.WriteSetting(null, key, value));
            }
        }

        public void ClearSetting(BackupSetting key)
        {
            using var fs = GetSettingsFileStream();
            SettingsFileTools.ClearSetting(fs, key);
        }

        private Stream GetSettingsFileStream()
        {
            try
            {
                return GetRawFileData(SrcSettingsFile);
            }
            catch (Exception e)
            {
                throw new Exception("Failed to load settings file", e);
            } 
        }

        private void WriteSettingsFileStream(byte[] data) => OverwriteOrCreateFile(SrcSettingsFile, data);
        
        private void ReadAesKeyFile(string password)
        {
            if (AesKeyFile != null)
            {
                try
                {
                    using MemoryStream ms = new();
                    GetRawFileData(AesKeyFile).CopyTo(ms);
                    AesTool = AesHelper.CreateFromKeyFile(ms.ToArray(), password);
                }
                // TODO: Special handling for wrong passwords, no keyfile present, etc.
                catch (Exception)
                {
                    AesTool = null;
                }
            } 
            else
            {
                throw new Exception("No AES keyfile path specified");
            }
        }
    }
}
