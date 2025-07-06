using Concentus;
using Concentus.Oggfile;
using EmbedIO;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;

namespace SpeechAPITTS
{
    internal class OpenAIVoicesResponse
    {
        [JsonProperty("voices")]
        public List<Voice> Voices { get; set; } = [];

        internal class Voice
        {
            [JsonProperty("id")]
            public string Id { get; set; } = string.Empty;

            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;
        }
    }

    internal class OpenAIErrorResponse
    {
        [JsonProperty("error")]
        public string Error { get; set; } = string.Empty;

        [JsonProperty("details")]
        public string Details { get; set; } = string.Empty;
    }

    internal class OpenAISpeechRequest
    {
        [JsonProperty("input", Required = Required.Always)]
        public string Input { get; set; } = string.Empty;

        [JsonProperty("voice", Required = Required.Always)]
        public string Voice { get; set; } = string.Empty;

        [JsonProperty("response_format", Required = Required.Always)]
        public string ResponseFormat { get; set; } = string.Empty;
    }

    internal interface IFormatConverter
    {
        byte[] Convert(int sampleRate, int channels, short[] samples);

        string ResultMimeType { get; }
    }

    internal class OpusFormatConverter : IFormatConverter
    {
        public byte[] Convert(int sampleRate, int channels, short[] samples)
        {
            var outputStream = new MemoryStream();
            var opusEncoder = OpusCodecFactory.CreateEncoder(sampleRate, channels);
            var oggStream = new OpusOggWriteStream(opusEncoder, outputStream);
            oggStream.WriteSamples(samples, 0, samples.Length);
            oggStream.Finish();
            return outputStream.ToArray();
        }

        public string ResultMimeType => "audio/ogg; codecs=opus";
    }

    internal class Program
    {
        private const int SampleRate = 48000;
        private const int Channels = 2;

        private static async Task SendJsonResponse(IHttpContext context, int statusCode, object response)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var responseBuffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response));

            await context.Response.OutputStream.WriteAsync(responseBuffer);
            context.Response.Close();
        }

        private static async Task VoicesHandler(IHttpContext context)
        {
            Debug.Assert(OperatingSystem.IsWindows());

            var synthesizer = new SpeechSynthesizer();
            var voices = synthesizer.GetInstalledVoices();

            await SendJsonResponse(context, 200, new OpenAIVoicesResponse
            {
                Voices = voices.Select(voice =>
                {
                    Debug.Assert(OperatingSystem.IsWindows());

                    return new OpenAIVoicesResponse.Voice
                    {
                        Id = voice.VoiceInfo.Name,
                        Name = $"{voice.VoiceInfo.Name}/{voice.VoiceInfo.Gender}/{voice.VoiceInfo.Age}"
                    };
                }).ToList()
            });
        }

        private static readonly Dictionary<string, IFormatConverter> formatConverters = new()
        {
            { "opus", new OpusFormatConverter() }
        };

        private static async Task SpeechHandler(IHttpContext context)
        {
            Debug.Assert(OperatingSystem.IsWindows());

            if (context.Request.ContentType != "application/json")
            {
                await SendJsonResponse(context, 400, new OpenAIErrorResponse
                {
                    Error = "malformed body",
                    Details = $"content-type is not application/json: {context.Request.ContentType}"
                });
                return;
            }

            OpenAISpeechRequest request;

            try
            {
                request = JsonConvert.DeserializeObject<OpenAISpeechRequest>(new StreamReader(context.Request.InputStream, Encoding.UTF8).ReadToEnd(), new JsonSerializerSettings
                {
                    Error = (sender, args) =>
                    {
                        throw args.ErrorContext.Error;
                    },
                }) ?? throw new Exception("no body?");
            }
            catch (Exception ex)
            {
                await SendJsonResponse(context, 400, new OpenAIErrorResponse
                {
                    Error = "malformed body",
                    Details = $"error occured while deserializing json: {ex}"
                });
                return;
            }

            if (!formatConverters.TryGetValue(request.ResponseFormat, out IFormatConverter? converter))
            {
                await SendJsonResponse(context, 400, new OpenAIErrorResponse
                {
                    Error = "malformed body",
                    Details = $"response format '{request.ResponseFormat}' is not supported"
                });
                return;
            }

            var synthesizer = new SpeechSynthesizer();
            FixVoices(synthesizer);

            var voices = synthesizer.GetInstalledVoices();

            var voice = voices.FirstOrDefault(otherVoice =>
            {
                Debug.Assert(OperatingSystem.IsWindows());
                return otherVoice.VoiceInfo.Name == request.Voice;
            });

            if (voice == null)
            {
                await SendJsonResponse(context, 400, new OpenAIErrorResponse
                {
                    Error = "malformed body",
                    Details = $"voice '{request.Voice}' not found"
                });
                return;
            }

            var outputStream = new MemoryStream();

            try
            {
                synthesizer.SelectVoice(voice.VoiceInfo.Name);
                synthesizer.SetOutputToAudioStream(outputStream,
                    new SpeechAudioFormatInfo(SampleRate, AudioBitsPerSample.Sixteen, Channels == 1 ? AudioChannel.Mono :
                    Channels == 2 ? AudioChannel.Stereo : throw new Exception("wrong number of channels")));
                synthesizer.Speak(request.Input);
            }
            catch (Exception ex)
            {
                await SendJsonResponse(context, 500, new OpenAIErrorResponse
                {
                    Error = "internal error",
                    Details = $"failed to speak: {ex}"
                });
                return;
            }

            byte[] converted;

            try
            {
                var input = outputStream.ToArray();
                var samples = new short[input.Length / 2];
                Buffer.BlockCopy(input, 0, samples, 0, input.Length);

                converted = converter.Convert(SampleRate, Channels, samples);
            }
            catch (Exception ex)
            {
                await SendJsonResponse(context, 500, new OpenAIErrorResponse
                {
                    Error = "internal error",
                    Details = $"failed to convert: {ex}"
                });
                return;
            }

            context.Response.ContentType = converter.ResultMimeType;
            context.Response.StatusCode = 200;
            await context.Response.OutputStream.WriteAsync(converted);
            context.Response.Close();
        }

        private static void FixVoices(SpeechSynthesizer synthesizer)
        {
            Debug.Assert(OperatingSystem.IsWindows());

            var voices = synthesizer.GetInstalledVoices();
            foreach (var voice in voices)
            {
                var voiceInfo = voice.VoiceInfo;
                var voiceCultureField = typeof(VoiceInfo).GetField("_culture", BindingFlags.NonPublic | BindingFlags.Instance);
                if (voiceCultureField == null)
                {
                    throw new Exception("field _culture not found");
                }
                voiceCultureField.SetValue(voiceInfo, CultureInfo.InvariantCulture);
            }
        }

        private static WebServer CreateServer()
        {
            var server = new WebServer(o => o
                .WithUrlPrefix("http://*:8000/")
                .WithEmbedIOHttpListener()
            );

            server.OnGet("/v1/audio/voices", VoicesHandler);
            server.OnGet("/audio/voices", VoicesHandler);

            server.OnPost("/v1/audio/speech", SpeechHandler);
            server.OnPost("/audio/speech", SpeechHandler);

            server.OnAny(async context => await SendJsonResponse(context, 404, new OpenAIErrorResponse
            {
                Error = "not found",
                Details = $"route {context.Request.HttpMethod} {context.RequestedPath} does not exist"
            }));

            return server;
        }

        private static void Main()
        {
            var server = CreateServer();

            server.RunAsync().Wait();
        }
    }
}
