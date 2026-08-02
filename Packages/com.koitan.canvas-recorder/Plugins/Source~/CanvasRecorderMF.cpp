// Windows の Media Foundation を使って H.264 + AAC の MP4 を書き出す薄いラッパー。
// Unity のスタンドアロンビルドから [DllImport] で呼ばれる。
//
// COM の扱いをネイティブ側に閉じ込めることで、Unity 側は単なる C 関数呼び出しになる。
// IL2CPP と Mono のどちらでも動作させるための構成。

#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <vector>

#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")

#define CR_API extern "C" __declspec(dllexport)

namespace
{
    struct Encoder
    {
        IMFSinkWriter* writer = nullptr;
        DWORD videoStream = 0;
        DWORD audioStream = 0;
        bool hasAudio = false;

        UINT32 width = 0;
        UINT32 height = 0;
        UINT32 fps = 30;
        UINT32 channels = 0;
        UINT32 sampleRate = 0;

        LONGLONG videoTime = 0;      // 100ns 単位
        LONGLONG videoFrameDuration = 0;
        LONGLONG audioTime = 0;

        bool flipVertically = false;
    };

    Encoder* g_encoder = nullptr;
    bool g_mfStarted = false;

    void ReleaseEncoder()
    {
        if (g_encoder == nullptr) return;

        if (g_encoder->writer != nullptr)
        {
            g_encoder->writer->Release();
            g_encoder->writer = nullptr;
        }

        delete g_encoder;
        g_encoder = nullptr;
    }
}

// 録画を開始する。
// audioChannels / audioSampleRate に 0 を渡すと音声トラックを作らない。
// flipVertically は、渡されるフレームが下から上に並んでいる場合に 1 を指定する。
// 戻り値: 成功なら 0、失敗なら HRESULT。
CR_API int CanvasRecorderMF_Open(
    const wchar_t* path,
    int width, int height, int fps, int videoBitrate,
    int audioChannels, int audioSampleRate,
    int flipVertically)
{
    if (g_encoder != nullptr) return E_UNEXPECTED;
    if (path == nullptr || width <= 0 || height <= 0 || fps <= 0) return E_INVALIDARG;

    // H.264 は偶数の寸法を要求する。
    if ((width & 1) != 0 || (height & 1) != 0) return E_INVALIDARG;

    HRESULT hr = S_OK;

    if (!g_mfStarted)
    {
        hr = MFStartup(MF_VERSION, MFSTARTUP_LITE);
        if (FAILED(hr)) return hr;
        g_mfStarted = true;
    }

    Encoder* encoder = new Encoder();
    encoder->width = static_cast<UINT32>(width);
    encoder->height = static_cast<UINT32>(height);
    encoder->fps = static_cast<UINT32>(fps);
    encoder->videoFrameDuration = 10 * 1000 * 1000 / fps;
    encoder->flipVertically = flipVertically != 0;
    encoder->channels = static_cast<UINT32>(audioChannels);
    encoder->sampleRate = static_cast<UINT32>(audioSampleRate);
    encoder->hasAudio = audioChannels > 0 && audioSampleRate > 0;

    IMFMediaType* videoOut = nullptr;
    IMFMediaType* videoIn = nullptr;
    IMFMediaType* audioOut = nullptr;
    IMFMediaType* audioIn = nullptr;

    hr = MFCreateSinkWriterFromURL(path, nullptr, nullptr, &encoder->writer);

    // 映像の出力形式（H.264）
    if (SUCCEEDED(hr)) hr = MFCreateMediaType(&videoOut);
    if (SUCCEEDED(hr)) hr = videoOut->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (SUCCEEDED(hr)) hr = videoOut->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
    if (SUCCEEDED(hr)) hr = videoOut->SetUINT32(MF_MT_AVG_BITRATE, static_cast<UINT32>(videoBitrate));
    if (SUCCEEDED(hr)) hr = videoOut->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (SUCCEEDED(hr)) hr = MFSetAttributeSize(videoOut, MF_MT_FRAME_SIZE, encoder->width, encoder->height);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(videoOut, MF_MT_FRAME_RATE, encoder->fps, 1);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(videoOut, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    if (SUCCEEDED(hr)) hr = encoder->writer->AddStream(videoOut, &encoder->videoStream);

    // 映像の入力形式（RGB32 = BGRA）。色変換は SinkWriter が自動で挟む。
    if (SUCCEEDED(hr)) hr = MFCreateMediaType(&videoIn);
    if (SUCCEEDED(hr)) hr = videoIn->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (SUCCEEDED(hr)) hr = videoIn->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    if (SUCCEEDED(hr)) hr = videoIn->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (SUCCEEDED(hr)) hr = MFSetAttributeSize(videoIn, MF_MT_FRAME_SIZE, encoder->width, encoder->height);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(videoIn, MF_MT_FRAME_RATE, encoder->fps, 1);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(videoIn, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    if (SUCCEEDED(hr)) hr = encoder->writer->SetInputMediaType(encoder->videoStream, videoIn, nullptr);

    if (SUCCEEDED(hr) && encoder->hasAudio)
    {
        // 音声の出力形式（AAC）
        if (SUCCEEDED(hr)) hr = MFCreateMediaType(&audioOut);
        if (SUCCEEDED(hr)) hr = audioOut->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        if (SUCCEEDED(hr)) hr = audioOut->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC);
        if (SUCCEEDED(hr)) hr = audioOut->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        if (SUCCEEDED(hr)) hr = audioOut->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, encoder->sampleRate);
        if (SUCCEEDED(hr)) hr = audioOut->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, encoder->channels);
        if (SUCCEEDED(hr)) hr = audioOut->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 16000);
        if (SUCCEEDED(hr)) hr = encoder->writer->AddStream(audioOut, &encoder->audioStream);

        // 音声の入力形式（16bit PCM）
        if (SUCCEEDED(hr)) hr = MFCreateMediaType(&audioIn);
        if (SUCCEEDED(hr)) hr = audioIn->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        if (SUCCEEDED(hr)) hr = audioIn->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
        if (SUCCEEDED(hr)) hr = audioIn->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        if (SUCCEEDED(hr)) hr = audioIn->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, encoder->sampleRate);
        if (SUCCEEDED(hr)) hr = audioIn->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, encoder->channels);
        if (SUCCEEDED(hr)) hr = audioIn->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, encoder->channels * 2);
        if (SUCCEEDED(hr))
            hr = audioIn->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, encoder->sampleRate * encoder->channels * 2);
        if (SUCCEEDED(hr)) hr = encoder->writer->SetInputMediaType(encoder->audioStream, audioIn, nullptr);
    }

    if (SUCCEEDED(hr)) hr = encoder->writer->BeginWriting();

    if (videoOut != nullptr) videoOut->Release();
    if (videoIn != nullptr) videoIn->Release();
    if (audioOut != nullptr) audioOut->Release();
    if (audioIn != nullptr) audioIn->Release();

    if (FAILED(hr))
    {
        g_encoder = encoder;
        ReleaseEncoder();
        return hr;
    }

    g_encoder = encoder;
    return S_OK;
}

// BGRA32 のフレームを 1 枚書き込む。data は width * height * 4 バイト。
// timeHns に 0 以上を渡すとその時刻（100ns 単位）で記録する。負値なら fps から自動で進める。
// 音声はサンプル数で正確に進むため、映像側を実経過時間で刻むと同期がずれない。
CR_API int CanvasRecorderMF_WriteVideoFrame(const unsigned char* data, int size, long long timeHns)
{
    if (g_encoder == nullptr || g_encoder->writer == nullptr) return E_UNEXPECTED;
    if (data == nullptr) return E_POINTER;

    const LONG stride = static_cast<LONG>(g_encoder->width) * 4;
    const DWORD bufferSize = static_cast<DWORD>(stride) * g_encoder->height;
    if (static_cast<DWORD>(size) < bufferSize) return E_INVALIDARG;

    IMFMediaBuffer* buffer = nullptr;
    HRESULT hr = MFCreateMemoryBuffer(bufferSize, &buffer);

    if (SUCCEEDED(hr))
    {
        BYTE* destination = nullptr;
        hr = buffer->Lock(&destination, nullptr, nullptr);

        if (SUCCEEDED(hr))
        {
            // MFCopyImage は負のストライドで上下反転コピーができる。
            const BYTE* source = data;
            LONG sourceStride = stride;
            if (g_encoder->flipVertically)
            {
                source = data + static_cast<size_t>(stride) * (g_encoder->height - 1);
                sourceStride = -stride;
            }

            hr = MFCopyImage(destination, stride, source, sourceStride,
                             static_cast<DWORD>(stride), g_encoder->height);
            buffer->Unlock();
        }
    }

    if (SUCCEEDED(hr)) hr = buffer->SetCurrentLength(bufferSize);

    const LONGLONG presentationTime = timeHns >= 0 ? timeHns : g_encoder->videoTime;

    IMFSample* sample = nullptr;
    if (SUCCEEDED(hr)) hr = MFCreateSample(&sample);
    if (SUCCEEDED(hr)) hr = sample->AddBuffer(buffer);
    if (SUCCEEDED(hr)) hr = sample->SetSampleTime(presentationTime);
    if (SUCCEEDED(hr)) hr = sample->SetSampleDuration(g_encoder->videoFrameDuration);
    if (SUCCEEDED(hr)) hr = g_encoder->writer->WriteSample(g_encoder->videoStream, sample);

    if (SUCCEEDED(hr)) g_encoder->videoTime = presentationTime + g_encoder->videoFrameDuration;

    if (sample != nullptr) sample->Release();
    if (buffer != nullptr) buffer->Release();

    return hr;
}

// 音声を書き込む。samples は -1..1 の float、count はチャンネル分を含めた総サンプル数。
CR_API int CanvasRecorderMF_WriteAudio(const float* samples, int count)
{
    if (g_encoder == nullptr || g_encoder->writer == nullptr) return E_UNEXPECTED;
    if (!g_encoder->hasAudio) return S_FALSE;
    if (samples == nullptr || count <= 0) return E_INVALIDARG;

    const DWORD bufferSize = static_cast<DWORD>(count) * 2; // 16bit
    IMFMediaBuffer* buffer = nullptr;
    HRESULT hr = MFCreateMemoryBuffer(bufferSize, &buffer);

    if (SUCCEEDED(hr))
    {
        BYTE* destination = nullptr;
        hr = buffer->Lock(&destination, nullptr, nullptr);

        if (SUCCEEDED(hr))
        {
            short* output = reinterpret_cast<short*>(destination);
            for (int i = 0; i < count; i++)
            {
                float value = samples[i];
                if (value > 1.0f) value = 1.0f;
                if (value < -1.0f) value = -1.0f;
                output[i] = static_cast<short>(value * 32767.0f);
            }
            buffer->Unlock();
        }
    }

    if (SUCCEEDED(hr)) hr = buffer->SetCurrentLength(bufferSize);

    const int frames = count / static_cast<int>(g_encoder->channels);
    const LONGLONG duration =
        static_cast<LONGLONG>(frames) * 10 * 1000 * 1000 / g_encoder->sampleRate;

    IMFSample* sample = nullptr;
    if (SUCCEEDED(hr)) hr = MFCreateSample(&sample);
    if (SUCCEEDED(hr)) hr = sample->AddBuffer(buffer);
    if (SUCCEEDED(hr)) hr = sample->SetSampleTime(g_encoder->audioTime);
    if (SUCCEEDED(hr)) hr = sample->SetSampleDuration(duration);
    if (SUCCEEDED(hr)) hr = g_encoder->writer->WriteSample(g_encoder->audioStream, sample);

    if (SUCCEEDED(hr)) g_encoder->audioTime += duration;

    if (sample != nullptr) sample->Release();
    if (buffer != nullptr) buffer->Release();

    return hr;
}

// 録画を終了してファイルを完成させる。
CR_API int CanvasRecorderMF_Close()
{
    if (g_encoder == nullptr) return S_FALSE;

    HRESULT hr = S_OK;
    if (g_encoder->writer != nullptr) hr = g_encoder->writer->Finalize();

    ReleaseEncoder();
    return hr;
}

// 録画中かどうか。
CR_API int CanvasRecorderMF_IsOpen()
{
    return g_encoder != nullptr ? 1 : 0;
}
