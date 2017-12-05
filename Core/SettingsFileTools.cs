using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BackupCore
{
    public static class SettingsFileTools
    {
        public static string ReadSetting(Stream settingsfile, BackupSetting key)
        {
            var settings = ReadSettings(settingsfile);
            if (settings != null)
            {
                if (settings.ContainsKey(key))
                {
                    return settings[key];
                }
            }
            throw new KeyNotFoundException();
        }

        public static byte[] WriteSetting(Stream settingsfile, BackupSetting key, string value)
        {
            Dictionary<BackupSetting, string> settings;
            try
            {
                settings = ReadSettings(settingsfile);
            }
            catch
            {
                settings = new Dictionary<BackupSetting, string>();
            }
            settings[key] = value;
            return WriteSettings(settings);
        }

        public static void ClearSetting(Stream settingsfile, BackupSetting key)
        {
            Dictionary<BackupSetting, string> settings;
            try
            {
                settings = ReadSettings(settingsfile);
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
                    WriteSettings(settings);
                }
            }
            else
            {
                throw new Exception();
            }
        }

        public static Dictionary<BackupSetting, string> ReadSettings(Stream settingsfile)
        {
            Dictionary<BackupSetting, string> settings = new Dictionary<BackupSetting, string>();
            using (StreamReader reader = new StreamReader(settingsfile))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
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

        public static byte[] WriteSettings(Dictionary<BackupSetting, string> settings)
        {
            MemoryStream ms = new MemoryStream();
            using (StreamWriter writer = new StreamWriter(ms))
            {
                foreach (var kv in settings)
                {
                    writer.WriteLine(kv.Key.ToString() + " " + kv.Value);
                }
            }
            return ms.ToArray();
        }
    }
}
