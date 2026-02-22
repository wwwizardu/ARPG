#nullable enable
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ARPG.Manager
{
    public class LLMManager : MonoBehaviour
    {
        private LLMClient _client = new LLMClient();
        private bool _isRequesting = false;

        public bool IsReady => _client.IsReady;

        public void Initialize()
        {
            _client.LoadApiKey();
        }

        /// <summary>
        /// 단일 프롬프트로 요청합니다.
        /// </summary>
        public async UniTask<string?> RequestAsync(string inPrompt, string? inSystemPrompt = null)
        {
            var messages = new List<ChatMessage>();

            if (string.IsNullOrEmpty(inSystemPrompt) == false)
            {
                messages.Add(new ChatMessage("system", inSystemPrompt));
            }

            messages.Add(new ChatMessage("user", inPrompt));

            return await _client.SendChatRequestAsync(messages);
        }

        /// <summary>
        /// 대화 컨텍스트를 포함하여 요청합니다.
        /// </summary>
        public async UniTask<string?> RequestWithContextAsync(List<ChatMessage> inMessages, string? inModel = null, float inTemperature = 0.7f)
        {
            return await _client.SendChatRequestAsync(inMessages, inModel, inTemperature);
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha5) && _isRequesting == false)
            {
                TestQuestRequest().Forget();
            }
        }

        private async UniTask TestQuestRequest()
        {
            _isRequesting = true;
            Debug.Log("[LLMManager] 퀘스트 생성 요청 시작...");

            string? result = await RequestAsync(
                "판타지 RPG 퀘스트를 하나 생성해줘. 퀘스트 이름, 설명, 보상을 포함해서 JSON 형식으로 응답해줘.",
                "너는 판타지 RPG 게임의 퀘스트 디자이너야. 한국어로 응답해."
            );

            if (result == null)
            {
                Debug.LogError("[LLMManager] 퀘스트 생성 실패.");
                return;
            }

            Debug.Log($"[LLMManager] 퀘스트 생성 결과:\n{result}");
            _isRequesting = false;
        }
    }
}
