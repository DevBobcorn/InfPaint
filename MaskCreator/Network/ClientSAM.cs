using MaskCreator.Masks;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace MaskCreator.Network
{
    // https://stackoverflow.com/questions/57056598/named-pipes-ipc-python-server-c-sharp-client?noredirect=1&lq=1
    public static class ClientSAM
    {
        private static readonly string HOST = "127.0.0.1";
        private static readonly int    PORT = 65432;

        private enum ProcessType
        {
            Initialize        = 200,

            Disconnect        = 100,
            GenerateMasks     = 101,
            GenerateBoxLayers = 102
        }

        public const int MAX_RECEIVE_SIZE = 8 << 20; // 8MB

        private static readonly byte[] _4ByteReadBuf = new byte[4];

        private static TcpClient? tcpClient = null;

        private static void WriteStartSequence(NetworkStream stream, ProcessType type)
        {
            for (int i = 0; i < 5; i++)
            {
                stream.WriteByte(0); // Padding before start sequence, will be ignored
            }

            stream.WriteByte(42); // 1B
            stream.WriteByte(20); // 1B
            stream.WriteByte(77); // 1B
            stream.WriteByte(13); // 1B
            stream.WriteByte(37); // 1B

            stream.WriteByte((byte) type); // 1B
        }

        private static async Task WriteImageData(NetworkStream stream, byte[] imageBytes)
        {
            WriteUInt32(stream, (uint) imageBytes.Length); // 4B, unsigned big-endian
            await stream.WriteAsync(imageBytes); // (imageSize)B
        }

        private static void WriteUInt32(NetworkStream stream, uint num)
        {
            var bytes = BitConverter.GetBytes(num);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes); // Big-endian

            stream.Write(bytes);
        }

        private static async Task<uint> ReadUInt32(NetworkStream stream)
        {
            await stream.ReadAsync(_4ByteReadBuf, 0, 4);

            // If the system architecture is little-endian (that is, little end first),
            // reverse the byte array.
            if (BitConverter.IsLittleEndian)
                Array.Reverse(_4ByteReadBuf);

            return BitConverter.ToUInt32(_4ByteReadBuf, 0);
        }

        private static void WriteAsciiText(NetworkStream stream, string prompt)
        {
            var textBytes = Encoding.ASCII.GetBytes(prompt);
            WriteUInt32(stream, (uint) textBytes.Length); // 4B, unsigned big-endian
            stream.Write(textBytes);
        }

        private static async Task<string> ReadAsciiText(NetworkStream stream)
        {
            uint textSize = await ReadUInt32(stream);
            if (textSize > MAX_RECEIVE_SIZE)
            {
                throw new InvalidDataException("Data too large!");
            }

            var textBuf = new byte[textSize];
            await stream.ReadAsync(textBuf.AsMemory(0, (int) textSize));

            return Encoding.ASCII.GetString(textBuf);
        }

        private static void WriteUtf8Text(NetworkStream stream, string prompt)
        {
            var textBytes = Encoding.UTF8.GetBytes(prompt);
            WriteUInt32(stream, (uint) textBytes.Length); // 4B, unsigned big-endian
            stream.Write(textBytes);
        }

        private static async Task<string> ReadUtf8Text(NetworkStream stream)
        {
            uint textSize = await ReadUInt32(stream);
            if (textSize > MAX_RECEIVE_SIZE)
            {
                throw new InvalidDataException("Data too large!");
            }

            var textBuf = new byte[textSize];
            await stream.ReadAsync(textBuf.AsMemory(0, (int) textSize));

            return Encoding.UTF8.GetString(textBuf);
        }

        private static void WriteControlPoints(NetworkStream stream, ControlPoint[] points)
        {
            if (points.Length == 0) return;

            // Write point count
            WriteUInt32(stream, (uint) points.Length); // 4B, unsigned big-endian

            for (int i = 0; i < points.Length; i++)
            {
                WriteUInt32(stream, (uint) points[i].X);
                WriteUInt32(stream, (uint) points[i].Y);
                stream.WriteByte(points[i].Label ? (byte) 1 : (byte) 0);
            }
        }

        private static void WriteControlBox(NetworkStream stream, ControlBox? box)
        {
            if (box is null) return;

            WriteUInt32(stream, (uint) box.X1);
            WriteUInt32(stream, (uint) box.Y1);
            WriteUInt32(stream, (uint) box.X2);
            WriteUInt32(stream, (uint) box.Y2);
        }

        private static async Task<MaskGenerationResult[]> ReadGeneratedMasks(NetworkStream stream)
        {
            uint maskCount = await ReadUInt32(stream);

            var result = new MaskGenerationResult[maskCount];

            for (uint i = 0; i < maskCount; i++)
            {
                uint scoreAsInt = await ReadUInt32(stream);
                var score = scoreAsInt / 1000000F;
                uint maskSize = await ReadUInt32(stream);
                if (maskSize > MAX_RECEIVE_SIZE)
                {
                    throw new InvalidDataException("Data too large!");
                }

                var maskBytes = new byte[maskSize];
                await stream.ReadAsync(maskBytes, 0, (int) maskSize);

                result[i] = new MaskGenerationResult(maskBytes, score);
            }

            return result;
        }

        public static void Connect()
        {
            Disconnect();

            tcpClient = new TcpClient();

            try
            {
                tcpClient.Connect(HOST, PORT);
            }
            catch (Exception)
            {
                tcpClient.Close();
                tcpClient = null;
            }
        }

        public static void Disconnect()
        {
            if (tcpClient != null)
            {
                if (tcpClient.Connected)
                {
                    try
                    {
                        var stream = tcpClient.GetStream();

                        // Send disconnect request
                        WriteStartSequence(stream, ProcessType.Disconnect);

                        stream.Close();
                    }
                    catch (Exception) { }
                }
                
                tcpClient.Close();

                tcpClient = null;
            }
        }

        private static NetworkStream? GetConnectionStream()
        {
            if (tcpClient == null || !tcpClient.Connected)
            {
                Connect();
            }

            return tcpClient?.GetStream();
        }

        public static async Task<(string procDir, string dinoPrompt)> GetStartupArgs(Action<string> msgCallback)
        {
            NetworkStream? stream = GetConnectionStream();
            if (stream is null)
            {
                msgCallback.Invoke("Failed to connect with SAM2 server.");
                return (string.Empty, string.Empty);
            }

            try
            {
                WriteStartSequence(stream, ProcessType.Initialize);

                stream.Flush();

                msgCallback.Invoke("Starting up...");

                var procDir = await ReadUtf8Text(stream);

                var dinoPrompt = await ReadAsciiText(stream);

                return (procDir, dinoPrompt);
            }
            catch (IOException ex)
            {
                msgCallback.Invoke($"Error: {ex.Message}");
                return (string.Empty, string.Empty);
            }
        }

        public static async Task<BoxMaskLayerData[]> GenerateBoxLayers(byte[] imageBytes, string prompt, int w, int h, Action<string> msgCallback)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                msgCallback.Invoke("Text prompt required for box detection.");
                return [];
            }

            NetworkStream? stream = GetConnectionStream();
            if (stream is null)
            {
                msgCallback.Invoke("Failed to connect with SAM2 server.");
                return [];
            }

            try
            {
                WriteStartSequence(stream, ProcessType.GenerateBoxLayers);
                await WriteImageData(stream, imageBytes);
                WriteAsciiText(stream, prompt);

                stream.Flush();
                msgCallback.Invoke("Generating box layers...");

                uint boxLayerCount = await ReadUInt32(stream);
                var boxLayers = new BoxMaskLayerData[boxLayerCount];

                for (uint i = 0; i < boxLayerCount; i++)
                {
                    var caption = await ReadAsciiText(stream);

                    uint x1 = await ReadUInt32(stream);
                    uint x2 = await ReadUInt32(stream);
                    uint y1 = await ReadUInt32(stream);
                    uint y2 = await ReadUInt32(stream);

                    var boxMasks = await ReadGeneratedMasks(stream);

                    boxLayers[i] = new BoxMaskLayerData($"Box [{caption}]", w, h, (int) x1, (int) x2, (int) y1, (int) y2);
                    boxLayers[i].UpdateMaskData(boxMasks, x => { });
                }

                msgCallback.Invoke($"Generated {boxLayerCount} box layer(s).");

                return boxLayers;
            }
            catch (IOException ex)
            {
                msgCallback.Invoke($"Error: {ex.Message}");
                return [];
            }
        }

        public static async Task<MaskGenerationResult[]> GenerateMasks(byte[] imageBytes, ControlPoint[] points, ControlBox? box, Action<string> msgCallback)
        {
            bool writePoints = points.Length > 0;
            bool writeBox = box is not null;

            byte controlFlag = 0;

            if (writePoints) controlFlag |= 1;
            if (writeBox) controlFlag |= 2;

            if (controlFlag == 0)
            {
                msgCallback.Invoke("Points and/or box prompts required for segmentation.");
                return [];
            }

            NetworkStream? stream = GetConnectionStream();
            if (stream is null)
            {
                msgCallback.Invoke("Failed to connect with SAM2 server.");
                return [];
            }

            try
            {
                WriteStartSequence(stream, ProcessType.GenerateMasks);
                await WriteImageData(stream, imageBytes);

                stream.WriteByte(controlFlag); // 1B

                // Write control points if given
                if (writePoints)
                {
                    WriteControlPoints(stream, points);
                }

                // Write control box if given
                if (writeBox)
                {
                    WriteControlBox(stream, box);
                }

                stream.Flush();
                msgCallback.Invoke("Generating masks...");

                // Receive generated masks
                var masks = await ReadGeneratedMasks(stream);

                msgCallback.Invoke($"Generated {masks.Length} mask candidate(s). Scores: {
                        string.Join(" | ", masks.Select(x => x.score.ToString("0.000")))}");

                return masks;
            }
            catch (IOException ex)
            {
                msgCallback.Invoke($"Error: {ex.Message}");
                return [];
            }
        }
    }
}
