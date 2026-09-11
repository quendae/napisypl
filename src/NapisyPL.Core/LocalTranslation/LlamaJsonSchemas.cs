namespace NapisyPL.Core.LocalTranslation;

public static class LlamaJsonSchemas
{
    public static object ContextResponseFormat => new
    {
        type = "json_schema",
        schema = new
        {
            type = "object",
            properties = new
            {
                speakers = new
                {
                    type = "object",
                    additionalProperties = new
                    {
                        type = "object",
                        properties = new
                        {
                            gender = new { type = "string", @enum = new[] { "male", "female", "mixed", "unknown" } },
                            confidence = new { type = "number", minimum = 0, maximum = 1 }
                        },
                        required = new[] { "gender", "confidence" },
                        additionalProperties = false
                    }
                },
                lines = new
                {
                    type = "object",
                    additionalProperties = new
                    {
                        type = "object",
                        properties = new
                        {
                            addressee = new { type = "string" },
                            confidence = new { type = "number", minimum = 0, maximum = 1 }
                        },
                        required = new[] { "addressee", "confidence" },
                        additionalProperties = false
                    }
                }
            },
            required = new[] { "speakers", "lines" },
            additionalProperties = false
        }
    };

    public static object TranslationResponseFormat => BuildChangedLineArrayFormat();

    public static object ReviewResponseFormat => BuildChangedLineArrayFormat();

    private static object BuildChangedLineArrayFormat() => new
    {
        type = "json_schema",
        schema = new
        {
            type = "array",
            items = new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "integer", minimum = 1 },
                    text = new { type = "string", minLength = 1 }
                },
                required = new[] { "id", "text" },
                additionalProperties = false
            }
        }
    };
}
