using System.Buffers.Binary;
using System.Text;

namespace NapisyPL.Core.ContextResolution;

public sealed record PcmWaveData(int SampleRate, float[] Samples);

public static class PcmWaveReader
{
    public static PcmWaveData ReadMono16(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Audio context file is not a RIFF WAV file.");
        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Audio context file is not a WAVE file.");

        ushort? audioFormat = null;
        ushort? channels = null;
        int? sampleRate = null;
        ushort? bitsPerSample = null;
        byte[]? pcm = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadUInt32();
            var chunkStart = stream.Position;

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                    throw new InvalidDataException("Invalid WAV fmt chunk.");
                audioFormat = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                _ = reader.ReadInt32(); // byte rate
                _ = reader.ReadUInt16(); // block align
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                if (chunkSize > int.MaxValue)
                    throw new InvalidDataException("WAV data chunk is too large.");
                pcm = reader.ReadBytes((int)chunkSize);
            }

            var next = chunkStart + chunkSize + (chunkSize % 2);
            if (next > stream.Length)
                break;
            stream.Position = next;
        }

        if (audioFormat != 1 || channels != 1 || bitsPerSample != 16 || sampleRate is null || pcm is null)
            throw new InvalidDataException("Enhanced audio must be mono 16-bit PCM WAV.");
        if ((pcm.Length & 1) != 0)
            throw new InvalidDataException("Invalid 16-bit PCM sample data.");

        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2, 2));
            samples[i] = value / 32768f;
        }

        return new PcmWaveData(sampleRate.Value, samples);
    }
}
