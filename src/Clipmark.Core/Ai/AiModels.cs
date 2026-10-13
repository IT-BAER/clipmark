namespace Clipmark.Core.Ai;

public sealed record AiModel(string Id, string Name);

public static class AiModels
{
    public static readonly IReadOnlyList<AiModel> All =
    [
        new("claude-sonnet-5-5", "Claude Sonnet 5.5"),
        new("claude-opus-5-5", "Claude Opus 5.5"),
        new("claude-haiku-5-5", "Claude Haiku 5.5"),
    ];

    public static AiModel Default => All[0];

    /// <summary>The model with this id, or <see cref="Default"/> for null or an unknown id.</summary>
    public static AiModel Find(string? id) => All.FirstOrDefault(m => m.Id == id) ?? Default;
}

/// <summary>Ask AI <c>output_config.effort</c> values; every listed model accepts all of them.</summary>
public static class AiEfforts
{
    public static readonly IReadOnlyList<string> All = ["low", "medium", "high", "xhigh"];

    public const string Default = "medium";
}
