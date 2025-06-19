using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

// 简化版HTTP服务器
public class SimpleHttpServer
{
    private HttpListener _listener;
    private bool _isRunning;
    private IWavePlayer _waveOut;
    private AudioFileReader _audioFile;
    private string _currentSong;
    private List<string> _musicLibrary = new List<string>();

    public SimpleHttpServer(int port)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:8080/");
        ScanMusicLibrary();
    }

    private void ScanMusicLibrary()
    {
        // 扫描音乐文件夹，这里假设放在程序目录下的Music文件夹
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
                    Console.WriteLine("connect");
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
            // 添加静态文件服务
            if (path == "/" || path == "/index.html")
            {
                ServeFile(response, "./wwwroot/index.html", "text/html");
                return;
            }
            if (path == "/play" && method == "POST")
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
        StopPlayback(); // 停止当前播放

        if (File.Exists(songPath))
        {
            _audioFile = new AudioFileReader(songPath);
            _waveOut = new WaveOutEvent();
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

    private void WriteResponse(HttpListenerResponse response, string message, int statusCode = 200)
    {
        response.StatusCode = statusCode;
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

// 主程序
class Program
{
    static void Main(string[] args)
    {
        int port = 1000; // 可根据需要修改端口
        var server = new SimpleHttpServer(port);

        Console.WriteLine($"Starting server on port {port}");
        Console.WriteLine("Access the web interface from any device on the same network");

        server.Start();

        Console.WriteLine("Press any key to stop...");
        Console.ReadKey();

        server.Stop();
    }
}
