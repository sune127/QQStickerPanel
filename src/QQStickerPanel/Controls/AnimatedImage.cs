using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QQStickerPanel.Controls;

public sealed class AnimatedImage : Image
{
    private readonly List<BitmapFrame> _frames = [];
    private CancellationTokenSource? _loadCancellation;
    private int _frameIndex;
    private TimeSpan _accumulatedTime = TimeSpan.Zero;
    private bool _isRendering;

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath),
        typeof(string),
        typeof(AnimatedImage),
        new PropertyMetadata(string.Empty, OnPlaybackSourceChanged));

    public static readonly DependencyProperty IsAnimationEnabledProperty = DependencyProperty.Register(
        nameof(IsAnimationEnabled),
        typeof(bool),
        typeof(AnimatedImage),
        new PropertyMetadata(false, OnPlaybackSourceChanged));

    public AnimatedImage()
    {
        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += OnUnloaded;
    }

    public string SourcePath
    {
        get => (string)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    private static void OnPlaybackSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var animatedImage = (AnimatedImage)dependencyObject;
        if (!animatedImage.IsAnimationEnabled)
        {
            animatedImage.ClearFrames();
            return;
        }

        animatedImage.LoadFrames(animatedImage.SourcePath);
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateRenderingState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ClearFrames();
    }

    private async void LoadFrames(string? filePath)
    {
        _loadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        ResetFrames();
        Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            FinishLoad(cancellation);
            return;
        }

        try
        {
            var frames = await Task.Run(() => DecodeFrames(filePath, cancellation.Token), cancellation.Token);
            if (_loadCancellation != cancellation || cancellation.IsCancellationRequested)
            {
                return;
            }

            if (frames.Count == 0)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            _frames.AddRange(frames);
            Source = _frames[0];
            Visibility = Visibility.Visible;
            UpdateRenderingState();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            if (_loadCancellation == cancellation)
            {
                Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            FinishLoad(cancellation);
        }
    }

    private static List<BitmapFrame> DecodeFrames(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(filePath);
        var decoder = GifBitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frames = new List<BitmapFrame>(decoder.Frames.Count);
        foreach (var frame in decoder.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            frame.Freeze();
            frames.Add(frame);
        }

        return frames;
    }

    private void ClearFrames()
    {
        _loadCancellation?.Cancel();
        ResetFrames();
        Visibility = Visibility.Collapsed;
    }

    private void ResetFrames()
    {
        StopRendering();
        _frames.Clear();
        _frameIndex = 0;
        _accumulatedTime = TimeSpan.Zero;
        Source = null;
    }

    private void FinishLoad(CancellationTokenSource cancellation)
    {
        if (_loadCancellation != cancellation)
        {
            return;
        }

        _loadCancellation = null;
        cancellation.Dispose();
    }

    private void UpdateRenderingState()
    {
        if (IsVisible && _frames.Count > 1)
        {
            StartRendering();
            return;
        }

        StopRendering();
    }

    private void StartRendering()
    {
        if (_isRendering)
        {
            return;
        }

        _isRendering = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_isRendering)
        {
            return;
        }

        _isRendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_frames.Count <= 1 || !IsVisible)
        {
            StopRendering();
            return;
        }

        _accumulatedTime += TimeSpan.FromMilliseconds(16);
        if (_accumulatedTime < TimeSpan.FromMilliseconds(100))
        {
            return;
        }

        _accumulatedTime = TimeSpan.Zero;
        _frameIndex = (_frameIndex + 1) % _frames.Count;
        Source = _frames[_frameIndex];
    }
}
