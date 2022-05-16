using System.Collections.Generic;

namespace LagernCore.Models
{
    public class SettingsFileModel
    {
        public string? BackupName { get; set; }

        public List<BackupDestinationSpecification>? Destinations { get; set; }

        public BackupDestinationSpecification? Cache { get; set; }

        public class BackupDestinationSpecification
        {
            public string? Name { get; set; }

            public DestinationType? Type { get; set; }

            public bool UsePassword { get; set; }

            public string? Path { get; set; }

            public string? CloudConfig { get; set; }
        }

        public enum DestinationType
        {
            Filesystem,
            Backblaze
        }
    }
}
