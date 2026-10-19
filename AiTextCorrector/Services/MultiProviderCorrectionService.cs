using System;
using System.ClientModel;
using System.Linq;
using System.Threading.Tasks;
using AiTextCorrector.Models;
using Anthropic;
using Anthropic.Models.Messages;
using OpenAI;
using OpenAI.Chat;

namespace AiTextCorrector.Services;

public class MultiProviderCorrectionService
{
    private readonly AppConfig _config;
    private readonly ApiKeyStore _keyStore;

    public MultiProviderCorrectionService(AppConfig config, ApiKeyStore keyStore)
    {
        _config = config;
        _keyStore = keyStore;
    }

    // Endpoint OpenAI-compatible de Z.ai pour les modèles GLM.
    private const string GlmEndpoint = "https://api.z.ai/api/paas/v4";

    // Endpoint OpenAI-compatible de Groq.
    private const string GroqEndpoint = "https://api.groq.com/openai/v1";

    public Task<string> CorrectAsync(string inputText, string prompt) =>
        CompleteAsync($"Instruction: {prompt}\n\nTexte à corriger:\n{inputText}");

    /// <summary><paramref name="instruction"/> : prompt de traduction, langue cible déjà substituée.</summary>
    public Task<string> TranslateAsync(string text, string instruction) =>
        CompleteAsync($"Instruction: {instruction}\n\nTexte à traduire:\n{text}");

    private async Task<string> CompleteAsync(string userMessage)
    {
        if (string.Equals(_config.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return await CompleteWithAnthropicAsync(userMessage);
        }

        if (string.Equals(_config.Provider, "GLM", StringComparison.OrdinalIgnoreCase))
        {
            // GLM (Z.ai) est OpenAI-compatible : on réutilise le client OpenAI avec
            // une base URL et une clé différentes.
            var glmKey = _keyStore.GetGlmKey();
            if (string.IsNullOrWhiteSpace(glmKey))
            {
                throw new InvalidOperationException("Clé GLM absente. Renseigne-la dans la fenêtre ou via AITEXTCORRECTOR_GLM_API_KEY.");
            }

            return await CompleteWithOpenAiCompatibleAsync(_config.GlmModel, glmKey, GlmEndpoint, userMessage, "GLM");
        }

        if (string.Equals(_config.Provider, "Groq", StringComparison.OrdinalIgnoreCase))
        {
            // Groq est OpenAI-compatible : même mécanisme que GLM, autre endpoint.
            var groqKey = _keyStore.GetGroqKey();
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                throw new InvalidOperationException("Clé Groq absente. Renseigne-la dans la fenêtre ou via AITEXTCORRECTOR_GROQ_API_KEY.");
            }

            return await CompleteWithOpenAiCompatibleAsync(_config.GroqModel, groqKey, GroqEndpoint, userMessage, "Groq");
        }

        var openAiKey = _keyStore.GetOpenAiKey();
        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            throw new InvalidOperationException("Clé OpenAI absente. Renseigne-la dans la fenêtre ou via OPENAI_API_KEY.");
        }

        return await CompleteWithOpenAiCompatibleAsync(_config.Model, openAiKey, endpoint: null, userMessage, "OpenAI");
    }

    // Appelle un service compatible avec l'API OpenAI (OpenAI lui-même ou GLM/Z.ai
    // selon l'endpoint fourni).
    private static async Task<string> CompleteWithOpenAiCompatibleAsync(
        string model, string apiKey, string? endpoint, string userMessage, string providerLabel)
    {
        ChatClient client;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            client = new ChatClient(model, apiKey);
        }
        else
        {
            var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
            client = new ChatClient(model, new ApiKeyCredential(apiKey), options);
        }

        ChatCompletion completion = await client.CompleteChatAsync(new UserChatMessage(userMessage));
        var text = completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"La réponse {providerLabel} est vide.");
        }

        return text.Trim();
    }

    private async Task<string> CompleteWithAnthropicAsync(string userMessage)
    {
        var apiKey = _keyStore.GetAnthropicKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Clé Anthropic absente. Renseigne-la dans la fenêtre ou via AITEXTCORRECTOR_ANTHROPIC_API_KEY.");
        }

        var client = new AnthropicClient { ApiKey = apiKey };
        var parameters = new MessageCreateParams
        {
            MaxTokens = 1200,
            Model = _config.AnthropicModel,
            Messages =
            [
                new MessageParam
                {
                    Role = Role.User,
                    Content = userMessage
                }
            ]
        };

        var message = await client.Messages.Create(parameters);
        var text = string.Concat(message.Content.Select(c => c.TryPickText(out var textBlock) ? textBlock.Text : string.Empty));

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("La réponse Anthropic est vide.");
        }

        return text.Trim();
    }
}
