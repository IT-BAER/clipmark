using System.Runtime.InteropServices;
using Clipmark.Core.Recording;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Recording;

/// <summary>
/// WASAPI shared-mode capture through raw COM vtables: the default render device in loopback (system audio) or the
/// default capture device (microphone). Create, read and dispose on one MTA thread.
/// </summary>
internal sealed unsafe class WasapiCapture : IDisposable
{
    private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EnumeratorIid = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid CaptureClientIid = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00AA00389B71");

    private const uint StreamFlagsLoopback = 0x00020000, BufferFlagsSilent = 0x2;
    private const int NotFound = unchecked((int)0x80070490);

    private nint _client, _capture;

    public int Channels { get; }
    public int SampleRate { get; }
    private readonly int _bits, _blockAlign;
    private readonly bool _float;

    private WasapiCapture(nint client, nint capture, int channels, int rate, int bits, int blockAlign, bool isFloat)
    {
        (_client, _capture, Channels, SampleRate, _bits, _blockAlign, _float) = (client, capture, channels, rate, bits, blockAlign, isFloat);
    }

    private static T Slot<T>(nint obj, int index) where T : unmanaged => ((T*)*(void**)obj)[index];

    /// <summary>Opens the device, or returns null with a user-facing reason (no such device, unsupported format).</summary>
    public static WasapiCapture? TryOpen(bool loopback, out string? error)
    {
        error = null;
        nint enumerator = 0, device = 0, client = 0, capture = 0, format = 0;
        try
        {
            Guid clsid = EnumeratorClsid, iid = EnumeratorIid;
            Marshal.ThrowExceptionForHR(CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iid, &enumerator));
            // IMMDeviceEnumerator 4 GetDefaultAudioEndpoint(EDataFlow, ERole, IMMDevice**): eRender 0 / eCapture 1, eConsole 0
            int hr = ((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Slot<nint>(enumerator, 4))(enumerator, loopback ? 0 : 1, 0, &device);
            if (hr == NotFound)
            {
                error = Loc.Get(loopback ? "AudioNoSpeakers" : "AudioNoMicrophone");
                return null;
            }
            Marshal.ThrowExceptionForHR(hr);
            // IMMDevice 3 Activate(REFIID, DWORD, PROPVARIANT*, void**)
            Guid clientIid = AudioClientIid;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Slot<nint>(device, 3))(device, &clientIid, CLSCTX_ALL, 0, &client));
            // IAudioClient 8 GetMixFormat(WAVEFORMATEX**)
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot<nint>(client, 8))(client, &format));
            byte* f = (byte*)format;
            ushort tag = *(ushort*)f;
            int channels = *(ushort*)(f + 2), rate = *(int*)(f + 4), blockAlign = *(ushort*)(f + 12), bits = *(ushort*)(f + 14);
            bool isFloat = tag == 3 || (tag == 0xFFFE && *(Guid*)(f + 24) == FloatSubtype);
            if (!(isFloat ? bits == 32 : bits is 16 or 24 or 32))
            {
                error = Loc.Format("AudioUnsupportedFormat", bits);
                return null;
            }
            // IAudioClient 3 Initialize(shareMode, streamFlags, bufferDuration, periodicity, format, sessionGuid): 200 ms buffer
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int, uint, long, long, nint, nint, int>)Slot<nint>(client, 3))(
                client, 0, loopback ? StreamFlagsLoopback : 0, 2_000_000, 0, format, 0));
            // IAudioClient 14 GetService(REFIID, void**)
            Guid captureIid = CaptureClientIid;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot<nint>(client, 14))(client, &captureIid, &capture));
            var result = new WasapiCapture(client, capture, channels, rate, bits, blockAlign, isFloat);
            client = capture = 0; // owned by result
            return result;
        }
        catch (Exception e)
        {
            error = Loc.Format(loopback ? "AudioSystemFailed" : "AudioMicrophoneFailed", e.Message);
            return null;
        }
        finally
        {
            if (format != 0) CoTaskMemFree(format);
            if (capture != 0) Marshal.Release(capture);
            if (client != 0) Marshal.Release(client);
            if (device != 0) Marshal.Release(device);
            if (enumerator != 0) Marshal.Release(enumerator);
        }
    }

    // IAudioClient 10 Start, 11 Stop
    public void Start() => Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int>)Slot<nint>(_client, 10))(_client));

    public void Stop()
    {
        if (_client != 0)
            ((delegate* unmanaged[Stdcall]<nint, int>)Slot<nint>(_client, 11))(_client);
    }

    /// <summary>Reads every waiting packet; <paramref name="sink"/> gets interleaved floats and the QPC time of the first frame (100 ns units).</summary>
    public void Drain(Action<float[], long> sink)
    {
        var getNext = (delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot<nint>(_capture, 5);
        var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, ulong*, ulong*, int>)Slot<nint>(_capture, 3);
        var release = (delegate* unmanaged[Stdcall]<nint, uint, int>)Slot<nint>(_capture, 4);
        while (true)
        {
            uint packet;
            Marshal.ThrowExceptionForHR(getNext(_capture, &packet));
            if (packet == 0)
                return;
            byte* data;
            uint frames, flags;
            ulong devicePosition, qpc;
            Marshal.ThrowExceptionForHR(getBuffer(_capture, &data, &frames, &flags, &devicePosition, &qpc));
            try
            {
                var samples = (flags & BufferFlagsSilent) != 0
                    ? new float[frames * Channels]
                    : Pcm.ToFloat(new ReadOnlySpan<byte>(data, (int)frames * _blockAlign), _bits, _float);
                sink(samples, (long)qpc);
            }
            finally
            {
                release(_capture, frames);
            }
        }
    }

    public void Dispose()
    {
        if (_capture != 0) Marshal.Release(_capture);
        if (_client != 0) Marshal.Release(_client);
        _capture = _client = 0;
    }
}
