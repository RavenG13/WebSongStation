using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Net;
using System.Text.Json;

public class SimpleHttpServer
{
    private HttpListener _listener;
    private bool _isRunning;
    private IWavePlayer _waveOut;
    private AudioFileReader _audioFile;
    private string _currentSong;
    private List<string> _musicLibrary = new List<string>();
    private float _volume = 0.5f;
    private int _selectedDevice = -1; // -1 表示默认设备

    public SimpleHttpServer(int port)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:8080/");
        ScanMusicLibrary();
    
    }

    private void ScanMusicLibrary()
    {
        string musicPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "music");
        if (Directory.Exists(musicPath))
        {
            _musicLibrary.AddRange(Directory.GetFiles(musicPath, "*.mp3"));
            _musicLibrary.AddRange(Directory.GetFiles(musicPath, "*.wav"));
        }
    }

    public void Start()
    {
        _isRunning = true;
        _listener.Start();
        ThreadPool.QueueUserWorkItem((o) =>
        {
            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    Console.WriteLine(context.ToString());
                    ProcessRequest(context);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        });
    }

    private void ServeFile(HttpListenerResponse response, string filePath, string contentType)
    {
        if (File.Exists(filePath))
        {
            response.ContentType = contentType;
            byte[] buffer = File.ReadAllBytes(filePath);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        else
        {
            WriteResponse(response, "File not found", 404);
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            string path = request.Url.LocalPath.ToLower();
            string method = request.HttpMethod;

            if (path == "/" || path == "/index.html")
            {
                ServeFile(response, "./wwwroot/index.html", "text/html");
                return;
            }
            else if (path == "/play" && method == "POST")
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    string songName = reader.ReadToEnd();
                    PlaySong(songName);
                    WriteResponse(response, "Playing: " + songName);
                }
            }
            else if (path == "/stop" && method == "GET")
            {
                StopPlayback();
                WriteResponse(response, "Playback stopped");
            }
            else if (path == "/pause" && method == "GET")
            {
                PausePlayback();
                WriteResponse(response, "Playback paused");
            }
            else if (path == "/resume" && method == "GET")
            {
                ResumePlayback();
                WriteResponse(response, "Playback resumed");
            }
            else if (path == "/list" && method == "GET")
            {
                string songs = string.Join("\n", _musicLibrary);
                WriteResponse(response, songs);
            }
            else if (path == "/devices" && method == "GET")
            {
                var enumerator = new MMDeviceEnumerator();
                var audioEndPoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                
                var i = audioEndPoints.Count;
                var devices = Enumerable.Range(-1, i)
                    .Select(i => new
                    {
                        id = i,
                        name = i == -1 ? "默认设备" : audioEndPoints[i].FriendlyName
                    });

                var json = JsonSerializer.Serialize(devices);
                WriteResponse(response, json, contentType: "application/json");
            }
            else if (path == "/setdevice" && method == "POST")
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    var json = reader.ReadToEnd();
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    _selectedDevice = int.Parse(data["deviceId"]);  // 手动转换为 int

                    StopPlayback();

                    WriteResponse(response, $"音频设备已切换到: {_selectedDevice}");
                }
            }
            else if (path == "/volume" && method == "POST")
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    var json = reader.ReadToEnd();
                    //var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    _volume = float.Parse(data["volume"])/100.0f;  // 手动转换为 int
                    //_volume = data["volume"] / 100.0f;

                    if (_audioFile != null)
                    {
                        _audioFile.Volume = _volume;
                    }

                    WriteResponse(response, $"音量已设置为: {data["volume"]}%");
                }
            }
            else
            {
                WriteResponse(response, "Invalid command", 404);
            }
        }
        catch (Exception ex)
        {
            WriteResponse(response, $"Error: {ex.Message}", 500);
        }
        finally
        {
            response.Close();
        }
    }

    private void PlaySong(string songPath)
    {
        StopPlayback();
        _waveOut = null;
        if (File.Exists(songPath))
        {
            _audioFile = new AudioFileReader(songPath);
            _audioFile.Volume = _volume;

            var enumerator = new MMDeviceEnumerator();
            var audioEndPoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            if (_selectedDevice == -1)
            {
                _waveOut = new WasapiOut();
            }
            else
            {
                _waveOut = new WasapiOut(device: audioEndPoints[_selectedDevice], AudioClientShareMode.Shared, false, 100);
            }
            
            _waveOut.Init(_audioFile);
            _waveOut.Play();
            _currentSong = songPath;
        }
    }

    private void StopPlayback()
    {
        _waveOut?.Stop();
        _audioFile?.Dispose();
        _waveOut?.Dispose();
        _currentSong = null;
    }

    private void PausePlayback()
    {
        _waveOut?.Pause();
    }

    private void ResumePlayback()
    {
        _waveOut?.Play();
    }

    private void WriteResponse(HttpListenerResponse response, string message, int statusCode = 200, string contentType = "text/plain")
    {
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(message);
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
    }

    public void Stop()
    {
        _isRunning = false;
        _listener.Stop();
        StopPlayback();
    }
}

class Program
{
    static void Main(string[] args)
    {
        int port = 1000;
        var server = new SimpleHttpServer(port);

        Console.WriteLine($"Starting server on port {port}");
        Console.WriteLine("Access the web interface from any device on the same network");

        server.Start();

        Console.WriteLine("Press any key to stop...");
        Console.ReadKey();

        server.Stop();
    }
}
