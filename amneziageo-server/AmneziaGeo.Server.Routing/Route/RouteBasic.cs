namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// The targets sent straight out or dropped before any rule is read.
/// </summary>
public sealed record RouteBasic
{
    /// <summary>
    /// The number the dropped targets are carried under in the plan.
    /// </summary>
    public const long BlockId = -1;

    /// <summary>
    /// The number the targets sent straight out are carried under in the plan.
    /// </summary>
    public const long DirectId = -2;

    /// <summary>
    /// The name the dropped targets are carried under in the plan.
    /// </summary>
    public const string BlockName = "basic-block";

    /// <summary>
    /// The name the targets sent straight out are carried under in the plan.
    /// </summary>
    public const string DirectName = "basic-direct";

    /// <summary>
    /// The lists with nothing in them.
    /// </summary>
    public static readonly RouteBasic Empty = new();

    /// <summary>
    /// The targets the host sends out its own way.
    /// </summary>
    public IReadOnlyList<string> Direct { get; init; } = [];

    /// <summary>
    /// The targets the host drops.
    /// </summary>
    public IReadOnlyList<string> Block { get; init; } = [];

    /// <summary>
    /// When the lists were last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>
    /// Tells whether a number belongs to one of the lists.
    /// </summary>
    public static bool Holds(long id) => id is BlockId or DirectId;

    /// <summary>
    /// Returns the lists as rules read ahead of the others, leaving out the empty ones.
    /// </summary>
    public IReadOnlyList<RouteRule> Rules()
    {
        var rules = new List<RouteRule>();
        if (Block.Count > 0)
        {
            rules.Add(new RouteRule { Id = BlockId, Name = BlockName, Action = RouteAction.Block, Targets = Block });
        }

        if (Direct.Count > 0)
        {
            rules.Add(new RouteRule { Id = DirectId, Name = DirectName, Action = RouteAction.Direct, Targets = Direct });
        }

        return rules;
    }
}
