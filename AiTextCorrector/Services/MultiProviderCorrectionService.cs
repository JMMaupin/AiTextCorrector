using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiTextCorrector.Localization;
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

    // Phrases de l'interface par requête : une réponse courte est plus fiable, et
    // reste sous le plafond de longueur d'Anthropic.
    private const int InterfaceBatchSize = 40;

    // Phrases envoyées en JSON lisible : « é » plutôt que « é ».
    private static readonly JsonSerializerOptions RelaxedJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Consigne de traduction de l'interface. Les trous ({0}, {language}) sont
    // vérifiés au retour : une phrase qui en perd un est écartée.
    private static string InterfaceTranslationInstruction(string language) =>
        $"Translate the values of the JSON object below into {language}. They are the user interface texts of a Windows application that corrects and translates text with AI. " +
        "Each key tells where its text appears: Main is the main window, Status its status bar, Tray the notification area menu, " +
        "Settings, Hotkey, ApiKeys, Prompts and Language the settings window and its tabs, Setup the installer. " +
        "Keep every key unchanged. Keep every placeholder in braces, such as {0}, {1} or {language}, exactly as it is. " +
        "Keep line breaks, symbols such as ✓, ✗, → and …, and these names unchanged: AI Text Corrector, OpenAI, Anthropic, Claude, GLM, Z.ai, Groq, " +
        "HKCU, ASCII, Ctrl, Shift, Alt, Win and environment variable names. " +
        "Write short, natural texts, as in a well translated application: button labels must stay short. Address the user consistently. " +
        "Answer with one JSON object only, with the same keys and the translated texts as values, and no other text.";

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

    /// <summary>
    /// Traduit des phrases de l'interface (clé → texte anglais) vers
    /// <paramref name="language"/>, par lots. <paramref name="batchTranslated"/> reçoit
    /// chaque lot dès sa réponse, avec le nombre de phrases traitées jusque-là : une
    /// erreur en cours de route ne fait pas perdre les lots déjà traduits.
    /// </summary>
    public async Task TranslateInterfaceAsync(IReadOnlyDictionary<string, string> phrases, string language,
                                              Action<int, IReadOnlyDictionary<string, string>> batchTranslated)
    {
        var processed = 0;
        foreach (var batch in phrases.Chunk(InterfaceBatchSize))
        {
            var source = batch.ToDictionary(p => p.Key, p => p.Value);
            var raw = await CompleteAsync(
                $"Instruction: {InterfaceTranslationInstruction(language)}\n\n{JsonSerializer.Serialize(source, RelaxedJson)}",
                jsonMode: true, maxTokens: 4096);

            processed += source.Count;
            batchTranslated(processed, ParseTranslatedPhrases(raw, source));
        }
    }

    /// <summary>
    /// Analyse tolérante, comme pour la correction expliquée. Ne garde que les clés
    /// demandées dont la traduction conserve les trous du texte anglais ; une réponse
    /// illisible donne un lot vide, et ses phrases restent en anglais.
    /// </summary>
    internal static Dictionary<string, string> ParseTranslatedPhrases(string raw, IReadOnlyDictionary<string, string> source)
    {
        var result = new Dictionary<string, string>();
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return result;

        try
        {
            using var document = JsonDocument.Parse(raw.AsMemory(start, end - start + 1));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    source.TryGetValue(property.Name, out var english) &&
                    property.Value.GetString() is { } text &&
                    InterfaceTranslation.IsValidTranslation(english, text))
                {
                    result[property.Name] = text.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Lot inexploitable : ses phrases seront proposées à la retraduction.
        }

        return result;
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
                throw new InvalidOperationException(Loc.Format("Errors.MissingKey", "GLM", "AITEXTCORRECTOR_GLM_API_KEY"));
            }

            return await CompleteWithOpenAiCompatibleAsync(_config.GlmModel, glmKey, GlmEndpoint, userMessage, "GLM", jsonMode);
        }

        if (string.Equals(_config.Provider, "Groq", StringComparison.OrdinalIgnoreCase))
        {
            // Groq est OpenAI-compatible : même mécanisme que GLM, autre endpoint.
            var groqKey = _keyStore.GetGroqKey();
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                throw new InvalidOperationException(Loc.Format("Errors.MissingKey", "Groq", "AITEXTCORRECTOR_GROQ_API_KEY"));
            }

            return await CompleteWithOpenAiCompatibleAsync(_config.GroqModel, groqKey, GroqEndpoint, userMessage, "Groq", jsonMode);
        }

        var openAiKey = _keyStore.GetOpenAiKey();
        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            throw new InvalidOperationException(Loc.Format("Errors.MissingKey", "OpenAI", "OPENAI_API_KEY"));
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
            throw new InvalidOperationException(Loc.Format("Errors.EmptyAnswer", providerLabel));
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
            401 => Loc.Text("Errors.Http401"),
            403 => Loc.Text("Errors.Http403"),
            404 => Loc.Text("Errors.Http404"),
            429 => Loc.Text("Errors.Http429"),
            >= 500 => Loc.Text("Errors.Http5xx"),
            _ => string.Empty
        };

        var text = detail.Length > 0
            ? Loc.Format("Errors.HttpStatusDetail", provider, ex.Status, detail)
            : Loc.Format("Errors.HttpStatus", provider, ex.Status);
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
            throw new InvalidOperationException(Loc.Format("Errors.MissingKey", "Anthropic", "AITEXTCORRECTOR_ANTHROPIC_API_KEY"));
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
            throw new InvalidOperationException(Loc.Format("Errors.EmptyAnswer", "Anthropic"));
        }

        return text.Trim();
    }
}
