using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class SimpleApiServer : IDisposable
{
    string blogPath = @"";
    string assetsPath = @"";

    readonly HttpListener _listener;
    readonly CancellationTokenSource _cts = new();
    Task? _listenTask;
    readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SimpleApiServer(string prefix)
    {
        DotNetEnv.Env.Load();
        
        blogPath = Environment.GetEnvironmentVariable("BLOG_PATH") ?? "";
        if (string.IsNullOrWhiteSpace(blogPath)){}

        assetsPath = Environment.GetEnvironmentVariable("ASSETS_PATH") ?? "";
        if (string.IsNullOrWhiteSpace(assetsPath)){}

        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
    }

    public void Start()
    {
        _listener.Start();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch { }
        _listenTask?.Wait(2000);
    }

    async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleContextAsync(ctx), ct); // fire-and-forget per request
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                // Listener stopped
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // ログ出力など必要に応じて
                Console.WriteLine("ListenLoop exception: " + ex);
            }
        }
    }

    async Task WriteFileAsync(HttpListenerResponse res, string filePath)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        res.ContentLength64 = fileBytes.Length;
        await res.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length).ConfigureAwait(false);
    }

    async Task HandleContextAsync(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;
        res.ContentType = "application/json; charset=utf-8";
        res.AddHeader("Access-Control-Allow-Origin", "*");

        try
        {
            string path = req.Url?.AbsolutePath ?? "/";

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = (int)HttpStatusCode.OK;
                res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                res.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                res.Close();
                return;
            }

            if (req.HttpMethod == "POST")
            {
                if (path.Equals("/save", StringComparison.OrdinalIgnoreCase))
                {
                    res.StatusCode = (int)HttpStatusCode.OK;
                    using StreamReader reader = new(req.InputStream, Encoding.UTF8);
                    string body = await reader.ReadToEndAsync().ConfigureAwait(false);

                    SaveRequest? saveData = JsonSerializer.Deserialize<SaveRequest>(body);
                    if (saveData != null && saveData?.FileName != null)
                    {
                        string filePath = Path.Join(blogPath, saveData.FileName + ".md");
                        string md = $"---\ntitle: {saveData.Title}\nauthors: {saveData.Author}\n---\n" + saveData.Content ?? string.Empty;
                        File.WriteAllText(filePath, md);
                        res.StatusCode = (int)HttpStatusCode.OK;
                        await WriteJsonAsync(res, new { message = "File saved successfully", fileName = saveData.FileName, status = "success" });
                        return;
                    }

                    res.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteJsonAsync(res, new { error = "Invalid save request", detail = "Missing or invalid fileName" });
                    return;
                }

                if (path.Equals("/upload", StringComparison.OrdinalIgnoreCase))
                {
                    res.StatusCode = (int)HttpStatusCode.OK;
                    using StreamReader reader = new(req.InputStream, Encoding.UTF8);
                    string body = await reader.ReadToEndAsync().ConfigureAwait(false);

                    SaveFileRequest? saveData = JsonSerializer.Deserialize<SaveFileRequest>(body);
                    try
                    {
                        if (saveData != null && saveData?.FileName != null)
                        {
                            if (string.IsNullOrWhiteSpace(saveData.BlogFileName))
                            {
                                res.StatusCode = (int)HttpStatusCode.BadRequest;
                                await WriteJsonAsync(res, new { error = "Invalid save request", detail = "Missing or invalid blogFileName" });
                                return;
                            }
                            string dirPath = Path.Join(assetsPath, "img", "blog", saveData.BlogFileName);
                            Directory.CreateDirectory(dirPath);
                            string filePath = Path.Join(dirPath, saveData.FileName);
                            saveData.Content = Convert.FromBase64String(saveData.Base64Content);
                            File.WriteAllBytes(filePath, saveData.Content);
                            res.StatusCode = (int)HttpStatusCode.OK;
                            await WriteJsonAsync(res, new { message = "File saved successfully", fileName = saveData.FileName, status = "success" });
                            return;
                        }
                    }
                    catch(Exception ex)
                    {
                        res.StatusCode = (int)HttpStatusCode.InternalServerError;
                        await WriteJsonAsync(res, new { error = "Failed to save file", detail = ex.Message, status = "error" });
                        return;
                    }

                    res.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteJsonAsync(res, new { error = "Invalid save request", detail = "Missing or invalid fileName" });
                    return;
                }
            }

            if (req.HttpMethod == "DELETE")
            {
                if (path.Equals("/delete", StringComparison.OrdinalIgnoreCase))
                {
                    res.StatusCode = (int)HttpStatusCode.OK;
                    using StreamReader reader = new(req.InputStream, Encoding.UTF8);
                    string body = await reader.ReadToEndAsync().ConfigureAwait(false);

                    SaveRequest? saveData = JsonSerializer.Deserialize<SaveRequest>(body);
                    if (saveData != null && saveData?.FileName != null)
                    {
                        string filePath = Path.Join(blogPath, saveData.FileName + ".md");
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                File.Delete(filePath);
                                res.StatusCode = (int)HttpStatusCode.OK;
                                await WriteJsonAsync(res, new { message = "File deleted successfully", fileName = saveData.FileName, status = "OK" });
                                return;
                            }
                            catch (Exception ex)
                            {
                                res.StatusCode = (int)HttpStatusCode.InternalServerError;
                                await WriteJsonAsync(res, new { error = "Failed to delete file", detail = ex.Message, status = "error" });
                                return;
                            }
                        }
                        else
                        {
                            res.StatusCode = (int)HttpStatusCode.NotFound;
                            await WriteJsonAsync(res, new { error = "File not found", fileName = saveData.FileName, status = "error" });
                            return;
                        }
                    }

                    res.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteJsonAsync(res, new { error = "Invalid save request", detail = "Missing or invalid fileName" });
                    return;
                }
            }

            if (req.HttpMethod != "GET")
            {
                res.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                await WriteJsonAsync(res, new { error = "Method not allowed" });
                return;
            }

            if (path.Equals("/", StringComparison.OrdinalIgnoreCase))
            {
                string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", "index.html");
                res.ContentType = "text/html; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, home);
                return;
            }
            
            if (path.Equals("/styles.css", StringComparison.OrdinalIgnoreCase))
            {
                string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", "styles.css");
                res.ContentType = "text/css; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, home);
                return;
            }

            if (path.Equals("/app.js", StringComparison.OrdinalIgnoreCase))
            {
                string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", "app.js");
                res.ContentType = "application/javascript; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, home);
                return;
            }

            if (path.Equals("/list", StringComparison.OrdinalIgnoreCase))
            {
                // ダミーデータを返す
                List<object> list = GetMarkdownFileList(blogPath);
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteJsonAsync(res, list);
                return;
            }

            if (path.Equals("/image", StringComparison.OrdinalIgnoreCase))
            {
                // ダミーデータを返す
                string? filePath = req.QueryString["path"];
                string? fileFullPath = Path.Join(assetsPath, filePath);
                if (string.IsNullOrWhiteSpace(fileFullPath) || !File.Exists(fileFullPath))
                {
                    res.StatusCode = (int)HttpStatusCode.NotFound;
                    await WriteJsonAsync(res, new { error = "File not found", path = fileFullPath });
                    return;
                }
                res.StatusCode = (int)HttpStatusCode.OK;
                res.ContentType = GetContentType(fileFullPath);
                await WriteFileAsync(res, fileFullPath);
                return;
            }

            if (path.Equals("/file", StringComparison.OrdinalIgnoreCase))
            {
                // クエリパラメータ ?path=... を取得
                string? filePath = req.QueryString["path"];
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    res.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteJsonAsync(res, new { error = "Missing 'path' query parameter" });
                    return;
                }

                // URL エンコードされているのでデコード（HttpListener が既にデコードしていることが多いですが念のため）
                filePath = WebUtility.UrlDecode(filePath);

                // ファイル情報取得
                if (!File.Exists(filePath))
                {
                    res.StatusCode = (int)HttpStatusCode.NotFound;
                    await WriteJsonAsync(res, new { error = "File not found", path = filePath });
                    return;
                }

                object info = BuildFileInfoObject(filePath);
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteJsonAsync(res, info);
                return;
            }

            // 未定義パス
            res.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteJsonAsync(res, new { error = "Not found" });
        }
        catch (Exception ex)
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(res, new { error = "Internal server error", detail = ex.Message });
        }
        finally
        {
            try { res.Close(); } catch { }
        }
    }

    string GetContentType(string fileFullPath)
    {
        string extension = Path.GetExtension(fileFullPath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }

    async Task WriteJsonAsync(HttpListenerResponse res, object obj)
    {
        string json = JsonSerializer.Serialize(obj, _jsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    object BuildFileInfoObject(string fullPath)
    {
        FileInfo fi = new(fullPath);

        // name: file name without extension
        string name = Path.GetFileNameWithoutExtension(fi.Name);
        // title: uppercase, hyphen -> space (例に合わせる)
        string title = name.Replace('-', ' ').ToUpperInvariant();
        // relative path: 相対パス（実行ディレクトリに対する）
        string rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath).Replace('\\', '/');

        string content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;

        // createdAt/updatedAt: DateTimeOffset にして ISO8601 (JsonSerializer が "o" 形式で出す)
        DateTimeOffset createdAt = new(fi.CreationTime);
        DateTimeOffset updatedAt = new(fi.LastWriteTime);

        return new
        {
            fullPath = fullPath,
            relativePath = rel,
            fileName = fi.Name,
            name = name,
            title = title,
            extension = fi.Extension,
            createdAt = createdAt,   // シリアライズ時に ISO8601 になる
            updatedAt = updatedAt,
            content = content
        };
    }

    List<object> GetMarkdownFileList(string rootDirectory)
    {
        List<object> result = [];

        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return result;
        }

        // 再帰的に .md と .mdx を列挙
        HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".mdx" };

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories)
                            .Where(f => extensions.Contains(Path.GetExtension(f)));
        }
        catch (Exception)
        {
            // アクセス権などで列挙に失敗した場合は空リストを返す
            return result;
        }

        foreach (var fullPath in files)
        {
            try
            {
                FileInfo fi = new(fullPath);

                string relative = Path.GetRelativePath(rootDirectory, fullPath).Replace('\\', '/');
                string fileName = fi.Name;
                string name = Path.GetFileNameWithoutExtension(fi.Name);
                string title = name.Replace('-', ' ').ToUpperInvariant();
                string extension = fi.Extension;

                // UTC 時刻で返す（シリアライズ時に ISO8601）
                DateTimeOffset createdAt = new(fi.CreationTimeUtc);
                DateTimeOffset updatedAt = new(fi.LastWriteTimeUtc);

                result.Add(new
                {
                    fullPath = fullPath,
                    relativePath = relative,
                    fileName = fileName,
                    name = name,
                    title = title,
                    extension = extension,
                    createdAt = createdAt,
                    updatedAt = updatedAt
                });
            }
            catch
            {
                // ファイルが消えた／アクセス不可になったときはスキップ
                continue;
            }
        }

        return result;
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts.Dispose();
    }
}

class SaveRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

class SaveFileRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public byte[] Content { get; set; } = [];

    [System.Text.Json.Serialization.JsonPropertyName("base64Content")]
    public string Base64Content { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("blogFileName")]
    public string BlogFileName { get; set; } = "";
}