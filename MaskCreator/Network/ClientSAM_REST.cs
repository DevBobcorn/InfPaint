using MaskCreator.Masks;
using MaskCreator.Utils;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MaskCreator.Network
{
    // https://stackoverflow.com/questions/57056598/named-pipes-ipc-python-server-c-sharp-client?noredirect=1&lq=1
    public static class ClientSAM_REST
    {
        private static readonly string HOST = "127.0.0.1";
        private static readonly int    PORT = 7880;

        private static readonly HttpClient httpClient = new();

        public static void Connect()
        {
            // Do nothing
        }

        public static void Disconnect()
        {
            // Do nothing
        }

        /// <summary>
        /// Used for json deserialization
        /// </summary>
        private class StartupArgsResponse
        {
            [JsonPropertyName("proc_dir")]
            public required string ProcDir { get; set; }

            [JsonPropertyName("dino_prompt")]
            public required string DinoPrompt { get; set; }
        }

        /// <summary>
        /// Used for json deserialization
        /// </summary>
        private class MaskDataObject
        {
            [JsonPropertyName("score")]
            public required string Score { get; set; } // float string

            [JsonPropertyName("bytes")]
            public required string Bytes { get; set; } // Base64 encoded image bytes
        }

        /// <summary>
        /// Used for json deserialization
        /// </summary>
        private class BoxMaskLayerDataObject
        {
            [JsonPropertyName("caption")]
            public required string Caption { get; set; }

            [JsonPropertyName("x1")]
            public required int X1 { get; set; }

            [JsonPropertyName("y1")]
            public required int Y1 { get; set; }

            [JsonPropertyName("x2")]
            public required int X2 { get; set; }

            [JsonPropertyName("y2")]
            public required int Y2 { get; set; }

            [JsonPropertyName("masks")]
            public required List<MaskDataObject> Masks { get; set; }
        }

        /// <summary>
        /// Used for json deserialization
        /// </summary>
        private class GenBoxMaskLayersResponse
        {
            [JsonPropertyName("box_layers")]
            public required List<BoxMaskLayerDataObject> BoxLayers { get; set; }
        }

        /// <summary>
        /// Used for json deserialization
        /// </summary>
        private class GenMasksResponse
        {
            [JsonPropertyName("masks")]
            public required List<MaskDataObject> Masks { get; set; }
        }

        private static async Task<string> RequestJsonTextAsync_GET(string url)
        {
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        private static async Task<string> RequestJsonTextAsync_POST(string url, string jsonText)
        {
            var content = new StringContent(jsonText, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }

        public static async Task<(string procDir, string dinoPrompt)> GetStartupArgs(Action<string> msgCallback)
        {
            try
            {
                var requestUrl = $"http://{HOST}:{PORT}/mask_creator_args";

                msgCallback.Invoke("Starting up...");

                // Receive startup args
                var respText = await RequestJsonTextAsync_GET(requestUrl);
                var startupArgs = JsonSerializer.Deserialize<StartupArgsResponse>(respText)!;

                return (startupArgs.ProcDir, startupArgs.DinoPrompt);
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

            try
            {
                Dictionary<string, object> parameters = new()
                {
                    ["image_bytes"] = Convert.ToBase64String(imageBytes),
                    ["text_prompt"] = prompt
                };
                var requestUrl = $"http://{HOST}:{PORT}/generate_box_layers";

                msgCallback.Invoke("Generating box layers...");

                // Receive generated box mask layers
                var respText = await RequestJsonTextAsync_POST(requestUrl, SimpleJsonSerializer.Object2Json(parameters));
                var boxLayerObjects = JsonSerializer.Deserialize<GenBoxMaskLayersResponse>(respText)!.BoxLayers;
                
                var boxLayerCount = boxLayerObjects.Count;
                var boxLayers = new BoxMaskLayerData[boxLayerCount];

                for (int i = 0; i < boxLayerCount; i++)
                {
                    var boxLayerObject = boxLayerObjects[i];

                    boxLayers[i] = new BoxMaskLayerData($"Box [{boxLayerObject.Caption}]", w, h,
                        boxLayerObject.X1, boxLayerObject.Y1, boxLayerObject.X2, boxLayerObject.Y2);
                    boxLayers[i].UpdateMaskData(boxLayerObject.Masks.Select(
                        x => new MaskGenerationResult(Convert.FromBase64String(x.Bytes),
                            float.Parse(x.Score, System.Globalization.CultureInfo.InvariantCulture))).ToArray(), x => { });
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
                msgCallback.Invoke("Points and/or box prompts are required for segmentation.");
                return [];
            }

            try
            {
                Dictionary<string, object> parameters = new()
                {
                    ["image_bytes"] = Convert.ToBase64String(imageBytes),
                    ["control_flag"] = controlFlag
                };

                // Write control points if given
                if (writePoints)
                {
                    parameters.Add("points", string.Join(',', points.Select(p => $"{p.X},{p.Y},{(p.Label ? 1 : 0)}")));
                }

                // Write control box if given
                if (writeBox && box is not null)
                {
                    parameters.Add("box", $"{box.X1},{box.Y1},{box.X2},{box.Y2}");
                }

                var requestUrl = $"http://{HOST}:{PORT}/generate_masks";

                msgCallback.Invoke("Generating masks...");

                // Receive generated masks
                var respText = await RequestJsonTextAsync_POST(requestUrl, SimpleJsonSerializer.Object2Json(parameters));
                var respObj = JsonSerializer.Deserialize<GenMasksResponse>(respText);
                var maskObjects = respObj!.Masks;

                msgCallback.Invoke($"Generated {maskObjects.Count} mask candidate(s). Scores: {string.Join(" | ",
                    maskObjects.Select(x => float.Parse(x.Score, System.Globalization.CultureInfo.InvariantCulture).ToString("0.000")))}");

                return maskObjects.Select(x => new MaskGenerationResult(Convert.FromBase64String(x.Bytes),
                    float.Parse(x.Score, System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            }
            catch (IOException ex)
            {
                msgCallback.Invoke($"Error: {ex.Message}");
                return [];
            }
        }
    }
}
