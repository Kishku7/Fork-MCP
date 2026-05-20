using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Fork.Logic.WebRequesters;

namespace Fork.Logic.Service;

/// <summary>
/// Handles resource pack SHA-1 hash checking and computation for server startup.
/// Extracted from ServerManager.
/// </summary>
public static class ResourcePackService
{
    /// <summary>
    /// Returns true if the cached hash is still valid (resource pack has not changed since hashDate).
    /// </summary>
    public static async Task<bool> IsHashUpToDate(DateTime hashDate, string fileSourceUrl)
    {
        if (string.IsNullOrEmpty(fileSourceUrl))
        {
            return false;
        }

        HttpWebRequest request = WebRequest.CreateHttp(fileSourceUrl);
        request.Method = WebRequestMethods.Http.Head;
        HttpWebResponse webResponse = await request.GetResponseAsync() as HttpWebResponse;
        DateTime? lastModified = webResponse?.LastModified;

        if (lastModified != null && lastModified.Value.CompareTo(hashDate) < 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Downloads the resource pack at <paramref name="url"/> and returns its SHA-1 hash as an uppercase hex string.
    /// Returns an empty string if the URL is empty or the content-type is not application/zip.
    /// </summary>
    public static async Task<string> HashResourcePack(string url, IProgress<double> downloadProgress)
    {
        string result = "";
        if (string.IsNullOrEmpty(url))
        {
            return result;
        }

        // Ensure tmp directory exists
        new DirectoryInfo(Path.Combine(App.ApplicationPath, "tmp")).Create();
        FileInfo resourcePackFile = new(
            Path.Combine(App.ApplicationPath, "tmp", Guid.NewGuid().ToString()
                .Replace("-", "") + ".zip"));

        // Verify content-type before downloading
        HttpWebRequest request = WebRequest.CreateHttp(url);
        request.Method = WebRequestMethods.Http.Head;
        HttpWebResponse webResponse = await request.GetResponseAsync() as HttpWebResponse;
        if (webResponse != null && webResponse.ContentType != "application/zip")
        {
            return result;
        }

        await Downloader.DownloadFileAsync(url, resourcePackFile.FullName, downloadProgress);

        // Calculate SHA-1
        await using (FileStream fs = resourcePackFile.OpenRead())
        {
            await using BufferedStream bs = new(fs);
            using (SHA1Managed sha1 = new())
            {
                byte[] hash = sha1.ComputeHash(bs);
                StringBuilder formatted = new(2 * hash.Length);
                foreach (byte b in hash) formatted.AppendFormat("{0:X2}", b);
                result = formatted.ToString();
            }
        }

        resourcePackFile.Delete();
        return result;
    }
}
