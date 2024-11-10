using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using Azure.Storage.Blobs;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech.Audio;

namespace VisionToSpeechFunctionApp
{
    public class VisionToSpeechFunction
    {
        private readonly ILogger<VisionToSpeechFunction> _logger;
        private static readonly string storageConnectionString = "your-storage-connection-string";
        private static readonly string speechApiKey = "your-speech-api-key";
        private static readonly string speechRegion = "eastus";

        public VisionToSpeechFunction(ILogger<VisionToSpeechFunction> logger)
        {
            _logger = logger;
        }

        [Function("VisionToSpeech")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req, 
            FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger("VisionToSpeech");
            logger.LogInformation("C# HTTP trigger function processed a request.");

            // Read the incoming request
            var content = await new StreamReader(req.Body).ReadToEndAsync();  // Use StreamReader for HttpRequest
            var jsonResponse = JObject.Parse(content);

            // Extract the description text (from the vision analysis)
            string? description = jsonResponse["description"]?["captions"]?[0]?["text"]?.ToString();

            if (string.IsNullOrEmpty(description))
            {
                logger.LogError("Description not found in the response.");
                return new BadRequestObjectResult("Description not found.");
            }

            // Initialize the Speech SDK
            var speechConfig = SpeechConfig.FromSubscription(speechApiKey, speechRegion);
            var tempFilePath = Path.Combine(Path.GetTempPath(), "speech.wav");  // Ensure temporary file path is correct
            var audioConfig = AudioConfig.FromWavFileOutput(tempFilePath);
            var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig);

            // Convert text to speech
            var result = await synthesizer.SpeakTextAsync(description);
            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                logger.LogInformation("Successfully synthesized the text to speech.");
            }
            else
            {
                logger.LogError("Error synthesizing speech: " + result.ToString());
                return new BadRequestObjectResult("Failed to synthesize speech.");
            }

            // Upload the MP3 file to Azure Blob Storage
            var blobServiceClient = new BlobServiceClient(storageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient("speech-container");
            var blobClient = containerClient.GetBlobClient("speech.wav");

            // Upload the MP3 file
            using (var fileStream = File.OpenRead(tempFilePath))
            {
                await blobClient.UploadAsync(fileStream, overwrite: true);
            }

            // Clean up temporary file
            File.Delete(tempFilePath);

            return new OkObjectResult("Speech file uploaded successfully.");
        }
    }
}
