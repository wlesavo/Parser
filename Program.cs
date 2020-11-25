using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using MongoDB.Driver;
using MongoDB.Bson;
using FcmSharp.Requests;
using FcmSharp.Settings;
using FcmSharp;
using System.Net.Http;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;

namespace Pars
{
    class Program
    {
        static void Main(string[] args)
        {
            // vk vars & configs

            string vk_http_url, vk_plat_likes, vk_platinum_url, vk_platinum_text, vk_title, vk_hashtags, temp_str;
            string[] temp, words;
            WallObj vk;
            string vk_owner_id = "id of a public vk page";
            string vk_offset = "24";
            string vk_access_token = "vk token";
            int vk_likes_min = 100;
            int vk_last_id = 0;

            // instagram vars & configs

            int inst_likes, inst_comments;
            string inst_text, inst_url, inst_id, inst_title, inst_hashtags, inst_last_id;
            inst_last_id = "";
            int inst_likes_min = 10000;
            int inst_offset = 3;
            InstagramObj inst;

            //youtube vars, configs & initialization

            string google_token = "token from google account";
            int yt_video_counts = 3;
            var yt_views_required = 100000ul;
            var yt_likes_required = 2000ul;
            var yt_conf = new YouTubeService(new BaseClientService.Initializer() { ApiKey = google_token });
            var yt_channels_request = yt_conf.Channels.List("contentDetails");
            yt_channels_request.ForUsername = "username of a channel";
            List<string> yt_id_storage = new List<string>();

            //mongodb configs from appharbor platform

            string mongo_connection_string = "connection address";
            MongoClient mongo_client = new MongoClient(mongo_connection_string);
            IMongoDatabase database = mongo_client.GetDatabase("db name");
            IMongoCollection<BsonDocument> mongo_platinum_col = database.GetCollection<BsonDocument>("document name");
            BsonDocument mongo_col_all = new BsonDocument();
            var id_k = mongo_platinum_col.CountDocuments(mongo_col_all);

            // FTP configs for uploading parsed data

            string complete_dir_path = @"ftp_adress";
            string ftp_username = "username";
            string ftp_password = "password";
            string ftp_file_name = "storage.json";
            string platinum_str_to_upload;
            Uri ftp_file_uri = new Uri(complete_dir_path + "/http/" + ftp_file_name);
            WebClient ftp_webclient = new WebClient();
            ftp_webclient.Encoding = new UTF8Encoding(true);
            string json_ftp_string;

            // background worker loop

            while (true)
            {
                Thread.Sleep(1200000);

                //getting stored data from ftp server 
                json_ftp_string = ftp_webclient.DownloadString(complete_dir_path + ftp_file_name);

                //start of vk part

                //get json data from vk public page
                vk_http_url = string.Format("https://api.vk.com/method/wall.get?owner_id={0}&offset={1}&count=1&access_token={2}&v=5.95", vk_owner_id, vk_offset, vk_access_token);
                using (StreamReader strr = new StreamReader(HttpWebRequest.Create(vk_http_url).GetResponse().GetResponseStream(), Encoding.UTF8))
                    temp_str = strr.ReadToEnd();
                vk = JsonConvert.DeserializeObject<WallObj>(temp_str);

                // if something changed
                if (vk.response.items[0].Id != vk_last_id)
                {
                    vk_last_id = vk.response.items[0].Id;
                    //if trashold is passed
                    if (vk.response.items[0].likes.Count > vk_likes_min)
                    {
                        vk_platinum_url = "https://vk.com/wall" + vk_owner_id + "_" + vk.response.items[0].Id.ToString();
                        id_k = mongo_platinum_col.CountDocuments(new BsonDocument()) + 1;
                        vk_plat_likes = vk.response.items[0].likes.Count.ToString();
                        vk_platinum_text = vk.response.items[0].Text;
                        words = vk_platinum_text.Split(new char[] { ' ' });
                        vk_hashtags = "";

                        //parse hashtags from main text and split complex hash tags
                        foreach (string s in words)
                        {
                            if (s.StartsWith("#"))
                            {
                                temp = s.Split(new char[] { '@' });
                                vk_hashtags += " " + temp[0];
                            }
                        }

                        if (vk_hashtags.Length > 1)
                        {
                            vk_hashtags = vk_hashtags.Substring(1);
                        }

                        //TODO: check for characters ruining json file
                        words = vk_platinum_text.Split(new char[] { '.' });
                        vk_title = words[0];
                        vk_title = vk_title.Replace('"', '\"');

                        //upload data to mongo and ftp
                        mongo_col_all = new BsonDocument { { "Source", "vk" }, { "Id", id_k.ToString() }, { "URL", vk_platinum_url }, { "likes", vk_plat_likes } };
                        mongo_platinum_col.InsertOne(mongo_col_all);
                        platinum_str_to_upload = "{ \"Source\": \"vk\", \"Id\": " + id_k.ToString()
                                               + ", \"URL\": \"" + vk_platinum_url
                                               + "\", \"title\": \"" + vk_title
                                               + "\", \"likes\": " + vk_plat_likes
                                               + ", \"hashtags\": \"" + vk_hashtags
                                               + "\" }, ";
                        json_ftp_string = json_ftp_string.Insert(16, platinum_str_to_upload);
                        UploadFile(ftp_file_uri, json_ftp_string, ftp_username, ftp_password);

                        //send push notifications about new post
                        SendPush("vk", "vk", vk_platinum_url, id_k.ToString(), vk_plat_likes, vk_title, vk_hashtags);
                    }
                }

                //end of vk part

                Thread.Sleep(1200000);

                //start of youtube part

                var yt_channels_response = yt_channels_request.Execute();
                foreach (var yt_channel in yt_channels_response.Items)
                {
                    //getting the list of uploaded videos
                    var yt_uploads_list_id = yt_channel.ContentDetails.RelatedPlaylists.Uploads;
                    var yt_playlist_items_request = yt_conf.PlaylistItems.List("Snippet");
                    yt_playlist_items_request.PlaylistId = yt_uploads_list_id;
                    yt_playlist_items_request.MaxResults = yt_video_counts;

                    //execute request and loop over videos
                    var yt_playlist_items_response = yt_playlist_items_request.Execute();
                    foreach (var playlist_item in yt_playlist_items_response.Items)
                    {
                        string yt_video_id = playlist_item.Snippet.ResourceId.VideoId;
                        if (!(yt_id_storage.Contains(yt_video_id)))
                        {
                            //get video stats
                            var video_request = yt_conf.Videos.List("statistics");
                            video_request.Id = yt_video_id;
                            var response = video_request.Execute();
                            var yt_view_count = response.Items[0].Statistics.ViewCount;
                            var yt_like_count = response.Items[0].Statistics.LikeCount;
                            var yt_dislike_count = response.Items[0].Statistics.DislikeCount;
                            var yt_comment_count = response.Items[0].Statistics.CommentCount;

                            //some metrics for popularity of the video
                            if ((yt_view_count > yt_views_required) || ((yt_like_count - yt_dislike_count) > yt_likes_required))
                            {
                                string yt_text = playlist_item.Snippet.Description;
                                string yt_title = playlist_item.Snippet.Title;
                                id_k = mongo_platinum_col.CountDocuments(new BsonDocument()) + 1;
                                words = yt_text.Split(new char[] { ' ' });

                                //parsing tags
                                string yt_hashtags = "";
                                foreach (string s1 in words)
                                {
                                    if (s1.StartsWith("#"))
                                    {
                                        temp = s1.Split(new char[] { '@' });
                                        yt_hashtags += " " + temp[0];
                                    }
                                }
                                if (yt_hashtags.Length > 1)
                                {
                                    yt_hashtags = yt_hashtags.Substring(1);
                                }

                                //forming title
                                yt_title = yt_title.Replace('"', '\"');
                                string yt_url = "https://www.youtube.com/watch?v=" + yt_video_id;

                                //send to mogodb
                                mongo_col_all = new BsonDocument { { "Source", "yt" }, { "Id", id_k.ToString() }, { "URL", yt_url }, { "likes", yt_like_count.ToString() } };
                                mongo_platinum_col.InsertOne(mongo_col_all);

                                //send to ftp
                                platinum_str_to_upload = "{ \"Source\": \"yt\", \"Id\": " + id_k.ToString()
                                                       + ", \"URL\": \"" + yt_url
                                                       + "\", \"title\": \"" + yt_title
                                                       + "\", \"likes\": " + yt_like_count.ToString()
                                                       + ", \"hashtags\": \"" + yt_hashtags
                                                       + "\" }, ";
                                json_ftp_string = json_ftp_string.Insert(16, platinum_str_to_upload);
                                UploadFile(ftp_file_uri, json_ftp_string, ftp_username, ftp_password);

                                //send push notification
                                SendPush("yt", "youtube", yt_url, id_k.ToString(), yt_like_count.ToString(), yt_title, yt_hashtags);
                                //save to list
                                yt_id_storage.Add(yt_video_id);
                            }
                        }
                    }
                }

                // end of yotube part

                Thread.Sleep(1200000);
                // start of instagram part
                using (var instagram = new Instagram())
                {
                    if (instagram.LoginAsync("app", "pass").GetAwaiter().GetResult())
                    {
                        //get instagram posts
                        string inst_json_string = instagram.Client.GetStringAsync("/public_page/?__a=1").GetAwaiter().GetResult();
                        inst = JsonConvert.DeserializeObject<InstagramObj>(inst_json_string);
                        inst_likes = inst.graphql.user.edge_owner_to_timeline_media.edges[inst_offset].node.edge_liked_by.Count;
                        inst_comments = inst.graphql.user.edge_owner_to_timeline_media.edges[inst_offset].node.edge_liked_by.Count;
                        if (inst.graphql.user.edge_owner_to_timeline_media.edges[inst_offset].node.edge_media_to_caption.hui.Count() > 0)
                        {
                            inst_text = inst.graphql.user.edge_owner_to_timeline_media.edges[inst_offset].node.edge_media_to_caption.hui[0].pizda.Text;
                        }
                        else
                        {
                            inst_text = "";
                        }

                        inst_url = "https://www.instagram.com/p/" + inst.graphql.user.edge_owner_to_timeline_media.edges[inst_offset].node.Shortcode;
                        inst_id = inst.graphql.user.edge_owner_to_timeline_media.edges[inst_offset].node.Id;

                        if (inst_id != inst_last_id)
                        {
                            inst_last_id = inst_id;
                            if (inst_likes > inst_likes_min)
                            {
                                id_k = mongo_platinum_col.CountDocuments(new BsonDocument()) + 1;
                                //get hashtags
                                words = inst_text.Split(new char[] { ' ' });
                                inst_hashtags = "";
                                foreach (string s in words)
                                {
                                    if (s.StartsWith("#"))
                                    {
                                        temp = s.Split(new char[] { '@' });
                                        inst_hashtags += " " + temp[0];
                                    }
                                }
                                if (inst_hashtags.Length > 1)
                                {
                                    inst_hashtags = inst_hashtags.Substring(1);
                                }
                                words = inst_text.Split(new char[] { '.' });
                                inst_title = words[0];
                                words = inst_title.Split(new char[] { '#' });
                                inst_title = words[0];
                                inst_title = inst_title.Replace('"', '\"');

                                //upload to mongo db
                                mongo_col_all = new BsonDocument { { "Source", "inst" }, { "Id", id_k.ToString() }, { "URL", inst_url }, { "likes", inst_likes.ToString() } };
                                mongo_platinum_col.InsertOne(mongo_col_all);

                                //upload to ftp
                                platinum_str_to_upload = "{ \"Source\": \"inst\", \"Id\": " + id_k.ToString()
                                                       + ", \"URL\": \"" + inst_url
                                                       + "\", \"title\": \"" + inst_title
                                                       + "\", \"likes\": " + inst_likes.ToString()
                                                       + ", \"hashtags\": \"" + inst_hashtags
                                                       + "\" }, ";

                                json_ftp_string = json_ftp_string.Insert(16, platinum_str_to_upload);
                                UploadFile(ftp_file_uri, json_ftp_string, ftp_username, ftp_password);

                                //send push notifications
                                SendPush("inst", "instagram", inst_url, id_k.ToString(), inst_likes.ToString(), inst_title, inst_hashtags);
                            }


                        }
                    }
                }

                // end of instagram part

            }

        }

        private static void UploadFile(Uri target, string data, string name, string pass)
        {
            // simple function for uploading parsed data to ftp server

            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(target);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.ContentLength = data.Length;
            request.Credentials = new NetworkCredential(name, pass);

            using (Stream requestStream = request.GetRequestStream())
            {
                byte[] databyte = System.Text.Encoding.UTF8.GetBytes(data);
                requestStream.Write(databyte, 0, databyte.Length);
                requestStream.Close();
            }

            FtpWebResponse response = (FtpWebResponse)request.GetResponse();
            response.Close();
        }

        private static void SendPush(string source, string topic, string url, string id, string likes, string body, string hashtags)
        {
            var fcm_settings = FileBasedFcmClientSettings.CreateFromFile(Directory.GetCurrentDirectory() + @"\fcm_config.json");
            using (var clientFcm = new FcmClient(fcm_settings))
            {
                string title = "Platinum post " + topic;
                var data = new Dictionary<string, string>()
                                    {
                                        {"Source", source},
                                        {"URL", url},
                                        {"Id", id},
                                        {"likes", likes},
                                        {"title", title},
                                        {"body", body},
                                        {"hashtags", hashtags}
                                    };
                var message = new FcmMessage()
                {
                    ValidateOnly = false,
                    Message = new Message
                    {
                        Topic = topic,
                        Data = data

                    },
                };

                CancellationTokenSource cts = new CancellationTokenSource();
                clientFcm.SendAsync(message, cts.Token).GetAwaiter().GetResult();
            }
        }
    }

    public class Instagram : IDisposable
    {
        // class for connection to instgram
        private const string USER_AGENT =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_10_3) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/45.0.2414.0 Safari/537.36";

        private HttpClientHandler m_handler;
        private HttpClient m_client;

        public HttpClient Client
        {
            get { return m_client; }
        }

        public Instagram()
        {
            m_handler = new HttpClientHandler();
            m_client = new HttpClient(m_handler);
            m_client.BaseAddress = new Uri("https://instagram.com/");
            m_client.DefaultRequestHeaders.UserAgent.ParseAdd(USER_AGENT);
        }

        public void Dispose()
        {
            m_client.Dispose();
            m_handler.Dispose();
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            // getting login page for setting cookies to 'csrftoken'
            await m_client.GetAsync("/accounts/login/");

            // getting token from cookies
            var cookies = m_handler.CookieContainer.GetCookies(m_client.BaseAddress);
            var csrftoken = cookies["csrftoken"].Value;

            // login fields
            var fields = new Dictionary<string, string>()
            {
                { "username", username },
                { "password", password }
            };

            // making a request
            var request = new HttpRequestMessage(HttpMethod.Post, "/accounts/login/ajax/");
            request.Content = new FormUrlEncodedContent(fields);
            request.Headers.Referrer = new Uri(m_client.BaseAddress, "/accounts/login/");

            // setting headers
            request.Headers.Add("X-CSRFToken", csrftoken);
            request.Headers.Add("X-Instagram-AJAX", "1");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");

            // AJAX autorization
            var response = await m_client.SendAsync(request);
            var info = JsonConvert.DeserializeObject<LoginInfo>(await response.Content.ReadAsStringAsync());

            return info.authenticated;
        }

        private class LoginInfo
        {
            public string status { get; set; }
            public bool authenticated { get; set; }
        }

    }

    public class WallObj
    {
        // class for parsing vk json response
        public class Response
        {
            [JsonProperty("count")]
            public string Count { get; set; }

            public class Items
            {
                [JsonProperty("id")]
                public int Id { get; set; }

                [JsonProperty("from_id")]
                public int From_id { get; set; }

                [JsonProperty("owner_id")]
                public int Owner_id { get; set; }

                [JsonProperty("date")]
                public int Date { get; set; }

                [JsonProperty("marked_as_ads")]
                public int Marked_as_ads { get; set; }

                [JsonProperty("post_type")]
                public string Post_type { get; set; }

                [JsonProperty("text")]
                public string Text { get; set; }

                public class Attachments
                {

                }
                public class Comments
                {
                    [JsonProperty("count")]
                    public int Count { get; set; }

                }
                public class Likes
                {
                    [JsonProperty("count")]
                    public int Count { get; set; }

                }
                public class Reposts
                {
                    [JsonProperty("count")]
                    public int Count { get; set; }

                }
                public class Views
                {
                    [JsonProperty("count")]
                    public int Count { get; set; }

                }

                [JsonProperty("attachments")]
                public List<Attachments> attachments { get; set; }

                [JsonProperty("comments")]
                public Comments comments { get; set; }

                [JsonProperty("likes")]
                public Likes likes { get; set; }

                [JsonProperty("reposts")]
                public Reposts reposts { get; set; }

                [JsonProperty("views")]
                public Views views { get; set; }

                [JsonProperty("edited")]
                public int Edited { get; set; }


            }

            [JsonProperty("items")]

            public List<Items> items { get; set; }
        }

        [JsonProperty("response")]
        public Response response { get; set; }

    }
    public class InstagramObj
    {
        // class for parsing instagram json
        public class Graphql
        {
            public class User
            {
                public class Edge_owner_to_timeline_media
                {

                    public class Edges
                    {
                        public class Node
                        {
                            public class Edge_media_to_comment
                            {
                                [JsonProperty("count")]
                                public int Count { get; set; }

                            }
                            public class Edge_media_to_caption
                            {
                                public class Hui
                                {
                                    public class Pizda
                                    {
                                        [JsonProperty("text")]
                                        public string Text { get; set; }

                                    }

                                    [JsonProperty("node")]
                                    public Pizda pizda { get; set; }

                                }

                                [JsonProperty("edges")]
                                public List<Hui> hui { get; set; }

                            }

                            public class Edge_liked_by
                            {
                                [JsonProperty("count")]
                                public int Count { get; set; }

                            }


                            [JsonProperty("edge_media_to_comment")]
                            public Edge_media_to_comment edge_media_to_comment { get; set; }

                            [JsonProperty("edge_media_to_caption")]
                            public Edge_media_to_caption edge_media_to_caption { get; set; }

                            [JsonProperty("edge_liked_by")]
                            public Edge_liked_by edge_liked_by { get; set; }

                            [JsonProperty("display_url")]
                            public string Display_url { get; set; }

                            [JsonProperty("media_preview")]
                            public string Media_preview { get; set; }

                            [JsonProperty("thumbnail_src")]
                            public string Thumbnail_src { get; set; }
                            [JsonProperty("shortcode")]
                            public string Shortcode { get; set; }

                            [JsonProperty("id")]
                            public string Id { get; set; }


                        }


                        [JsonProperty("node")]
                        public Node node { get; set; }


                    }

                    [JsonProperty("edges")]
                    public List<Edges> edges { get; set; }
                }
                [JsonProperty("edge_owner_to_timeline_media")]
                public Edge_owner_to_timeline_media edge_owner_to_timeline_media { get; set; }
            }

            [JsonProperty("user")]

            public User user { get; set; }
        }

        [JsonProperty("graphql")]
        public Graphql graphql { get; set; }
    }

}

