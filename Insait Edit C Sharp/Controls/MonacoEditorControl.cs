using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Insait_Edit_C_Sharp.Controls;

/// <summary>
/// Wrapper control for WebView2 Monaco Editor in Avalonia
/// </summary>
public class MonacoEditorControl : NativeControlHost
{
    private WebView2? _webView;
    private bool _isInitialized;
    private string _pendingContent = string.Empty;
    private string _pendingLanguage = "csharp";
    private TaskCompletionSource<bool>? _initializationTask;
    private string _currentContent = string.Empty;
    private bool _isDirty;

    public event EventHandler? EditorReady;
    public event EventHandler? ContentChanged;
    public event EventHandler<ContentChangedEventArgs>? ContentChangedWithValue;
    public event EventHandler<CursorPositionChangedEventArgs>? CursorPositionChanged;

    public bool IsEditorReady => _isInitialized;
    public bool IsDirty => _isDirty;
    public string CurrentContent => _currentContent;

    public MonacoEditorControl()
    {
        _initializationTask = new TaskCompletionSource<bool>();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };

        _ = InitializeWebViewAsync();

        var handle = _webView.Handle;
        return new PlatformHandle(handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_webView != null)
        {
            _webView.Dispose();
            _webView = null;
        }
        base.DestroyNativeControlCore(control);
    }

    private async Task InitializeWebViewAsync()
    {
        if (_webView == null) return;

        try
        {
            // Initialize WebView2 with default environment
            var env = await CoreWebView2Environment.CreateAsync();
            await _webView.EnsureCoreWebView2Async(env);

            // Configure WebView2 settings
            _webView.CoreWebView2.Settings.IsScriptEnabled = true;
            _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            // Handle messages from JavaScript
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // Load Monaco Editor HTML
            var monacoHtmlPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, 
                "Monaco", 
                "monaco-editor.html");

            if (System.IO.File.Exists(monacoHtmlPath))
            {
                _webView.CoreWebView2.Navigate($"file:///{monacoHtmlPath.Replace('\\', '/')}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Monaco HTML not found at: {monacoHtmlPath}");
                _initializationTask?.TrySetException(new System.IO.FileNotFoundException("Monaco HTML file not found", monacoHtmlPath));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 initialization error: {ex.Message}");
            _initializationTask?.TrySetException(ex);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;

            if (root.TryGetProperty("type", out var typeElement))
            {
                var type = typeElement.GetString();

                switch (type)
                {
                    case "editorReady":
                        _isInitialized = true;
                        _initializationTask?.TrySetResult(true);
                        
                        // Set pending content if any
                        if (!string.IsNullOrEmpty(_pendingContent))
                        {
                            SetContent(_pendingContent, _pendingLanguage);
                            _pendingContent = string.Empty;
                        }
                        
                        EditorReady?.Invoke(this, EventArgs.Empty);
                        break;

                    case "contentChanged":
                        _isDirty = true;
                        if (root.TryGetProperty("content", out var contentElement))
                        {
                            _currentContent = contentElement.GetString() ?? string.Empty;
                            ContentChangedWithValue?.Invoke(this, new ContentChangedEventArgs(_currentContent));
                        }
                        ContentChanged?.Invoke(this, EventArgs.Empty);
                        break;

                    case "cursorChanged":
                        if (root.TryGetProperty("line", out var lineElement) &&
                            root.TryGetProperty("column", out var colElement))
                        {
                            var line = lineElement.GetInt32();
                            var column = colElement.GetInt32();
                            CursorPositionChanged?.Invoke(this, new CursorPositionChangedEventArgs(line, column));
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing web message: {ex.Message}");
        }
    }

    public async Task WaitForInitializationAsync()
    {
        if (_initializationTask != null)
        {
            await _initializationTask.Task;
        }
    }

    public void SetContent(string content, string language = "csharp")
    {
        if (!_isInitialized)
        {
            _pendingContent = content;
            _pendingLanguage = language;
            return;
        }

        _currentContent = content;
        _isDirty = false;

        if (_webView?.CoreWebView2 != null)
        {
            var escapedContent = System.Text.Json.JsonSerializer.Serialize(content);
            var script = $"setContent({escapedContent}, '{language}');";
            _ = _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    public async Task<string> GetContentAsync()
    {
        if (_webView?.CoreWebView2 == null || !_isInitialized)
            return _currentContent;

        var result = await _webView.CoreWebView2.ExecuteScriptAsync("getContent();");
        
        if (!string.IsNullOrEmpty(result) && result != "null")
        {
            _currentContent = System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            return _currentContent;
        }
        
        return _currentContent;
    }

    /// <summary>
    /// Marks the content as saved (not dirty)
    /// </summary>
    public void MarkAsSaved()
    {
        _isDirty = false;
    }

    public void SetLanguage(string language)
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            var script = $"setLanguage('{language}');";
            _ = _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    public void GoToLine(int lineNumber)
    {
        GoToLine(lineNumber, 1);
    }

    public void GoToLine(int lineNumber, int column)
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            var script = $"goToLine({lineNumber}, {column});";
            _ = _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    public void FormatDocument()
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("formatDocument();");
        }
    }

    public void Undo()
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("undo();");
        }
    }

    public void Redo()
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("redo();");
        }
    }

    public void Find()
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("find();");
        }
    }

    public void Replace()
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("replace();");
        }
    }

    public void SetReadOnly(bool readOnly)
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            var script = $"setReadOnly({readOnly.ToString().ToLower()});";
            _ = _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    public void SetFontSize(int size)
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            var script = $"setFontSize({size});";
            _ = _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
    }

    public void FocusEditor()
    {
        if (_webView?.CoreWebView2 != null && _isInitialized)
        {
            _webView.Focus();
            _ = _webView.CoreWebView2.ExecuteScriptAsync("focus();");
        }
    }
}

public class CursorPositionChangedEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public CursorPositionChangedEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public class ContentChangedEventArgs : EventArgs
{
    public string NewContent { get; }

    public ContentChangedEventArgs(string newContent)
    {
        NewContent = newContent;
    }
}

/// <summary>
/// Platform handle wrapper for NativeControlHost
/// </summary>
internal class PlatformHandle : IPlatformHandle
{
    public IntPtr Handle { get; }
    public string HandleDescriptor { get; }

    public PlatformHandle(IntPtr handle, string descriptor)
    {
        Handle = handle;
        HandleDescriptor = descriptor;
    }
}