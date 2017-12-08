using System;
using System.Collections.Generic;
using System.Text;
using Flurl;
using Flurl.Http;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;

namespace BackupCore
{
    public class BackblazeInterop : ICloudInterop
    {
        private string ConnectionSettingsFile { get; set; }

        private string AccountID { get; set; }
        private string ApplicationKey { get; set; }
        private string BucketId { get; set; }
        private string BucketName { get; set; }

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
        }

        private async Task<AuthorizationResponse> AuthorizeAccount()
        {
            var authresp = await "https://api.backblazeb2.com/b2api/v1/b2_authorize_account"
                .WithHeaders(new
                {
                    Authorization = "Basic "
                        + Convert.ToBase64String(Encoding.UTF8.GetBytes(AccountID
                            + ":" + ApplicationKey))
                })
                .GetJsonAsync<AuthorizationResponse>();
            return authresp;
        }

        public async Task<string> UploadFileAsync(string file, byte[] data) => await UploadFileAsync(file, HashTools.GetSHA1Hasher().ComputeHash(data), data);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <param name="hash"></param>
        /// <param name="data"></param>
        /// <returns>fileId</returns>
        public async Task<string> UploadFileAsync(string file, byte[] hash, byte[] data)
        {
            AuthorizationResponse authresp = await AuthorizeAccount();

            var urlresp = await authresp.apiUrl
                .AppendPathSegment("/b2api/v1/b2_get_upload_url")
                .WithHeaders(new { Authorization = authresp.authorizationToken })
                .PostJsonAsync(new { bucketId = BucketId })
                .ReceiveJson<GetUrlResponse>();

            var filecontent = new ByteArrayContent(data);
            filecontent.Headers.Add("Content-Type", "application/octet-stream");
            var uploadresp = await urlresp.uploadUrl
                .WithHeaders(new
                {
                    Authorization = urlresp.authorizationToken,
                    X_Bz_File_Name = file,
                    Content_Length = data.Length,
                    X_Bz_Content_Sha1 = HashTools.ByteArrayToHexViaLookup32(hash)
                })
                .PostAsync(filecontent)
                .ReceiveJson<UploadResponse>();
            return uploadresp.fileId;
        }

        public async Task<byte[]> DownloadFileAsync(string fileNameOrId, bool fileid = false)
        {
            AuthorizationResponse authresp = await AuthorizeAccount();
            HttpResponseMessage downloadresp;
            if (fileid)
            {
                downloadresp = await authresp.downloadUrl
                    .AppendPathSegment("/b2api/v1/b2_download_file_by_id")
                    .WithHeaders(new { Authorization = authresp.authorizationToken })
                    .PostJsonAsync(new { fileId = fileNameOrId });
            }
            else
            {
                downloadresp = await authresp.downloadUrl
                    .AppendPathSegment("file")
                    .AppendPathSegment(BucketName)
                    .AppendPathSegment(fileNameOrId)
                    .WithHeaders(new { Authorization = authresp.authorizationToken })
                    .GetAsync();
            }
            

            return await downloadresp.Content.ReadAsByteArrayAsync();
        }

        public async void DeleteFileAsync(string filename, string fileid)
        {
            AuthorizationResponse authresp = await AuthorizeAccount();

            var deleteresp = await authresp.apiUrl
                .AppendPathSegment("/b2api/v1/b2_delete_file_version")
                .WithHeaders(new { Authorization = authresp.authorizationToken })
                .PostJsonAsync(new
                {
                    fileId = fileid,
                    fileName = filename
                });
        }

        public async Task<bool> FileExistsAsync(string file)
        {
            AuthorizationResponse authresp = await AuthorizeAccount();

            var filesresp = await authresp.apiUrl
                .AppendPathSegment("/b2api/v1/b2_list_file_names")
                .WithHeaders(new { Authorization = authresp.authorizationToken })
                .PostJsonAsync(new
                {
                    bucketId = BucketId,
                    startFileName = file,
                    maxFileCount = 1
                })
                .ReceiveJson<GetFilesResponse>();

            return filesresp.files.Length > 0 && filesresp.files[0].fileName == file;
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

        private class GetUrlResponse
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
