using NAudio.Utils;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MicrophoneOpenAI
{
    internal class Program
    {
        /// <summary>
        /// Sample rate, i.e., samples per second/frequency
        /// </summary>
        private static readonly int waveRate = 44100;
        /// <summary>
        /// Bit depth per sample
        /// </summary>
        private static readonly int waveBits = 16;
        private static readonly int waveChannels = 1;
        /// <summary>
        /// Above this noise level data from microphine is recorded to be sent for transcription.
        /// </summary>
        private static readonly int noiseLevel = 300;
        private static int countBelowNoiseLevel = 0;
        /// <summary>
        /// The time in milliseconds a pause takes, i.e., until the elapsed time is detected as a pause.
        /// If a pause is detected, the recorded data from microphone is sent for transcription.
        /// </summary>
        private static readonly int pauseTimeMilliseconds = 500;
        /// <summary>
        /// The buffer time in milliseconds
        /// </summary>
        private static readonly int bufferMilliseconds = 20;
        /// <summary>
        /// The minimum audio length it needs for a successfull transcription response
        /// </summary>
        private static readonly int minimumAudioLengthMilliseconds = 100;
        private static readonly int minimumAudioLengthBuffer = waveRate * (waveBits / 8) / (1000 / minimumAudioLengthMilliseconds);
        /// <summary>
        /// Contains a consecutive list of arrays of recorded microphone data
        /// </summary>
        private static LinkedList<Int16[]> int16ArrayList;

        private static readonly string OpenAIToken = "";
        private static readonly string OpenAIEndpointSpeechEndoint = "https://api.openai.com/v1/audio/transcriptions";

        static void Main(string[] args)
        {
            TranscribeSpeechFromMicrophone();
        }

        private static void TranscribeSpeechFromMicrophone()
        {
            var waveIn = new WaveInEvent
            {
                DeviceNumber = 0, // indicates which microphone to use
                WaveFormat = new WaveFormat(rate: waveRate, bits: waveBits, channels: waveChannels),
                BufferMilliseconds = bufferMilliseconds
            };
            waveIn.DataAvailable += WaveIn_DataAvailable;
            int16ArrayList = new LinkedList<Int16[]>();
            waveIn.StartRecording();

            Console.WriteLine("Say something ...");
            Console.ReadLine();
        }

        static void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            // copy buffer into an array of integers
            Int16[] values = new Int16[e.Buffer.Length / 2];
            Buffer.BlockCopy(e.Buffer, 0, values, 0, e.Buffer.Length);

            Int16 max = values.Max();
            if (max > noiseLevel)
            {
                // Console.Clear();
                // Console.Write(max);
                countBelowNoiseLevel = 0;
                int16ArrayList.AddLast(values);
            }
            else if (max <= noiseLevel && int16ArrayList.Any())
            {
                countBelowNoiseLevel++;
            }

            if (countBelowNoiseLevel > (pauseTimeMilliseconds / bufferMilliseconds))
            {
                // Console.Clear();
                // Console.Write("Pause detected");
                PrepareContentBytesForPost();
                countBelowNoiseLevel = 0;
            }
        }

        private static void PrepareContentBytesForPost()
        {
            if (int16ArrayList.Any())
            {
                int int16ArrayLength = int16ArrayList.First().Length;
                byte[] result = new byte[int16ArrayLength * sizeof(Int16) * int16ArrayList.Count()];

                for (int i = 0; i < int16ArrayList.Count(); i++)
                {
                    Buffer.BlockCopy(int16ArrayList.ElementAt(i), 0, result, i * int16ArrayLength * sizeof(Int16), int16ArrayLength * sizeof(Int16));
                }

                if (result.Length > minimumAudioLengthBuffer)
                {
                    PostContentBytes(result);
                    int16ArrayList.Clear();
                }
            }
        }

        public static byte[] ConvertToWav(byte[] bytes)
        {
            //string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            //WaveFileWriter wavFileWriter = new WaveFileWriter(Path.Combine(docPath, DateTime.Now.ToString("yyyy-dd-M-HH-mm-ss") + ".wav"), new WaveFormat(waveRate, waveBits, waveChannels));
            //wavFileWriter.Write(bytes, 0, bytes.Length);
            //wavFileWriter.Flush();

            MemoryStream ms = new MemoryStream();
            using (WaveFileWriter writer = new WaveFileWriter(new IgnoreDisposeStream(ms), new WaveFormat(waveRate, waveBits, waveChannels)))
            {
                writer.Write(bytes, 0, bytes.Length);
            }
            ms.Position = 0;
            return ms.ToArray();
        }

        private static async void PostContentBytes(byte[] bytes)
        {
            try
            {
                HttpClient client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(1000);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", OpenAIToken);

                MultipartFormDataContent content = new MultipartFormDataContent()
                {
                    { new ByteArrayContent(ConvertToWav(bytes)), "file", ".wav" },
                    { new StringContent("whisper-1"), "model" }
                };

                HttpResponseMessage response = await client.PostAsync(OpenAIEndpointSpeechEndoint, content);
                string speechToTextResponse = await response.Content.ReadAsStringAsync();
                string transcribedText = GetJsonText(speechToTextResponse);

                if (transcribedText != null)
                {
                    if (transcribedText.Length > 0)
                        Console.WriteLine($"Transcribed: {transcribedText}");
                }
                else
                {
                    Console.WriteLine($"Error: {GetJsonError(speechToTextResponse)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Something went wrong: {ex.Message}");
            }
        }

        private static string GetJsonText(string text)
        {
            var jObject = JObject.Parse(text);
            var jToken = jObject.GetValue("text");
            return jToken != null ? jToken.ToString() : null;
        }

        private static string GetJsonError(string text)
        {
            var jObject = JObject.Parse(text);
            var jToken = jObject.GetValue("error");
            var values = JsonConvert.DeserializeObject<Dictionary<string, string>>(jToken.ToString());
            return values != null ? values["message"] : null;
        }
    }
}
