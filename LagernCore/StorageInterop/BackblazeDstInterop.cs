using System;
using System.Text;
using Flurl;
using Flurl.Http;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;

namespace BackupCore
{
    public class BackblazeDstInterop : IDstFSInterop
    {
        private string? ConnectionSettingsFile { get; set; }

        public AesHelper? Encryptor { get; private set; }

        private string AccountID { get; set; }
        private string ApplicationKey { get; set; }
        private string BucketId { get; set; }
        private string BucketName { get; set; }
        
        private AuthorizationResponse? AuthResp;
        private GetUploadUrlResponse? UploadUrlResp = null;

        private static readonly int MinDelayMS = 32;
        private int DelayMS { get; set; } = 32;
        private static readonly int MaxDelayMS = 16_000;

        private static readonly int Retries = 7;

        public static string BlobSaveDirectory
        {
            get => "blobdata";
        }

        public static string IndexDirectory
        {
            get => "index";
        }

        private BackblazeDstInterop(string accountid, string applicationkey,
            string bucketid, string bucketname, AuthorizationResponse authorizationResponse,
            string? connectionsettingsfile=null)
        {
            AccountID = accountid;
            ApplicationKey = applicationkey;
            BucketId = bucketid;
            BucketName = bucketname;
            ConnectionSettingsFile = connectionsettingsfile;
            AuthResp = authorizationResponse;
        }

        public static async Task<IDstFSInterop> InitializeNew(string connectionsettingsfile, string? password = null)
        {
            BBConnectionSettings connectionsettings = await LoadBBConnectionSettings(connectionsettingsfile);
            return await InitializeNew(connectionsettings.accountId, connectionsettings.ApplicationKey,
                connectionsettings.bucketId, connectionsettings.bucketName, connectionsettingsfile, password);
        }

        public static async Task<IDstFSInterop> InitializeNew(string accountid, 
            string applicationkey, string bucketid, string bucketname,
            string? connectionSettingsFile=null, string? password = null)
        {
            var authResp = await AuthorizeAccount(accountid, applicationkey);
            BackblazeDstInterop backblazeDstInterop = new(accountid, applicationkey, bucketid, 
                bucketname, authResp, connectionSettingsFile);
            if (password != null)
            {
                AesHelper encryptor = AesHelper.CreateFromPassword(password);
                byte[] keyfile = encryptor.CreateKeyFile();
                await backblazeDstInterop.StoreIndexFileAsync(null, IndexFileType.EncryptorKeyFile, keyfile);
                backblazeDstInterop.Encryptor = encryptor;
            }
            return backblazeDstInterop;
        }

        public static async Task<IDstFSInterop> Load(string connectionsettingsfile, string? password=null)
        {
            BBConnectionSettings connectionsettings = await LoadBBConnectionSettings(connectionsettingsfile);
            return await Load(connectionsettings.accountId, connectionsettings.ApplicationKey,
                connectionsettings.bucketId, connectionsettings.bucketName, connectionsettingsfile, password);
        }

        public static async Task<IDstFSInterop> Load(string accountid, string applicationkey, 
            string bucketid, string bucketname, string? connectionSettingsFile=null,
            string? password = null)
        {
            var authResp = await AuthorizeAccount(accountid, applicationkey);
            BackblazeDstInterop backblazeDstInterop = new(accountid, applicationkey, bucketid, bucketname,
                authResp, connectionSettingsFile);
            if (password != null)
            {
                byte[] keyfile = await backblazeDstInterop.LoadIndexFileAsync(null, IndexFileType.EncryptorKeyFile);
                AesHelper encryptor = AesHelper.CreateFromKeyFile(keyfile, password);
                backblazeDstInterop.Encryptor = encryptor;
            }
            return backblazeDstInterop;
        }

        /// <summary>
        /// Pauses thread
        /// </summary>
        private void Delay()
        {
            Thread.Sleep(DelayMS);
        }

        private void SuccessfulTransmission()
        {
            DelayMS = Max(DelayMS - 500, MinDelayMS);
        }

        private void FailedTransmission()
        {
            DelayMS = Min(DelayMS * 2, MaxDelayMS);
        }

        private static async Task<AuthorizationResponse> AuthorizeAccount(string accountid, string applicationkey, int attempts = 0)
        {
            Thread.Sleep(MinDelayMS);
            try
            {
                var authresp = await "https://api.backblazeb2.com/b2api/v1/b2_authorize_account"
                .WithHeaders(new
                {
                    Authorization = "Basic "
                        + Convert.ToBase64String(Encoding.UTF8.GetBytes(accountid
                            + ":" + applicationkey))
                })
                .GetJsonAsync<AuthorizationResponse>().ConfigureAwait(false);
                return authresp;
            }
            catch (FlurlHttpException)
            {
                if (attempts < Retries)
                {
                    return await AuthorizeAccount(accountid, applicationkey, attempts + 1).ConfigureAwait(false);
                }
                throw;
            }
        }

        private async Task<AuthorizationResponse> AuthorizeAccount(int attempts = 0)
        {
            return await AuthorizeAccount(AccountID, ApplicationKey, attempts);
        }

        private Task StoreFileAsync(string file, byte[] data, bool preventEncryption=false) => 
            StoreFileAsync(file, HashTools.GetSHA1Hasher().ComputeHash(data), data, preventEncryption);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <param name="hash"></param>
        /// <param name="data"></param>
        /// <returns>fileId</returns>
        private async Task<(byte[] encryptedHash, string fileId)> StoreFileAsync(string file, byte[] hash, byte[] data, bool preventEncryption=false)
        {
            async Task<GetUploadUrlResponse> GetUploadUrl(int attempts=0)
            {
                if (AuthResp == null)
                {
                    AuthResp = await AuthorizeAccount();
                }
                Delay();
                try
                {
                    var urlresp = await AuthResp.Value.apiUrl
                        .AppendPathSegment("/b2api/v1/b2_get_upload_url")
                        .WithHeaders(new { Authorization = AuthResp.Value.authorizationToken })
                        .PostJsonAsync(new { bucketId = BucketId })
                        .ReceiveJson<GetUploadUrlResponse>().ConfigureAwait(false);
                    SuccessfulTransmission();
                    return urlresp;
                }
                catch (FlurlHttpException ex)
                {
                    if (ex.Call.HttpStatus != null && ex.Call.HttpStatus == System.Net.HttpStatusCode.Unauthorized)
                    {
                        AuthResp = null;
                    }
                    else
                    {
                        // Other classes of errors may be congestion related so we increase the delay
                        FailedTransmission();
                    }
                    if (attempts < Retries)
                    {
                        return await GetUploadUrl(attempts + 1).ConfigureAwait(false);
                    }
                    throw;
                }
            }

            var hashFileId = await UploadData();
            async Task<(byte[] encryptedHash, string fileId)> UploadData(int attempts = 0)
            {
                if (UploadUrlResp == null)
                {
                    UploadUrlResp = await GetUploadUrl();
                }
                Delay();
                try
                {
                    if (Encryptor != null && !preventEncryption)
                    {
                        data = Encryptor.EncryptBytes(data);
                        hash = HashTools.GetSHA1Hasher().ComputeHash(data);
                    }

                    var filecontent = new ByteArrayContent(data);
                    filecontent.Headers.Add("Content-Type", "application/octet-stream");
                    var uploadresp = await UploadUrlResp.Value.uploadUrl
                        .WithHeaders(new
                        {
                            Authorization = UploadUrlResp.Value.authorizationToken,
                            X_Bz_File_Name = file,
                            Content_Length = data.Length,
                            X_Bz_Content_Sha1 = HashTools.ByteArrayToHexViaLookup32(hash)
                        })
                        .PostAsync(filecontent)
                        .ReceiveJson<UploadResponse>().ConfigureAwait(false);
                    SuccessfulTransmission();
                    return (hash, uploadresp.fileId);
                }
                catch (FlurlHttpException ex)
                {
                    if (ex.Call.HttpStatus != null && ex.Call.HttpStatus == System.Net.HttpStatusCode.Unauthorized)
                    {
                        UploadUrlResp = null;
                    }
                    else
                    {
                        // Other classes of errors may be congestion related so we increase the delay
                        FailedTransmission();
                    }

                    if (attempts < Retries)
                    {
                        return await UploadData(attempts + 1).ConfigureAwait(false);
                    }
                    throw;
                }
            }
            return hashFileId;
        }

        private async Task<byte[]> LoadFileAsync(string fileName, bool decrypt=true)
        {
            byte[] downloaddata = await Download();
            async Task<byte[]> Download(int attempts = 0)
            {
                if (AuthResp == null)
                {
                    AuthResp = await AuthorizeAccount();
                }
                Delay();
                try
                {
                    HttpResponseMessage downloadresp = await AuthResp.Value.downloadUrl
                        .AppendPathSegment("file")
                        .AppendPathSegment(BucketName)
                        .AppendPathSegment(fileName)
                        .WithHeaders(new { Authorization = AuthResp.Value.authorizationToken })
                        .GetAsync().ConfigureAwait(false);
                    var data = await downloadresp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                    SuccessfulTransmission();

                    if (Encryptor != null && decrypt)
                    {
                        data = Encryptor.DecryptBytes(data);
                    }

                    return data;
                }
                catch (FlurlHttpException ex)
                {
                    if (ex.Call.HttpStatus != null && ex.Call.HttpStatus == System.Net.HttpStatusCode.Unauthorized)
                    {
                        AuthResp = null;
                    }
                    else
                    {
                        // Other classes of errors may be congestion related so we increase the delay
                        FailedTransmission();
                    }
                    if (attempts < Retries)
                    {
                        return await Download(attempts + 1).ConfigureAwait(false);
                    }
                    throw;
                }
            }
            return downloaddata;
        }

        private Task DeleteFileAsync(string filename, string fileid)
        {
            return Delete();
            async Task<HttpResponseMessage> Delete(int attempts = 0)
            {
                if (AuthResp == null)
                {
                    AuthResp = await AuthorizeAccount();
                }
                Delay();
                try
                {
                    var deleteresp = await AuthResp.Value.apiUrl
                        .AppendPathSegment("/b2api/v1/b2_delete_file_version")
                        .WithHeaders(new { Authorization = AuthResp.Value.authorizationToken })
                        .PostJsonAsync(new
                        {
                            fileId = fileid,
                            fileName = filename
                        }).ConfigureAwait(false);
                    SuccessfulTransmission();
                    return deleteresp;
                }
                catch (FlurlHttpException ex)
                {
                    if (ex.Call.HttpStatus != null && ex.Call.HttpStatus == System.Net.HttpStatusCode.Unauthorized)
                    {
                        AuthResp = null;
                    }
                    else
                    {
                        // Other classes of errors may be congestion related so we increase the delay
                        FailedTransmission();
                    }
                    if (attempts < Retries)
                    {
                        return await Delete(attempts + 1).ConfigureAwait(false);
                    }
                    throw;
                }
            }
        }

        private async Task<bool> FileExistsAsync(string file)
        {
            bool exists = await Exists();
            async Task<bool> Exists(int attempts = 0)
            {
                if (AuthResp == null)
                {
                    AuthResp = await AuthorizeAccount();
                }
                Delay();
                try
                {
                    var filesresp = await AuthResp.Value.apiUrl
                        .AppendPathSegment("/b2api/v1/b2_list_file_names")
                        .WithHeaders(new { Authorization = AuthResp.Value.authorizationToken })
                        .PostJsonAsync(new
                        {
                            bucketId = BucketId,
                            startFileName = file,
                            maxFileCount = 1
                        })
                        .ReceiveJson<GetFilesResponse>().ConfigureAwait(false);
                    SuccessfulTransmission();
                    return filesresp.files.Length > 0 && filesresp.files[0].fileName == file;
                }
                catch (FlurlHttpException ex)
                {
                    if (ex.Call.HttpStatus != null && ex.Call.HttpStatus == System.Net.HttpStatusCode.Unauthorized)
                    {
                        AuthResp = null;
                    }
                    else
                    {
                        // Other classes of errors may be congestion related so we increase the delay
                        FailedTransmission();
                    }
                    if (attempts < Retries)
                    {
                        return await Exists(attempts + 1).ConfigureAwait(false);
                    }
                    throw;
                }
            }
            return exists;
        }

        private static async Task<BBConnectionSettings> LoadBBConnectionSettings(string connectionsettingsfile)
        {
            string connectionsettings;
            using (var sr = new StreamReader(connectionsettingsfile))
            {
                connectionsettings = await sr.ReadToEndAsync();
            }
            return JsonConvert.DeserializeObject<BBConnectionSettings>(connectionsettings);
        }

        private static int Min(params int[] nums)
        {
            int min = nums[0];
            for (int i = 1; i < nums.Length; i++)
            {
                if (nums[i] < min)
                {
                    min = nums[i];
                }
            }
            return min;
        }
                
        private static int Max(params int[] nums)
        {
            int max = nums[0];
            for (int i = 1; i < nums.Length; i++)
            {
                if (nums[i] > max)
                {
                    max = nums[i];
                }
            }
            return max;
        }

        public Task<bool> IndexFileExistsAsync(string? bsname, IndexFileType fileType)
        {
            return FileExistsAsync(GetIndexFilePath(bsname, fileType));
        }

        public Task<byte[]> LoadIndexFileAsync(string? bsname, IndexFileType fileType)
        {
            return LoadFileAsync(GetIndexFilePath(bsname, fileType), fileType != IndexFileType.EncryptorKeyFile);
        }

        public Task StoreIndexFileAsync(string? bsname, IndexFileType fileType, byte[] data)
        {
            return StoreFileAsync(GetIndexFilePath(bsname, fileType), data, fileType==IndexFileType.EncryptorKeyFile);
        }

        public async Task<byte[]> LoadBlobAsync(byte[] hash, bool decrypt)
        {
            return await LoadFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)), decrypt);
        }

        public async Task<(byte[] encryptedHash, string fileId)> StoreBlobAsync(byte[] hash, byte[] data)
        {
            return await StoreFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)), hash, data);
        }

        public Task DeleteBlobAsync(byte[] hash, string fileId)
        {
            return DeleteFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)), fileId);
        }

        private static string GetIndexFilePath(string? bsname, IndexFileType fileType)
        {
            string filename;
            switch (fileType)
            {
                case IndexFileType.BlobIndex:
                    filename = Path.Combine(IndexDirectory, Core.BackupBlobIndexFile);
                    break;
                case IndexFileType.BackupSet:
                    if (bsname == null)
                    {
                        throw new Exception("Backup set name required to load backupset");
                    }
                    filename = Path.Combine(IndexDirectory, "backupstores", bsname);
                    break;
                case IndexFileType.SettingsFile:
                    filename = Path.Combine(IndexDirectory, Core.SettingsFilename);
                    break;
                case IndexFileType.EncryptorKeyFile:
                    filename = Path.Combine(IndexDirectory, "keyfile");
                    break;
                default:
                    throw new ArgumentException("Unknown IndexFileType");
            }
            return filename;
        }

        private struct BBConnectionSettings
        {
            public string accountId { get; set; }
            public string ApplicationKey { get; set; }
            public string bucketId { get; set; }
            public string bucketName { get; set; }
        }

        private struct AuthorizationResponse
        {
            public string accountId { get; set; }
            public string apiUrl { get; set; }
            public string authorizationToken { get; set; }
            public string downloadUrl { get; set; }
        }

        private struct GetUploadUrlResponse
        {
            public string bucketId { get; set; }
            public string uploadUrl { get; set; }
            public string authorizationToken { get; set; }
        }
        private struct UploadResponse
        {
            public string fileId { get; set; }
            public string fileName { get; set; }
            public string accountId { get; set; }
            public string bucketId { get; set; }
            public int contentLength { get; set; }
            public string contentSha1 { get; set; }
            public string contentType { get; set; }
        }

        private struct GetFilesResponse
        {
            public FileDescription[] files { get; set; }
            
        }

        private struct FileDescription
        {
            public string fileId { get; set; }
            public string fileName { get; set; }
            public string contentSha1 { get; set; }
        }
    }
}
