using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Fork.Annotations;
using Fork.Logic.Logging;
using Fork.Logic.Manager;
using Newtonsoft.Json;

namespace Fork.Logic.WebRequesters;

public class PaperWebRequester
{
    public List<string> RequestPaperVersions()
    {
        string url = "https://fill.papermc.io/v3/projects/paper";
        string json = ResponseCache.Instance.UncacheResponse(url);
        if (json == null)
        {
            try
            {
                Uri uri = new(url);
                HttpWebRequest request = WebRequest.CreateHttp(uri);
                request.UserAgent = ApplicationManager.UserAgent;
                using (WebResponse response = request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new(stream!))
                    json = reader.ReadToEnd();

                ResponseCache.Instance.CacheResponse(url, json);
            }
            catch (WebException e)
            {
                ErrorLogger.Append(e);
                Console.WriteLine(
                    "Could not receive Paper Versions (either papermc.io is down or your Internet connection is not working)");
                return new List<string>();
            }
        }

        PaperVersions paperVersions = JsonConvert.DeserializeObject<PaperVersions>(json);

        if (paperVersions == null || !paperVersions.project.id.Equals("paper"))
        {
            return null;
        }

        return paperVersions.versions.SelectMany(v => v.Value).ToList();
    }

    public async Task<int> RequestLatestBuildId(string version)
    {
        string url = "https://fill.papermc.io/v3/projects/paper/versions/" + version;
        {
            try
            {
                HttpWebRequest request = WebRequest.CreateHttp(url);
                request.UserAgent = ApplicationManager.UserAgent;
                using WebResponse response = request.GetResponse();
                await using Stream stream = response.GetResponseStream();
                using StreamReader reader = new(stream!);
                string json = await reader.ReadToEndAsync();
                PaperVersionDetails obj = JsonConvert.DeserializeObject<PaperVersionDetails>(json);
                ;
                return obj.builds.LastOrDefault();
            }
            catch (Exception e)
            {
                ErrorLogger.Append(e);
                Console.WriteLine("Could not get latest build id for paper version " + version);
                return 0;
            }
        }
    }

    [ItemCanBeNull]
    public async Task<string> RequestDownloadUrlForLatestBuild(string version)
    {
        string url = "https://fill.papermc.io/v3/projects/paper/versions/" + version + "/builds/latest";
        try
        {
            HttpWebRequest request = WebRequest.CreateHttp(url);
            request.UserAgent = ApplicationManager.UserAgent;
            using WebResponse response = request.GetResponse();
            await using Stream stream = response.GetResponseStream();
            using StreamReader reader = new(stream!);
            string json = await reader.ReadToEndAsync();
            PaperVersionBuildInformation obj = JsonConvert.DeserializeObject<PaperVersionBuildInformation>(json);
            return obj.downloads["server:default"].url;
        }
        catch (Exception e)
        {
            ErrorLogger.Append(e);
            Console.WriteLine("Could not get download url for latest build id for paper version " + version);
            return null;
        }
    }


    private class PaperVersions
    {
        public PaperProject project;
        public Dictionary<string, string[]> versions;

        internal class PaperProject
        {
            public string id;
            public string name;
        }
    }

    private class PaperVersionDetails
    {
        public PaperVersionDetailsVersion version;
        public int[] builds;

        internal class PaperVersionDetailsVersion
        {
            public string id;
            // More properties like minimum java version and recommended jvm flags would be available here
        }
    }

    private class PaperVersionBuildInformation
    {
        public string id;
        public DateTime time;
        public string channel;
        public Dictionary<string, PaperVersionBuildInformationDownloadInfo> downloads;

        internal class PaperVersionBuildInformationDownloadInfo
        {
            public string name;
            public long size;
            public string url;
        }
    }
}