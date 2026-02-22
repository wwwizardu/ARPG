#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace ARPG.Manager
{
    public class ChatMessage
    {
        [JsonProperty("role")]
        public string Role = string.Empty;

        [JsonProperty("content")]
        public string Content = string.Empty;

        public ChatMessage() { }

        public ChatMessage(string inRole, string inContent)
        {
            Role = inRole;
            Content = inContent;
        }
    }

    public class LLMClient
    {
        private const string GROQ_API_URL = "https://api.groq.com/openai/v1/chat/completions";
        public const string MODEL_LLAMA_70B = "llama-3.3-70b-versatile";
        public const string MODEL_LLAMA_8B = "llama-3.1-8b-instant";
        private const string DEFAULT_MODEL = MODEL_LLAMA_70B;
        private const string A_FILE_NAME = "llm.txt";
        private const string CONFIG_FOLDER_NAME = "Config";

        private string _apiKey = string.Empty;
        private bool _isReady = false;

        public bool IsReady => _isReady;

        public void LoadApiKey()
        {
            string configPath = Path.Combine(Application.persistentDataPath, CONFIG_FOLDER_NAME);
            string filePath = Path.Combine(configPath, A_FILE_NAME);

            if (File.Exists(filePath) == false)
            {
                Debug.LogWarning($"[LLMClient] API key file not found: {filePath}");
                _isReady = false;
                return;
            }

            _apiKey = File.ReadAllText(filePath).Trim();

            if (string.IsNullOrEmpty(_apiKey))
            {
                Debug.LogWarning("[LLMClient] API key file is empty.");
                _isReady = false;
                return;
            }

            _isReady = true;
            Debug.Log("[LLMClient] API key loaded successfully.");
        }

        public async UniTask<string?> SendChatRequestAsync(List<ChatMessage> inMessages, string? inModel = null, float inTemperature = 0.7f)
        {
            if (_isReady == false)
            {
                Debug.LogWarning("[LLMClient] Not ready. API key not loaded.");
                return null;
            }

            string model = inModel ?? DEFAULT_MODEL;

            var requestBody = new
            {
                model = model,
                messages = inMessages,
                temperature = inTemperature
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            using UnityWebRequest request = new UnityWebRequest(GROQ_API_URL, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

            await request.SendWebRequest().ToUniTask();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[LLMClient] Request failed: {request.error}\n{request.downloadHandler.text}");
                return null;
            }

            string responseJson = request.downloadHandler.text;
            var response = JsonConvert.DeserializeObject<GroqResponse>(responseJson);

            if (response == null || response.Choices == null || response.Choices.Count == 0)
            {
                Debug.LogError("[LLMClient] Invalid response from Groq API.");
                return null;
            }

            return response.Choices[0].Message.Content;
        }

        #region Response Models

        private class GroqResponse
        {
            [JsonProperty("choices")]
            public List<GroqChoice> Choices = new List<GroqChoice>();
        }

        private class GroqChoice
        {
            [JsonProperty("message")]
            public GroqMessage Message = new GroqMessage();
        }

        private class GroqMessage
        {
            [JsonProperty("content")]
            public string Content = string.Empty;
        }

        #endregion
    }
}
