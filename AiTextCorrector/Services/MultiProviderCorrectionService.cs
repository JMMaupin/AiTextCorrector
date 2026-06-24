using System;
using System.Linq;
using System.Threading.Tasks;
using AiTextCorrector.Models;
using Anthropic;
using Anthropic.Models.Messages;
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

    public async Task<string> CorrectAsync(string inputText, string prompt)
    {
        return string.Equals(_config.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase)
            ? await CorrectWithAnthropicAsync(inputText, prompt)
            : await CorrectWithOpenAiAsync(inputText, prompt);
    }

    private async Task<string> CorrectWithOpenAiAsync(string inputText, string prompt)
    {
        var apiKey = _keyStore.GetOpenAiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Clé OpenAI absente. Renseigne-la dans la fenêtre ou via OPENAI_API_KEY.");
        }

        var client = new ChatClient(_config.Model, apiKey);
        ChatCompletion completion = await client.CompleteChatAsync(
            new UserChatMessage($"Instruction: {prompt}\n\nTexte à corriger:\n{inputText}"));
        var text = completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("La réponse OpenAI est vide.");
        }

        return text.Trim();
    }

    private async Task<string> CorrectWithAnthropicAsync(string inputText, string prompt)
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
                    Content = $"Instruction: {prompt}\n\nTexte à corriger:\n{inputText}"
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
