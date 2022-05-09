using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

        public static async Task<FSCoreSrcDependencies> InitializeNew(string bsname, string? src, IFSInterop fsinterop, string? cache = null)
        {
            var srcdep = new FSCoreSrcDependencies(src, fsinterop);
            await srcdep.CreateDirectory(SettingsDirectoryName);

            await srcdep.WriteSetting(BackupSetting.name, bsname);
            
            if (cache != null)
            {
                await srcdep.WriteSetting(BackupSetting.cache, cache);
            }
           
            return srcdep;
        }

        public async Task<FileMetadata> GetFileMetadata(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath[1..];
            }
            if (BackupPathSrc == null)
            {
                throw new Exception("Must be configured with source path for this operation");
            }
            return await FSInterop.GetFileMetadata(Path.Combine(BackupPathSrc, relpath));
        }

        public async Task<IEnumerable<string>> GetDirectoryFiles(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath[1..];
            }
            if (BackupPathSrc == null)
            {
                throw new Exception("Must be configured with source path for this operation");
            }
            return (await FSInterop.GetDirectoryFiles(Path.Combine(BackupPathSrc, relpath)))
                .Select(filepath => Path.GetFileName(filepath));
        }

        public async Task<IEnumerable<string>> GetSubDirectories(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath[1..];
            }
            if (BackupPathSrc == null)
            {
                throw new Exception("Must be configured with source path for this operation");
            }
            return (await FSInterop.GetSubDirectories(Path.Combine(BackupPathSrc, relpath)))
                .Select(filepath => Path.GetFileName(filepath));
        }

        public async Task<Stream> GetFileData(string relpath)
        {
            if (AesTool != null)
            {
                return AesTool.GetEncryptedStream(await GetRawFileData(relpath));
            }
            return await GetRawFileData(relpath);
        }

        private async Task<Stream> GetRawFileData(string relpath)
        {
            if (BackupPathSrc == null)
            {
                throw new Exception("Must be configured with source path for this operation");
            }
            return await FSInterop.GetFileData(Path.Combine(BackupPathSrc, relpath));
        }

        public async Task OverwriteOrCreateFile(string path, byte[] data, FileMetadata? fileMetadata = null, bool absolutepath = false)
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
                await FSInterop.OverwriteOrCreateFile(path, data, fileMetadata);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task DeleteFile(string path, bool absolutepath = false)
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
                await FSInterop.DeleteFile(path);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task CreateDirectory(string path, bool absolutepath = false)
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
                await FSInterop.CreateDirectoryIfNotExists(path);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task WriteOutMetadata(string path, FileMetadata metadata, bool absolutepath = false)
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
                await FSInterop.WriteOutMetadata(path, metadata);
            }
            catch (Exception)
            {

                throw;
            }
        }

        public async Task<string> ReadSetting(BackupSetting key)
        {
            using var fs = await GetSettingsFileStream();
            return await SettingsFileTools.ReadSetting(fs, key);
        }

        public async Task<Dictionary<BackupSetting, string>> ReadSettings()
        {
            using var fs = await GetSettingsFileStream();
            return await SettingsFileTools.ReadSettings(fs);
        }

        public async Task WriteSetting(BackupSetting key, string value)
        {
            try
            {
                using Stream fs = await GetSettingsFileStream();
                await WriteSettingsFileStream(await SettingsFileTools.WriteSetting(fs, key, value));
            }
            catch (Exception)
            {
                await WriteSettingsFileStream(await SettingsFileTools.WriteSetting(null, key, value));
            }
        }

        public async Task ClearSetting(BackupSetting key)
        {
            using var fs = await GetSettingsFileStream();
            await SettingsFileTools.ClearSetting(fs, key);
        }

        private async Task<Stream> GetSettingsFileStream()
        {
            try
            {
                return await GetRawFileData(SrcSettingsFile);
            }
            catch (Exception e)
            {
                throw new Exception("Failed to load settings file", e);
            } 
        }

        private async Task WriteSettingsFileStream(byte[] data) => await OverwriteOrCreateFile(SrcSettingsFile, data);
        
        private async Task ReadAesKeyFile(string password)
        {
            if (AesKeyFile != null)
            {
                try
                {
                    using MemoryStream ms = new();
                    (await GetRawFileData(AesKeyFile)).CopyTo(ms);
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
