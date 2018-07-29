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
    public class BackblazeInterop : IDstFSInterop
    {
        private string ConnectionSettingsFile { get; set; }

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

        public BackblazeInterop(string accountid, string applicationkey,
            string bucketid, string bucketname)
        {
            AccountID = accountid;
            ApplicationKey = applicationkey;
            BucketId = bucketid;
            BucketName = bucketname;
        }

        public BackblazeInterop(string connectionsettingsfile)
        {
            ConnectionSettingsFile = connectionsettingsfile;
            BBConnectionSettings connectionsettings = LoadBBConnectionSettings();
            AccountID = connectionsettings.accountId;
            ApplicationKey = connectionsettings.ApplicationKey;
            BucketId = connectionsettings.bucketId;
            BucketName = connectionsettings.bucketName;
            AuthResp = AuthorizeAccount().Result;
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
                .GetJsonAsync<AuthorizationResponse>();
                SuccessfulTransmission();
                return authresp;
            }
            catch (FlurlHttpException)
            {
                FailedTransmission();
                if (attempts < Retries)
                {
                    return await AuthorizeAccount(attempts + 1);
                }
                throw;
            }
        }

        private async void StoreFileAsync(string file, byte[] data) => await StoreFileAsync(file, HashTools.GetSHA1Hasher().ComputeHash(data), data);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <param name="hash"></param>
        /// <param name="data"></param>
        /// <returns>fileId</returns>
        private async Task<string> StoreFileAsync(string file, byte[] hash, byte[] data)
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
                    .ReceiveJson<GetUploadUrlResponse>();
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
                        return await GetUploadUrl(attempts + 1);
                    }
                    throw;
                }
            }

            string fileid = await UploadData();
            async Task<string> UploadData(int attempts = 0)
            {
                if (UploadUrlResp == null)
                {
                    UploadUrlResp = await GetUploadUrl();
                }
                Delay();
                try
                {
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
                        .ReceiveJson<UploadResponse>();
                    SuccessfulTransmission();
                    return uploadresp.fileId;
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
                        return await UploadData(attempts + 1);
                    }
                    throw;
                }
            }
            return fileid;
        }

        private async Task<byte[]> LoadFileAsync(string fileName)
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
                        .GetAsync();
                    var data = await downloadresp.Content.ReadAsByteArrayAsync();
                    SuccessfulTransmission();
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
                        return await Download(attempts + 1);
                    }
                    throw;
                }
            }
            return downloaddata;
        }

        private void DeleteFileAsync(string filename, string fileid)
        {
            Delete();
            async void Delete(int attempts = 0)
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
                        });
                    SuccessfulTransmission();
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
                        Delete(attempts + 1);
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
                        .ReceiveJson<GetFilesResponse>();
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
                        return await Exists(attempts + 1);
                    }
                    throw;
                }
            }
            return exists;
        }

        private BBConnectionSettings LoadBBConnectionSettings()
        {
            string connectionsettings;
            using (var sr = new StreamReader(ConnectionSettingsFile))
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
            return LoadFileAsync(GetIndexFilePath(bsname, fileType));
        }

        public void StoreIndexFileAsync(string bsname, IndexFileType fileType, byte[] data)
        {
            StoreFileAsync(GetIndexFilePath(bsname, fileType), data);
        }

        public Task<byte[]> LoadBlobAsync(byte[] hash)
        {
            return LoadFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)));
        }

        public Task<string> StoreBlobAsync(byte[] hash, byte[] data)
        {
            return StoreFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)), hash, data);
        }

        public void DeleteBlobAsync(byte[] hash, string fileId)
        {
            DeleteFileAsync(Path.Combine(BlobSaveDirectory, HashTools.ByteArrayToHexViaLookup32(hash)), fileId);
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
