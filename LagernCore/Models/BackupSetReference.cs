namespace LagernCore.Models
{
    public record BackupSetReference(string BackupSetName, bool Shallow, bool Cache, bool BlobListCache)
    {
        private static readonly string ShallowSuffix = "~shallow";
        private static readonly string CacheSuffix = "~cache";
        private static readonly string BlobListCacheSuffix = "~blobListCache";

        public string StringRepr()
        {
            string repr = BackupSetName;
            if (Shallow)
            {
                repr += ShallowSuffix;
            }
            if (Cache)
            {
                repr += CacheSuffix;
            }
            if (BlobListCache)
            {
                repr += BlobListCacheSuffix;
            }

            return repr;
        }
    }
}
