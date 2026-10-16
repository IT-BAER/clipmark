using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Clipmark.Core.Recording;

namespace Clipmark.App.Recording;

/// <summary>
/// Recording to animated GIF: the Media Foundation source reader decodes and scales to RGB32 in one step (vtable calls,
/// no WinRT collections), <see cref="GifEncoder"/> writes on a second thread. A few frame buffers stream between them.
/// </summary>
internal static partial class GifExport
{
    private const int Buffers = 4;
    private const uint FirstVideo = 0xFFFFFFFC, AllStreams = 0xFFFFFFFE, MediaSource = 0xFFFFFFFF;
    private const uint ReaderError = 0x1, EndOfStream = 0x2;
    private const ushort VT_UI8 = 21;

    private static readonly Guid AdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
    private static readonly Guid HardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid SubType = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid PdDuration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    private static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");

    /// <summary>Frame size and duration of the recording's first video stream.</summary>
    public static (int Width, int Height, TimeSpan Duration) Probe(string video)
    {
        Check(MFStartup(0x00020070, 0));
        nint reader = 0;
        try
        {
            reader = OpenReader(video);
            var (width, height) = NativeSize(reader);
            return (width, height, Duration(reader));
        }
        finally
        {
            Release(reader);
            MFShutdown();
        }
    }

    /// <summary>
    /// Writes the GIF (first <see cref="GifPlan.MaxSeconds"/> seconds); Low writes again smaller until the file fits
    /// <see cref="GifPlan.LowBudgetBytes"/>. Progress goes from 0 to 1 per attempt.
    /// </summary>
    public static async Task WriteAsync(string video, string gif, GifQuality quality, IProgress<double>? progress, CancellationToken cancel)
    {
        int fps = GifPlan.Fps(quality);
        int? edge = GifPlan.MaxEdge;
        while (edge is int maxEdge)
        {
            int written = await Task.Run(() => WriteOnce(video, gif, fps, maxEdge, progress, cancel), cancel);
            edge = quality == GifQuality.Low ? GifPlan.SmallerEdge(new FileInfo(gif).Length, written) : null;
        }
    }

    /// <summary>One pass; returns the long edge it wrote.</summary>
    private static int WriteOnce(string video, string gif, int fps, int maxEdge, IProgress<double>? progress, CancellationToken cancel)
    {
        Check(MFStartup(0x00020070, 0));
        nint reader = 0;
        try
        {
            reader = OpenReader(video);
            var (sourceWidth, sourceHeight) = NativeSize(reader);
            var (width, height) = GifPlan.Size(sourceWidth, sourceHeight, maxEdge);
            int stride = SetOutput(reader, width, height);
            int count = GifPlan.FrameCount(Duration(reader), fps);

            var full = Channel.CreateBounded<(byte[] Frame, int Frames, int DelayCs)>(Buffers);
            var free = Channel.CreateUnbounded<byte[]>();
            for (int i = 0; i < Buffers; i++)
                free.Writer.TryWrite(new byte[width * height * 4]);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            var encode = Task.Run(async () =>
            {
                try
                {
                    await EncodeAsync(gif, width, height, count, full.Reader, free.Writer, progress, stop.Token);
                }
                catch
                {
                    stop.Cancel();
                    throw;
                }
            });
            Exception? decodeError = null;
            try
            {
                Decode(reader, stride, width, height, fps, count, full.Writer, free.Reader, stop.Token);
            }
            catch (Exception e)
            {
                decodeError = e;
                stop.Cancel();
            }
            full.Writer.TryComplete();
            Exception? encodeError = null;
            try
            {
                encode.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                encodeError = e;
            }
            cancel.ThrowIfCancellationRequested();
            // A failure on one side cancels the other: report the failure, not that cancellation.
            var error = decodeError is not (null or OperationCanceledException) ? decodeError : encodeError ?? decodeError;
            if (error is not null)
                ExceptionDispatchInfo.Throw(error);
            return Math.Max(width, height);
        }
        finally
        {
            Release(reader);
            MFShutdown();
        }
    }

    private static async Task EncodeAsync(string gif, int width, int height, int count, ChannelReader<(byte[] Frame, int Frames, int DelayCs)> full,
        ChannelWriter<byte[]> free, IProgress<double>? progress, CancellationToken stop)
    {
        using var file = new FileStream(gif, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var encoder = new GifEncoder(file, width, height);
        int done = 0;
        await foreach (var (frame, frames, delay) in full.ReadAllAsync(stop))
        {
            encoder.AddFrame(frame, delay);
            free.TryWrite(frame);
            progress?.Report((done += frames) / (double)count);
        }
    }

    /// <summary>Frame k shows the sample on screen at k / fps; one sample covering several frames goes once with their delays added.</summary>
    private static unsafe void Decode(nint reader, int stride, int width, int height, int fps, int count,
        ChannelWriter<(byte[] Frame, int Frames, int DelayCs)> full, ChannelReader<byte[]> free, CancellationToken stop)
    {
        var vt = *(nint**)reader;
        int k = 0;
        while (k < count)
        {
            stop.ThrowIfCancellationRequested();
            uint index, flags;
            long time;
            nint sample;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)vt[9])(reader, FirstVideo, 0, &index, &flags, &time, &sample));
            if ((flags & ReaderError) != 0)
            {
                Release(sample);
                throw new COMException("The video could not be read.");
            }
            if ((flags & EndOfStream) != 0)
            {
                Release(sample);
                break;
            }
            if (sample == 0)
                continue;
            try
            {
                long duration;
                if (((delegate* unmanaged[Stdcall]<nint, long*, int>)(*(nint**)sample)[37])(sample, &duration) < 0 || duration <= 0)
                    duration = TimeSpan.TicksPerSecond / 30;
                int first = k, delay = 0;
                while (k < count && GifPlan.FrameTime(k, fps).Ticks < time + duration)
                    delay += GifPlan.DelayCs(k++, fps);
                if (k == first)
                    continue;
                var frame = free.ReadAsync(stop).AsTask().GetAwaiter().GetResult();
                CopyFrame(sample, frame, stride, width, height);
                full.WriteAsync((frame, k - first, delay), stop).AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                Release(sample);
            }
        }
    }

    /// <summary>Copies the RGB32 sample top-down into <paramref name="frame"/>; a negative stride is a bottom-up image.</summary>
    private static unsafe void CopyFrame(nint sample, byte[] frame, int stride, int width, int height)
    {
        nint buffer;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)sample)[41])(sample, &buffer)); // ConvertToContiguousBuffer
        try
        {
            var vt = *(nint**)buffer;
            byte* bits;
            uint max, length;
            Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)vt[3])(buffer, &bits, &max, &length));
            try
            {
                int row = width * 4, pitch = Math.Abs(stride);
                if ((long)pitch * (height - 1) + row > length)
                    throw new COMException("The video frame is smaller than expected.");
                for (int y = 0; y < height; y++)
                    new ReadOnlySpan<byte>(bits + (long)(stride > 0 ? y : height - 1 - y) * pitch, row).CopyTo(frame.AsSpan(y * row));
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<nint, int>)vt[4])(buffer);
            }
        }
        finally
        {
            Release(buffer);
        }
    }

    private static unsafe nint OpenReader(string video)
    {
        nint attributes;
        Check(MFCreateAttributes(&attributes, 2));
        try
        {
            Check(SetUInt32(attributes, AdvancedVideoProcessing, 1));
            Check(SetUInt32(attributes, HardwareTransforms, 1));
            nint reader;
            Check(MFCreateSourceReaderFromURL(video, attributes, &reader));
            var vt = *(nint**)reader;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int, int>)vt[4])(reader, AllStreams, 0));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, int, int>)vt[4])(reader, FirstVideo, 1));
            return reader;
        }
        finally
        {
            Release(attributes);
        }
    }

    private static unsafe (int Width, int Height) NativeSize(nint reader)
    {
        nint type;
        Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, int>)(*(nint**)reader)[5])(reader, FirstVideo, 0, &type));
        try
        {
            ulong size;
            Check(GetUInt64(type, FrameSize, &size));
            return ((int)(size >> 32), (int)(uint)size);
        }
        finally
        {
            Release(type);
        }
    }

    /// <summary>Asks for RGB32 at the given size (the advanced video processor scales); returns the output stride.</summary>
    private static unsafe int SetOutput(nint reader, int width, int height)
    {
        var vt = *(nint**)reader;
        nint type;
        Check(MFCreateMediaType(&type));
        try
        {
            Check(SetGuid(type, MajorType, Video));
            Check(SetGuid(type, SubType, Rgb32));
            Check(SetUInt64(type, FrameSize, (ulong)width << 32 | (uint)height));
            Check(((delegate* unmanaged[Stdcall]<nint, uint, uint*, nint, int>)vt[7])(reader, FirstVideo, null, type));
        }
        finally
        {
            Release(type);
        }
        Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)vt[6])(reader, FirstVideo, &type));
        try
        {
            ulong size;
            Check(GetUInt64(type, FrameSize, &size));
            if ((int)(size >> 32) != width || (int)(uint)size != height)
                throw new COMException($"The video was decoded at {size >> 32} x {(uint)size} instead of {width} x {height}.");
            uint stride;
            return GetUInt32(type, DefaultStride, &stride) >= 0 ? (int)stride : width * 4;
        }
        finally
        {
            Release(type);
        }
    }

    private static unsafe TimeSpan Duration(nint reader)
    {
        PropVariant value;
        var key = PdDuration;
        Check(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, PropVariant*, int>)(*(nint**)reader)[12])(reader, MediaSource, &key, &value));
        if (value.Type != VT_UI8)
            throw new COMException("The video has no duration.");
        return TimeSpan.FromTicks(value.Value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        private ushort _reserved1, _reserved2, _reserved3;
        public long Value;
        private nint _padding;
    }

    private static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    private static unsafe void Release(nint unknown)
    {
        if (unknown != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)unknown)[2])(unknown);
    }

    // IMFAttributes vtable slots.
    private static unsafe int GetUInt32(nint p, Guid key, uint* value) => ((delegate* unmanaged[Stdcall]<nint, Guid*, uint*, int>)(*(nint**)p)[7])(p, &key, value);
    private static unsafe int GetUInt64(nint p, Guid key, ulong* value) => ((delegate* unmanaged[Stdcall]<nint, Guid*, ulong*, int>)(*(nint**)p)[8])(p, &key, value);
    private static unsafe int SetUInt32(nint p, Guid key, uint value) => ((delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)(*(nint**)p)[21])(p, &key, value);
    private static unsafe int SetUInt64(nint p, Guid key, ulong value) => ((delegate* unmanaged[Stdcall]<nint, Guid*, ulong, int>)(*(nint**)p)[22])(p, &key, value);
    private static unsafe int SetGuid(nint p, Guid key, Guid value) => ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)(*(nint**)p)[24])(p, &key, &value);

    [LibraryImport("mfplat.dll")] private static partial int MFStartup(uint version, uint flags);
    [LibraryImport("mfplat.dll")] private static partial int MFShutdown();
    [LibraryImport("mfplat.dll")] private static unsafe partial int MFCreateAttributes(nint* attributes, uint size);
    [LibraryImport("mfplat.dll")] private static unsafe partial int MFCreateMediaType(nint* type);
    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int MFCreateSourceReaderFromURL(string url, nint attributes, nint* reader);
}
