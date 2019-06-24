using System;
using System.Collections.Generic;
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
        private string ConnectionSettingsFile { get; set; }

        private AesHelper Encryptor { get; set; }

        private string AccountID { get; set; }
        private string ApplicationKey { get; set; }
        private string BucketId { get; set; }
        private string BucketName { get; set; }
        
        private AuthorizationResponse AuthResp = null;
        private GetUploadUrlResponse UploadUrlResp = null;

        private static readonly int MinDelayMS = 32;
        private int DelayMS { get; set; } = 32;
        private static readonly int MaxDelayMS = 16_000;

        private static readonly int Retries = 7;

        public string BlobSaveDirectory
        {
            get => "blobdata";
        }

        public string IndexDirectory
        {
            get => "index";
        }

        private BackblazeDstInterop(string accountid, string applicationkey,
            string bucketid, string bucketname, string connectionsettingsfile=null)
        {
            AccountID = accountid;
            ApplicationKey = applicationkey;
            BucketId = bucketid;
            BucketName = bucketname;
            ConnectionSettingsFile = connectionsettingsfile;
            AuthResp = AuthorizeAccount().Result;
        }

        public static IDstFSInterop InitializeNew(string connectionsettingsfile, string password = null)
        {
            BBConnectionSettings connectionsettings = LoadBBConnectionSettings(connectionsettingsfile);
            return InitializeNew(connectionsettings.accountId, connectionsettings.ApplicationKey,
                connectionsettings.bucketId, connectionsettings.bucketName, connectionsettingsfile, password);
        }

        public static IDstFSInterop InitializeNew(string accountid, 
            string applicationkey, string bucketid, string bucketname,
            string connectionSettingsFile=null, string password = null)
        {
            BackblazeDstInterop backblazeDstInterop = new BackblazeDstInterop(accountid, applicationkey, bucketid, 
                bucketname, connectionSettingsFile);
            if (password != null)
            {
                AesHelper encryptor = AesHelper.CreateFromPassword(password);
                byte[] keyfile = encryptor.CreateKeyFile();
                backblazeDstInterop.StoreIndexFileAsync(null, IndexFileType.EncryptorKeyFile, keyfile).Wait();
                backblazeDstInterop.Encryptor = encryptor;
            }
            return backblazeDstInterop;
        }

        public static IDstFSInterop Load(string connectionsettingsfile, string password=null)
        {
            BBConnectionSettings connectionsettings = LoadBBConnectionSettings(connectionsettingsfile);
            return Load(connectionsettings.accountId, connectionsettings.ApplicationKey,
                connectionsettings.bucketId, connectionsettings.bucketName, connectionsettingsfile, password);
        }

        public static IDstFSInterop Load(string accountid, string applicationkey, 
            string bucketid, string bucketname, string connectionSettingsFile=null,
            string password = null)
        {
            BackblazeDstInterop backblazeDstInterop = new BackblazeDstInterop(accountid, applicationkey, bucketid, bucketname,
                connectionSettingsFile);
            if (password != null)
            {
                byte[] keyfile = backblazeDstInterop.LoadIndexFileAsync(null, IndexFileType.EncryptorKeyFile).Result;
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

        private async Task<AuthorizationResponse> AuthorizeAccount(int attempts = 0)
        {
            Delay();
            try
            {
                var authresp = await "https://api.backblazeb2.com/b2api/v1/b2_authorize_account"
                .WithHeaders(new
                {
                    Authorization = "Basic "
                        + Convert.ToBase64String(Encoding.UTF8.GetBytes(AccountID
                            + ":" + ApplicationKey))
                })
                .GetJsonAsync<AuthorizationResponse>().ConfigureAwait(false);
                SuccessfulTransmission();
                return authresp;
            }
            catch (FlurlHttpException)
            {
                FailedTransmission();
                if (attempts < Retries)
                {
                    return await AuthorizeAccount(attempts + 1).ConfigureAwait(false);
                }
                throw;
            }
        }

        private Task StoreFileAsync(string file, byte[] data, bool preventEncryption=false) => StoreFileAsync(file, HashTools.GetSHA1Hasher().ComputeHash(data), data, preventEncryption);

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
                    var urlresp = await AuthResp.apiUrl
                    .AppendPathSegment("/b2api/v1/b2_get_upload_url")
                    .WithHeaders(new { Authorization = AuthResp.authorizationToken })
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
                    var uploadresp = await UploadUrlResp.uploadUrl
                        .WithHeaders(new
                        {
                            Authorization = UploadUrlResp.authorizationToken,
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

        private async Task<byte[]> LoadFileAsync(string fileName, bool preventEncrypt=false)
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
                    HttpResponseMessage downloadresp = await AuthResp.downloadUrl
                        .AppendPathSegment("file")
                        .AppendPathSegment(BucketName)
                        .AppendPathSegment(fileName)
                        .WithHeaders(new { Authorization = AuthResp.authorizationToken })
                        .GetAsync().ConfigureAwait(false);
                    var data = await downloadresp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                    SuccessfulTransmission();

                    if (Encryptor != null && !preventEncrypt)
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
                    var deleteresp = await AuthResp.apiUrl
                        .AppendPathSegment("/b2api/v1/b2_delete_file_version")
                        .WithHeaders(new { Authorization = AuthResp.authorizationToken })
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
                    var filesresp = await AuthResp.apiUrl
                        .AppendPathSegment("/b2api/v1/b2_list_file_names")
                        .WithHeaders(new { Authorization = AuthResp.authorizationToken })
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

        private static BBConnectionSettings LoadBBConnectionSettings(string connectionsettingsfile)
        {
            string connectionsettings;
            using (var sr = new StreamReader(connectionsettingsfile))
            {
                connectionsettings = sr.ReadToEnd();
            }
            return JsonConvert.DeserializeObject<BBConnectionSettings>(connectionsettings);
        }

        private int Min(params int[] nums)
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
                
        private int Max(params int[] nums)
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

        public Task<bool> IndexFileExistsAsync(string bsname, IndexFileType fileType)
        {
            return FileExistsAsync(GetIndexFilePath(bsname, fileType));
        }

        public Task<byte[]> LoadIndexFileAsync(string bsname, IndexFileType fileType)
        {
            return LoadFileAsync(GetIndexFilePath(bsname, fileType), fileType == IndexFileType.EncryptorKeyFile);
        }

        public Task StoreIndexFileAsync(string bsname, IndexFileType fileType, byte[] data)
        {
            return StoreFileAsync(GetIndexFilePath(bsname, fileType), data, fileType==IndexFileType.EncryptorKeyFile);
        }

        public Task<byte[]> LoadBlobAsync(byte[] hash)
        {
            return LoadFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)));
        }

        public Task<(byte[] encryptedHash, string fileId)> StoreBlobAsync(byte[] hash, byte[] data)
        {
            return StoreFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)), hash, data);
        }

        public Task DeleteBlobAsync(byte[] hash, string fileId)
        {
            return DeleteFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)), fileId);
        }

        private string GetIndexFilePath(string bsname, IndexFileType fileType)
        {
            string filename;
            switch (fileType)
            {
                case IndexFileType.BlobIndex:
                    filename = Path.Combine(IndexDirectory, Core.BackupBlobIndexFile);
                    break;
                case IndexFileType.BackupSet:
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

        private class BBConnectionSettings
        {
            public string accountId { get; set; }
            public string ApplicationKey { get; set; }
            public string bucketId { get; set; }
            public string bucketName { get; set; }
        }

        private class AuthorizationResponse
        {
            public string accountId { get; set; }
            public string apiUrl { get; set; }
            public string authorizationToken { get; set; }
            public string downloadUrl { get; set; }
        }

        private class GetUploadUrlResponse
        {
            public string bucketId { get; set; }
            public string uploadUrl { get; set; }
            public string authorizationToken { get; set; }
        }
        private class UploadResponse
        {
            public string fileId { get; set; }
            public string fileName { get; set; }
            public string accountId { get; set; }
            public string bucketId { get; set; }
            public int contentLength { get; set; }
            public string contentSha1 { get; set; }
            public string contentType { get; set; }
        }

        private class GetFilesResponse
        {
            public FileDescription[] files { get; set; }
            
        }

        private class FileDescription
        {
            public string fileId { get; set; }
            public string fileName { get; set; }
            public string contentSha1 { get; set; }
        }
    }
}
