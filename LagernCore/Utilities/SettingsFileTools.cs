using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BackupCore
{
    // TODO: Why are we taking in streams and returning byte arrays
    // Should likely standardize on byte arrays since settings files
    // will be quite small
    public static class SettingsFileTools
    {
        public static async Task<string> ReadSetting(Stream settingsfile, BackupSetting key)
        {
            var settings = await ReadSettings(settingsfile);
            if (settings != null)
            {
                if (settings.ContainsKey(key))
                {
                    return settings[key];
                }
            }
            throw new KeyNotFoundException();
        }

        public static async Task<byte[]> WriteSetting(Stream? settingsfile, BackupSetting key, string value)
        {
            Dictionary<BackupSetting, string> settings;
            if (settingsfile != null)
            {
                try
                {
                    settings = await ReadSettings(settingsfile);
                }
                catch
                {
                    settings = new Dictionary<BackupSetting, string>();
                }
            }
            else
            {
                settings = new Dictionary<BackupSetting, string>();
            }
            settings[key] = value;
            return await WriteSettings(settings);
        }

        public static async Task<byte[]> ClearSetting(Stream settingsfile, BackupSetting key)
        {
            Dictionary<BackupSetting, string> settings;
            try
            {
                settings = await ReadSettings(settingsfile);
            }
            catch
            {
                settings = new Dictionary<BackupSetting, string>();
            }
            if (settings != null)
            {
                if (settings.ContainsKey(key))
                {
                    settings.Remove(key);
                    return await WriteSettings(settings);
                }
                MemoryStream ms = new();
                settingsfile.CopyTo(ms);
                return ms.ToArray();
            }
            else
            {
                throw new Exception();
            }
        }

        public static async Task<Dictionary<BackupSetting, string>> ReadSettings(Stream settingsfile)
        {
            Dictionary<BackupSetting, string> settings = new();
            using (StreamReader reader = new(settingsfile))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    string[] kv = line.Split(' ');
                    if (Enum.TryParse(kv[0], out BackupSetting key))
                    {
                        settings[key] = kv[1];
                    }
                }
            }
            return settings;
        }

        public static async Task<byte[]> WriteSettings(Dictionary<BackupSetting, string> settings)
        {
            MemoryStream ms = new();
            using (StreamWriter writer = new(ms))
            {
                foreach (var kv in settings)
                {
                    await writer.WriteLineAsync(kv.Key.ToString() + " " + kv.Value);
                }
            }
            return ms.ToArray();
        }
    }
}
