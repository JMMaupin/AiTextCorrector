using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiTextCorrector.Models;
using Anthropic;
using Anthropic.Models.Messages;
using OpenAI;
using OpenAI.Chat;

namespace AiTextCorrector.Services;

/// <summary>Réponse « correction expliquée » inexploitable (JSON absent, invalide ou incomplet).</summary>
public sealed class InvalidExplanationException : Exception
{
    public InvalidExplanationException() : base("The AI answer did not contain usable explanations.")
    {
    }
}

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

    // Consigne ajoutée au prompt de correction stricte pour l'option « Explain ».
    // Fixe, et non modifiable dans les réglages : l'analyse de la réponse dépend de
    // ce format. La langue des explications suit celle du texte.
    private const string ExplanationInstruction =
        "Also explain each correction for a learner. Answer with one JSON object only, with no other text, in exactly this format:\n" +
        "{\"corrected\": \"<the full corrected text>\", \"errors\": [{\"original\": \"<the erroneous words, copied exactly from the text>\", " +
        "\"correction\": \"<the corrected words>\", \"category\": \"<the grammar topic, in one or two words>\", " +
        "\"rule\": \"<name of the rule>\", \"explanation\": \"<one or two short sentences explaining why>\"}]}\n" +
        "List the errors in the order they appear in the text, one entry per error. Keep \"original\" and \"correction\" as short as possible: only the words that change. " +
        // Pas d'exemple de catégorie en anglais : le modèle les recopiait tels quels
        // (« agreement ») au milieu d'explications rédigées en français.
        "Write \"category\", \"rule\" and \"explanation\" entirely in the same language as the text, including the category. " +
        "If there is no error, return the text unchanged with an empty \"errors\" list. " +
        "This JSON format replaces any instruction above about returning only the corrected text.";

    public Task<string> CorrectAsync(string inputText, string prompt) =>
        CompleteAsync($"Instruction: {prompt}\n\nText to correct:\n{inputText}");

    /// <summary><paramref name="instruction"/> : prompt de traduction, langue cible déjà substituée.</summary>
    public Task<string> TranslateAsync(string text, string instruction) =>
        CompleteAsync($"Instruction: {instruction}\n\nText to translate:\n{text}");

    /// <summary>
    /// Correction stricte avec l'explication de chaque faute (option « Explain »).
    /// Lève <see cref="InvalidExplanationException"/> si la réponse n'est pas
    /// exploitable : l'appelant se replie alors sur une correction ordinaire.
    /// </summary>
    public async Task<ExplainedCorrection> CorrectWithExplanationsAsync(string inputText, string prompt)
    {
        // Les explications allongent nettement la réponse : plafond relevé pour
        // Anthropic, faute de quoi le JSON serait tronqué.
        var raw = await CompleteAsync(
            $"Instruction: {prompt}\n\n{ExplanationInstruction}\n\nText to correct:\n{inputText}",
            jsonMode: true, maxTokens: 4096);
        return ParseExplainedCorrection(raw);
    }

    /// <summary>
    /// Analyse tolérante : certains modèles entourent le JSON d'un bloc ```json ou
    /// d'une phrase. Seul « corrected » est indispensable ; une faute mal formée est
    /// ignorée plutôt que de faire échouer toute la réponse.
    /// </summary>
    internal static ExplainedCorrection ParseExplainedCorrection(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidExplanationException();
        }

        try
        {
            using var document = JsonDocument.Parse(raw.AsMemory(start, end - start + 1));
            var root = document.RootElement;

            var corrected = StringProperty(root, "corrected");
            if (string.IsNullOrWhiteSpace(corrected))
            {
                throw new InvalidExplanationException();
            }

            var errors = new List<ErrorExplanation>();
            if (Property(root, "errors") is { ValueKind: JsonValueKind.Array } list)
            {
                foreach (var item in list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object))
                {
                    errors.Add(new ErrorExplanation(
                        StringProperty(item, "original"),
                        StringProperty(item, "correction"),
                        StringProperty(item, "category"),
                        StringProperty(item, "rule"),
                        StringProperty(item, "explanation")));
                }
            }

            return new ExplainedCorrection(corrected.Trim(), errors);
        }
        catch (JsonException)
        {
            throw new InvalidExplanationException();
        }
    }

    // Nom de propriété sans tenir compte de la casse : « Corrected » ou « corrected ».
    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string StringProperty(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : string.Empty;

    private async Task<string> CompleteAsync(string userMessage, bool jsonMode = false, int maxTokens = 1200)
    {
        if (string.Equals(_config.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return await CompleteWithAnthropicAsync(userMessage, maxTokens);
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

            return await CompleteWithOpenAiCompatibleAsync(_config.GlmModel, glmKey, GlmEndpoint, userMessage, "GLM", jsonMode);
        }

        if (string.Equals(_config.Provider, "Groq", StringComparison.OrdinalIgnoreCase))
        {
            // Groq est OpenAI-compatible : même mécanisme que GLM, autre endpoint.
            var groqKey = _keyStore.GetGroqKey();
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                throw new InvalidOperationException("Clé Groq absente. Renseigne-la dans la fenêtre ou via AITEXTCORRECTOR_GROQ_API_KEY.");
            }

            return await CompleteWithOpenAiCompatibleAsync(_config.GroqModel, groqKey, GroqEndpoint, userMessage, "Groq", jsonMode);
        }

        var openAiKey = _keyStore.GetOpenAiKey();
        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            throw new InvalidOperationException("Clé OpenAI absente. Renseigne-la dans la fenêtre ou via OPENAI_API_KEY.");
        }

        return await CompleteWithOpenAiCompatibleAsync(_config.Model, openAiKey, endpoint: null, userMessage, "OpenAI", jsonMode);
    }

    // Appelle un service compatible avec l'API OpenAI (OpenAI lui-même ou GLM/Z.ai
    // selon l'endpoint fourni).
    private static async Task<string> CompleteWithOpenAiCompatibleAsync(
        string model, string apiKey, string? endpoint, string userMessage, string providerLabel, bool jsonMode)
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

        ChatMessage[] messages = [new UserChatMessage(userMessage)];
        ChatCompletion completion;
        try
        {
            try
            {
                // Mode JSON : le service garantit un objet JSON valide en réponse.
                var options = jsonMode
                    ? new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() }
                    : null;
                completion = await client.CompleteChatAsync(messages, options);
            }
            catch (ClientResultException ex) when (jsonMode && ex.Status == 400)
            {
                // Modèle qui refuse le mode JSON : la consigne du prompt suffit, et
                // l'analyse de la réponse est tolérante.
                completion = await client.CompleteChatAsync(messages);
            }
        }
        catch (ClientResultException ex)
        {
            throw new InvalidOperationException(DescribeHttpError(providerLabel, ex), ex);
        }

        var text = completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"La réponse {providerLabel} est vide.");
        }

        return text.Trim();
    }

    /// <summary>
    /// Message d'erreur HTTP sur une ligne, pour la barre d'état : service, code,
    /// raison donnée par le serveur et piste de résolution. Le message du SDK
    /// reléguait la raison sous deux retours à la ligne (« HTTP 403 (…) ⏎⏎ raison »),
    /// voire la perdait quand un pare-feu répond par une page HTML.
    /// </summary>
    internal static string DescribeHttpError(string provider, ClientResultException ex)
    {
        var detail = ExtractErrorDetail(ReadBody(ex));
        var hint = ex.Status switch
        {
            401 => "The API key is invalid or revoked: check it in Settings > API keys.",
            403 => "Access refused from this computer: check for a VPN, a company proxy or firewall, " +
                   "a country the provider does not serve, or another key set in an environment variable.",
            404 => "Unknown model or address: check the model name in the configuration file.",
            429 => "Rate limit or quota reached: wait a moment, or check your plan and credit.",
            >= 500 => "Temporary problem at the provider: try again in a moment.",
            _ => string.Empty
        };

        var text = detail.Length > 0
            ? $"{provider} returned HTTP {ex.Status}: {detail}"
            : $"{provider} returned HTTP {ex.Status}";
        return hint.Length > 0 ? $"{text}. {hint}" : $"{text}.";
    }

    // Corps de la réponse d'erreur, ou null. Une réponse vide fait lever une
    // exception à la lecture (« Value cannot be null… ») : elle ne doit pas masquer
    // l'erreur HTTP d'origine.
    private static string? ReadBody(ClientResultException ex)
    {
        try
        {
            return ex.GetRawResponse()?.Content?.ToString();
        }
        catch
        {
            return null;
        }
    }

    // Raison lisible dans le corps de la réponse : message JSON au format OpenAI,
    // sinon titre de la page HTML (pare-feu, proxy), sinon début du texte brut.
    private static string ExtractErrorDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (Property(document.RootElement, "error") is { } error)
            {
                var message = error.ValueKind == JsonValueKind.String ? error.GetString() : StringProperty(error, "message");
                if (!string.IsNullOrWhiteSpace(message)) return Shorten(message);
            }
        }
        catch (JsonException)
        {
            // Pas du JSON : page HTML ou texte brut.
        }

        var title = Regex.Match(body, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (title.Success) return Shorten(WebUtility.HtmlDecode(title.Groups[1].Value));

        return body.TrimStart().StartsWith('<') ? string.Empty : Shorten(body);
    }

    private static string Shorten(string text)
    {
        var line = Regex.Replace(text, @"\s+", " ").Trim().TrimEnd('.');
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    private async Task<string> CompleteWithAnthropicAsync(string userMessage, int maxTokens)
    {
        var apiKey = _keyStore.GetAnthropicKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Clé Anthropic absente. Renseigne-la dans la fenêtre ou via AITEXTCORRECTOR_ANTHROPIC_API_KEY.");
        }

        var client = new AnthropicClient { ApiKey = apiKey };
        var parameters = new MessageCreateParams
        {
            MaxTokens = maxTokens,
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
