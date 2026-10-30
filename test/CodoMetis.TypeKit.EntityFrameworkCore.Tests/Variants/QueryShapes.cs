using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests.Variants;

/// <summary>Where the value object a query reads is.</summary>
public enum Site
{
    /// <summary>A table column, <c>r.P</c>.</summary>
    Column,

    /// <summary>A nullable column, <c>r.N</c>, read with <c>ValueOrNull()</c>.</summary>
    Nullable,

    /// <summary>The element of a primitive collection, <c>r.Tags.Any(t => …)</c>.</summary>
    CollectionElement,

    /// <summary>The element of a list the query captures, <c>ids.Any(i => … r.P …)</c>.</summary>
    ParameterElement,

    /// <summary>A column reached over a collection navigation, <c>b.Rows.Max(x => …)</c>.</summary>
    Navigation,

    /// <summary>A property of an owned type mapped to a JSON column, <c>r.Doc.P</c>.</summary>
    JsonProperty,

    /// <summary>A query filter of the model, which EF adds after the query's own tree is seen.</summary>
    QueryFilter,

    /// <summary>A named query filter.</summary>
    NamedQueryFilter,
}

/// <summary>How the query reaches what the value object wraps.</summary>
public enum Accessor
{
    Value,
    GetValue,
    ValueOrNull,

    /// <summary>Not at all: the value object itself, compared, ordered or projected.</summary>
    Itself,

    /// <summary>
    /// <c>.Value</c> in a generic method or base class, <c>where T : IValueObject&lt;T, TValue&gt;</c>: the tree
    /// C# writes holds <c>((IValueObject&lt;X, W&gt;)r.P).Value</c>, the interface's member, not the value object's
    /// (<c>r.N!.Value.Value</c> through a <c>Nullable</c>).
    /// </summary>
    ValueThroughTypeParameter,
}

/// <summary>Who stores the value object's column.</summary>
public enum Conversion
{
    /// <summary>The converter <c>UseTypeKit()</c> composes.</summary>
    TypeKit,

    /// <summary><c>HasConversion(typeof(ValueObjectConverter&lt;,&gt;))</c>, TypeKit's own, named.</summary>
    ExplicitTypeKit,

    /// <summary>A converter of the application's own, built from lambdas: stores <c>"C:" + value</c>.</summary>
    ForeignLambda,

    /// <summary>The same as a converter type, <c>HasConversion(typeof(TConverter))</c>.</summary>
    ForeignConverterType,

    /// <summary>The same from <c>ConfigureConventions</c>.</summary>
    ForeignConvention,
}

/// <summary>What the query does with the value it reads.</summary>
public enum Op
{
    EqualsLiteral,
    EqualsCaptured,
    NotEquals,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,

    /// <summary><c>r.N == null || r.N &lt; x</c>: the null rows kept in SQL, as the generated operators of a record class keep them in memory.</summary>
    LessThanOrNull,

    CompareTo,
    OrderBy,
    OrderByDescending,
    ThenBy,
    ThenByDescending,
    OrderByBoxed,
    ThenByBoxed,

    /// <summary><c>Select(r =&gt; (object)r.P).Order()</c>: boxed before the operator, which then reads <c>object</c> (#206).</summary>
    OrderBoxed,

    /// <summary><c>Select(r =&gt; (object)r.P).OrderBy(x =&gt; x)</c>: a key that is the element itself.</summary>
    OrderByItselfBoxed,

    MinBoxed,
    MaxBoxed,
    Order,
    OrderDescending,
    Min,
    Max,
    MinBy,
    MaxBy,
    Contains,
    GroupBy,
    Select,
    Distinct,
    AnyEquals,
    AnyGreaterOrEqual,
    CountGreaterOrEqual,
    ElementContains,
    ElementMax,
    ElementOrderFirst,
    NavigationMin,
    NavigationMax,
    NavigationOrderFirst,
    NavigationOrderDescendingFirst,

    /// <summary><c>EF.Functions.Greatest(r.P, x)</c>, relational: SQLite's <c>max</c>, PostgreSQL's <c>GREATEST</c>.</summary>
    Greatest,

    /// <summary><c>EF.Functions.Least(r.P, x)</c>.</summary>
    Least,

    /// <summary>Npgsql's row value, <c>EF.Functions.GreaterThan((r.P, r.Id), (x, 0))</c>; SQLite translates none of the four.</summary>
    RowGreaterThan,

    RowLessThan,
    RowGreaterOrEqual,
    RowLessOrEqual,
}

/// <summary>One query of the catalogue: a kind, who stores it, where the query reads it, how, and what it does.</summary>
public sealed record QueryCase(WrappedKind Kind, Conversion Conversion, Site Site, Accessor Accessor, Op Op)
{
    public string Id => $"{Kind.Name} | {Conversion} | {Site} | {Accessor} | {Op}";

    public override string ToString() => Id;

    public bool ReadsWrapped => Accessor != Accessor.Itself;

    /// <summary>A comparison, an ordering, or a minimum or maximum: what a value object's own <c>CompareTo</c> decides in memory.</summary>
    public bool Orders => Op is Op.GreaterOrEqual or Op.LessThan or Op.LessOrEqual or Op.LessThanOrNull or Op.CompareTo or Op.OrderBy or Op.OrderByDescending or Op.ThenBy or Op.ThenByDescending
                             or Op.OrderByBoxed or Op.ThenByBoxed or Op.OrderBoxed or Op.OrderByItselfBoxed or Op.MinBoxed or Op.MaxBoxed
                             or Op.Order or Op.OrderDescending or Op.Min or Op.Max or Op.MinBy or Op.MaxBy
                             or Op.AnyGreaterOrEqual or Op.CountGreaterOrEqual or Op.ElementMax or Op.ElementOrderFirst
                             or Op.NavigationMin or Op.NavigationMax or Op.NavigationOrderFirst or Op.NavigationOrderDescendingFirst
                             or Op.Greatest or Op.Least or Op.RowGreaterThan or Op.RowLessThan or Op.RowGreaterOrEqual or Op.RowLessOrEqual;

    public bool IsFilter => Site is Site.QueryFilter or Site.NamedQueryFilter;

    public bool IsForeign => Conversion is Conversion.ForeignLambda or Conversion.ForeignConverterType or Conversion.ForeignConvention;
}

/// <summary>
/// The query-translation catalogue (chain I of design/fix-history.md): what a query reads of a value object,
/// where, through which accessor and converter, and what it does with it, over every kind of
/// <see cref="WrappedKinds.Queries"/>.
/// </summary>
public static class QueryShapes
{
    /// <summary>The database's own orderings, called through <c>EF.Functions</c>; declared first, as <see cref="ColumnOps"/> reads it.</summary>
    private static readonly Op[] FunctionOps = [Op.Greatest, Op.Least, Op.RowGreaterThan, Op.RowLessThan, Op.RowGreaterOrEqual, Op.RowLessOrEqual];

    private static readonly Op[] ColumnOps =
    [
        Op.EqualsLiteral, Op.EqualsCaptured, Op.NotEquals, Op.GreaterOrEqual, Op.LessThan, Op.LessOrEqual, Op.CompareTo,
        Op.OrderBy, Op.OrderByDescending, Op.ThenBy, Op.ThenByDescending, Op.OrderByBoxed, Op.ThenByBoxed,
        Op.OrderBoxed, Op.OrderByItselfBoxed, Op.MinBoxed, Op.MaxBoxed, Op.Order, Op.OrderDescending,
        Op.Min, Op.Max, Op.MinBy, Op.MaxBy, Op.Contains, Op.GroupBy, Op.Select, Op.Distinct, .. FunctionOps,
    ];

    private static readonly Dictionary<Site, Op[]> Ops = new()
    {
        [Site.Column]            = ColumnOps,
        [Site.Nullable]          = [.. ColumnOps.Where(op => op != Op.CompareTo), Op.LessThanOrNull],
        [Site.CollectionElement] = [Op.AnyEquals, Op.AnyGreaterOrEqual, Op.ElementContains, Op.ElementMax, Op.ElementOrderFirst, .. FunctionOps],
        [Site.ParameterElement]  = [Op.AnyEquals, Op.AnyGreaterOrEqual],
        [Site.Navigation]        = [Op.AnyEquals, Op.CountGreaterOrEqual, Op.NavigationMin, Op.NavigationMax, Op.NavigationOrderFirst, Op.NavigationOrderDescendingFirst],
        [Site.JsonProperty]      = [Op.EqualsCaptured, Op.GreaterOrEqual, Op.OrderBy, Op.Max, Op.Select],
        [Site.QueryFilter]       = [Op.EqualsLiteral, Op.GreaterOrEqual, Op.CompareTo, Op.NavigationMax, .. FunctionOps],
        [Site.NamedQueryFilter]  = [Op.GreaterOrEqual],
    };

    private static Accessor[] Accessors(Site site) => site switch
    {
        Site.Nullable                                   => [Accessor.ValueOrNull, Accessor.ValueThroughTypeParameter, Accessor.Itself],
        Site.QueryFilter or Site.NamedQueryFilter       => [Accessor.Value, Accessor.ValueThroughTypeParameter, Accessor.Itself],
        _                                               => [Accessor.Value, Accessor.GetValue, Accessor.ValueThroughTypeParameter, Accessor.Itself],
    };

    /// <summary>The kinds a converter of the application's own is tried on, and the sites it reaches: the table's columns.</summary>
    private static readonly WrappedKind[] ForeignKinds = [WrappedKinds.Int32, WrappedKinds.String, WrappedKinds.StringRecordClass];

    private static readonly Site[] ConvertedSites = [Site.Column, Site.Nullable, Site.ParameterElement, Site.Navigation];

    private static readonly Lazy<Dictionary<string, QueryCase>> Catalogue = new(() => Enumerate().ToDictionary(c => c.Id));

    public static IEnumerable<QueryCase> All() => Catalogue.Value.Values;

    public static QueryCase Find(string id) => Catalogue.Value[id];

    /// <summary>Every combination C# can write: one whose query over the value object does not build (no <c>&gt;=</c> on a <c>Guid</c>) is no case.</summary>
    private static IEnumerable<QueryCase> Enumerate()
    {
        foreach (var kind in WrappedKinds.Queries)
            foreach (var conversion in Enum.GetValues<Conversion>())
            {
                if (conversion != Conversion.TypeKit && !ForeignKinds.Contains(kind)) continue;

                foreach (var (site, ops) in Ops)
                {
                    if (conversion != Conversion.TypeKit && !ConvertedSites.Contains(site)) continue;

                    foreach (var accessor in Accessors(site))
                        foreach (var op in ops)
                        {
                            var candidate = new QueryCase(kind, conversion, site, accessor, op);
                            if (new QueryBuilder(candidate, valueObject: true).CanBuild) yield return candidate;
                        }
                }
            }
    }
}

/// <summary>Holds a captured value, so that a query reads it as a parameter, as a closure does.</summary>
public sealed class Box<T>(T value)
{
    public T Value = value;
}

public class Row<T, TN>
{
    public int Id { get; set; }

    public T P { get; set; } = default!;

    public TN? N { get; set; }

    public int BagId { get; set; }
}

public class Bag<T, TN>
{
    public int Id { get; set; }

    public List<Row<T, TN>> Rows { get; set; } = [];
}

public class ElementRow<T>
{
    public int Id { get; set; }

    public List<T> Tags { get; set; } = [];
}

public class DocRow<T>
{
    public int Id { get; set; }

    public Doc<T> Doc { get; set; } = new();
}

public class Doc<T>
{
    public T P { get; set; } = default!;
}

/// <summary>
/// One query of the catalogue, built over the value object or over the wrapped type: the same expression
/// tree either way, but for the accessor, so the two can be compared, and run against a database or
/// against the objects in memory.
/// </summary>
public sealed class QueryBuilder(QueryCase @case, bool valueObject)
{
    private WrappedKind Kind => @case.Kind;

    public Type Element => valueObject ? Kind.ValueObject : Kind.Wrapped;

    public Type Optional => Element.IsValueType ? typeof(Nullable<>).MakeGenericType(Element) : Element;

    public Type Row => typeof(Row<,>).MakeGenericType(Element, Optional);

    public Type Bag => typeof(Bag<,>).MakeGenericType(Element, Optional);

    public Type ElementRow => typeof(ElementRow<>).MakeGenericType(Element);

    public Type DocRow => typeof(DocRow<>).MakeGenericType(Element);

    /// <summary>The entity type the query starts from.</summary>
    public Type Source => @case.Site switch
    {
        Site.Navigation                                                    => Bag,
        Site.QueryFilter when @case.Op == Op.NavigationMax                 => Bag,
        Site.CollectionElement                                             => ElementRow,
        Site.JsonProperty                                                  => DocRow,
        _                                                                  => Row,
    };

    public bool CanBuild
    {
        get
        {
            try
            {
                if (@case.IsFilter) Filter();
                else Build(Expression.Constant(Array.CreateInstance(Source, 0).AsQueryable()));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>The query, and whether its rows come in an order the query states.</summary>
    public (Expression Query, bool Ordered) Build(Expression source)
    {
        if (@case.IsFilter)
        {
            var entity = Expression.Parameter(Source, "e");
            return (Q("Select", [Source, typeof(int)], source, Expression.Lambda(Expression.Property(entity, "Id"), entity)), false);
        }

        return @case.Site switch
        {
            Site.Column or Site.Nullable or Site.JsonProperty => OverColumn(source),
            Site.CollectionElement                            => OverElements(source),
            Site.ParameterElement                             => OverParameter(source),
            Site.Navigation                                   => OverNavigation(source),
            _                                                 => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };
    }

    /// <summary>The query filter of a <see cref="Site.QueryFilter"/> case, over <see cref="Source"/>.</summary>
    public LambdaExpression Filter()
    {
        var entity = Expression.Parameter(Source, "e");
        if (@case.Op == Op.NavigationMax)
        {
            var row = Expression.Parameter(Row, "x");
            var max = Read(Expression.Property(row, "P"));
            var aggregate = E("Max", [Row, max.Type], Expression.Property(entity, "Rows"), Expression.Lambda(max, row));
            return Expression.Lambda(Expression.GreaterThanOrEqual(aggregate, Literal(aggregate.Type)), entity);
        }

        var read = Read(Expression.Property(entity, "P"));
        Expression body = @case.Op switch
        {
            Op.EqualsLiteral  => Expression.Equal(read, Literal(read.Type)),
            Op.GreaterOrEqual => Expression.GreaterThanOrEqual(read, Literal(read.Type)),
            Op.CompareTo      => Expression.GreaterThan(CompareTo(read, Literal(read.Type)), Expression.Constant(0)),
            Op.Greatest or Op.Least => Expression.Equal(Function(@case.Op, read, Literal(read.Type)), read),
            Op.RowGreaterThan or Op.RowLessThan or Op.RowGreaterOrEqual or Op.RowLessOrEqual
                              => RowValue(@case.Op, read, Expression.Property(entity, "Id"), Literal(read.Type), Expression.Constant(0)),
            _                 => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };
        return Expression.Lambda(body, entity);
    }

    /// <summary>
    /// The value the query reads, through the case's accessor; the wrapped type is read as it is, but for a
    /// <c>Nullable</c> read through a type parameter: <c>r.N!.Value</c> over the value object is <c>r.N!.Value</c>
    /// over an <c>int?</c>, and both throw on a null they project.
    /// </summary>
    private Expression Read(Expression operand) => !valueObject || @case.Accessor == Accessor.Itself
        ? !valueObject && @case.Accessor == Accessor.ValueThroughTypeParameter && Nullable.GetUnderlyingType(operand.Type) is not null
              ? Expression.Property(operand, "Value")
              : operand
        : @case.Accessor switch
        {
            Accessor.Value       => Expression.Property(operand, "Value"),
            Accessor.GetValue    => Expression.Call(Kind.Companion("GetValue"), operand),
            Accessor.ValueOrNull => Expression.Call(Kind.Companion("ValueOrNull"), operand),
            Accessor.ValueThroughTypeParameter => ThroughTypeParameter(operand),
            _                    => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };

    /// <summary>What C# writes for <c>.Value</c> on a <c>T</c> constrained to the interface: a conversion to it, and its member.</summary>
    private Expression ThroughTypeParameter(Expression operand)
    {
        var contract = typeof(IValueObject<,>).MakeGenericType(Kind.ValueObject, Kind.Wrapped);
        var instance = Nullable.GetUnderlyingType(operand.Type) is null ? operand : Expression.Property(operand, "Value");
        return Expression.Property(Expression.Convert(instance, contract), contract.GetProperty("Value")!);
    }

    /// <summary>The middle value, as the type the query reads it as.</summary>
    private object Middle => valueObject && @case.Accessor == Accessor.Itself ? Kind.Wrap(Kind.Values[1]) : Kind.Values[1];

    private Expression Literal(Type type) => Expression.Constant(Middle, type);

    private Expression Captured(Type type) =>
        Expression.Field(Expression.Constant(Activator.CreateInstance(typeof(Box<>).MakeGenericType(type), Middle)), "Value");

    /// <summary><c>EF.Functions</c>, as C# writes it in an expression tree.</summary>
    private static readonly Expression Functions = Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions))!);

    /// <summary><c>EF.Functions.Greatest(left, right)</c> or <c>Least</c>, typed as <paramref name="left"/>.</summary>
    private static Expression Function(Op op, Expression left, Expression right) =>
        Expression.Call(typeof(RelationalDbFunctionsExtensions), op.ToString(), [left.Type], Functions, Expression.NewArrayInit(left.Type, left, right));

    /// <summary>Npgsql's row-value comparison of <c>(left, leftKey)</c> with <c>(right, rightKey)</c>, each tuple passed as an <see cref="ITuple"/>.</summary>
    private static Expression RowValue(Op op, Expression left, Expression leftKey, Expression right, Expression rightKey)
    {
        var name = op switch
        {
            Op.RowGreaterThan    => nameof(NpgsqlDbFunctionsExtensions.GreaterThan),
            Op.RowLessThan       => nameof(NpgsqlDbFunctionsExtensions.LessThan),
            Op.RowGreaterOrEqual => nameof(NpgsqlDbFunctionsExtensions.GreaterThanOrEqual),
            _                    => nameof(NpgsqlDbFunctionsExtensions.LessThanOrEqual),
        };
        Expression Tuple(Expression item1, Expression item2) =>
            Expression.Convert(Expression.Call(typeof(ValueTuple), nameof(ValueTuple.Create), [item1.Type, item2.Type], item1, item2), typeof(ITuple));

        return Expression.Call(typeof(NpgsqlDbFunctionsExtensions), name, null, Functions, Tuple(left, leftKey), Tuple(right, rightKey));
    }

    private static Expression CompareTo(Expression left, Expression right) =>
        Expression.Call(left, left.Type.GetMethod("CompareTo", [left.Type]) ?? throw new InvalidOperationException($"{left.Type} has no CompareTo({left.Type})"), right);

    private (Expression, bool) OverColumn(Expression source)
    {
        var row = Expression.Parameter(Source, "r");
        Expression operand = @case.Site switch
        {
            Site.Column       => Expression.Property(row, "P"),
            Site.Nullable     => Expression.Property(row, "N"),
            Site.JsonProperty => Expression.Property(Expression.Property(row, "Doc"), "P"),
            _                 => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };
        var read = Read(operand);
        var type = read.Type;
        var key = Expression.Lambda(read, row);

        Expression Ids(Expression query) => Q("Select", [Source, typeof(int)], query, Expression.Lambda(Expression.Property(row, "Id"), row));
        Expression Where(Expression predicate) => Ids(Q("Where", [Source], source, Expression.Lambda(predicate, row)));
        Expression Values(Expression ordered) => Q("Select", [Source, type], ordered, key);
        Expression Unordered() => Q("OrderBy", [Source, typeof(int)], source, Expression.Lambda(Expression.Modulo(Expression.Property(row, "Id"), Expression.Constant(1)), row));
        var boxed = Expression.Lambda(Expression.Convert(read, typeof(object)), row);
        var boxedValues = Q("Select", [Source, typeof(object)], source, boxed);
        var itself = Expression.Parameter(typeof(object), "x");

        return @case.Op switch
        {
            Op.EqualsLiteral     => (Where(Expression.Equal(read, Literal(type))), false),
            Op.EqualsCaptured    => (Where(Expression.Equal(read, Captured(type))), false),
            Op.NotEquals         => (Where(Expression.NotEqual(read, Captured(type))), false),
            Op.GreaterOrEqual    => (Where(Expression.GreaterThanOrEqual(read, Captured(type))), false),
            Op.LessThan          => (Where(Expression.LessThan(read, Captured(type))), false),
            Op.LessOrEqual       => (Where(Expression.LessThanOrEqual(read, Captured(type))), false),
            Op.LessThanOrNull    => (Where(Expression.OrElse(Expression.Equal(read, Expression.Constant(null, type)), Expression.LessThan(read, Captured(type)))), false),
            Op.CompareTo         => (Where(Expression.GreaterThan(CompareTo(read, Captured(type)), Expression.Constant(0))), false),
            Op.OrderBy           => (Values(Q("OrderBy", [Source, type], source, key)), true),
            Op.OrderByDescending => (Values(Q("OrderByDescending", [Source, type], source, key)), true),
            Op.ThenBy            => (Values(Q("ThenBy", [Source, type], Unordered(), key)), true),
            Op.ThenByDescending  => (Values(Q("ThenByDescending", [Source, type], Unordered(), key)), true),
            Op.OrderByBoxed      => (Values(Q("OrderBy", [Source, typeof(object)], source, boxed)), true),
            Op.ThenByBoxed       => (Values(Q("ThenBy", [Source, typeof(object)], Unordered(), boxed)), true),
            Op.OrderBoxed        => (Q("Order", [typeof(object)], boxedValues), true),
            Op.OrderByItselfBoxed => (Q("OrderBy", [typeof(object), typeof(object)], boxedValues, Expression.Lambda(itself, itself)), true),
            Op.MinBoxed          => (Q("Min", [typeof(object)], boxedValues), true),
            Op.MaxBoxed          => (Q("Max", [typeof(object)], boxedValues), true),
            Op.Order             => (Q("Order", [type], Values(source)), true),
            Op.OrderDescending   => (Q("OrderDescending", [type], Values(source)), true),
            Op.Min               => (Q("Min", [Source, type], source, key), true),
            Op.Max               => (Q("Max", [Source, type], source, key), true),
            Op.MinBy             => (Q("MinBy", [Source, type], source, key), true),
            Op.MaxBy             => (Q("MaxBy", [Source, type], source, key), true),
            Op.Contains          => (Where(E("Contains", [type], Captured(typeof(List<>).MakeGenericType(type), List(type, Middle)), read)), false),
            Op.GroupBy           => (Grouped(source, key, type), false),
            Op.Select            => (Values(source), false),
            Op.Distinct          => (Q("Distinct", [type], Values(source)), false),
            Op.Greatest or Op.Least => (Q("Select", [Source, type], source, Expression.Lambda(Function(@case.Op, read, Captured(type)), row)), false),
            Op.RowGreaterThan or Op.RowLessThan or Op.RowGreaterOrEqual or Op.RowLessOrEqual
                                 => (Where(RowValue(@case.Op, read, Expression.Property(row, "Id"), Captured(type), Captured(typeof(int), 0))), false),
            _                    => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };
    }

    private Expression Grouped(Expression source, LambdaExpression key, Type type)
    {
        var grouping = typeof(IGrouping<,>).MakeGenericType(type, Source);
        var group = Expression.Parameter(grouping, "g");
        return Q("Select", [grouping, type], Q("GroupBy", [Source, type], source, key), Expression.Lambda(Expression.Property(group, "Key"), group));
    }

    private (Expression, bool) OverElements(Expression source)
    {
        var row = Expression.Parameter(Source, "r");
        var tag = Expression.Parameter(Element, "t");
        var tags = Expression.Property(row, "Tags");
        var read = Read(tag);
        var type = read.Type;
        var key = Expression.Lambda(read, tag);

        Expression Where(Expression predicate) =>
            Q("Select", [Source, typeof(int)], Q("Where", [Source], source, Expression.Lambda(predicate, row)), Expression.Lambda(Expression.Property(row, "Id"), row));
        Expression Select(Expression value) => Q("Select", [Source, type], source, Expression.Lambda(value, row));

        return @case.Op switch
        {
            Op.AnyEquals         => (Where(E("Any", [Element], tags, Expression.Lambda(Expression.Equal(read, Captured(type)), tag))), false),
            Op.AnyGreaterOrEqual => (Where(E("Any", [Element], tags, Expression.Lambda(Expression.GreaterThanOrEqual(read, Captured(type)), tag))), false),
            Op.ElementContains   => (Where(E("Contains", [type], E("Select", [Element, type], tags, key), Captured(type))), false),
            Op.ElementMax        => (Select(E("Max", [Element, type], tags, key)), false),
            Op.ElementOrderFirst => (Select(E("First", [type], E("Select", [Element, type], E("OrderBy", [Element, type], tags, key), key))), false),
            Op.Greatest or Op.Least => (Where(E("Any", [Element], tags, Expression.Lambda(Expression.Equal(Function(@case.Op, read, Captured(type)), read), tag))), false),
            Op.RowGreaterThan or Op.RowLessThan or Op.RowGreaterOrEqual or Op.RowLessOrEqual
                                 => (Where(E("Any", [Element], tags, Expression.Lambda(RowValue(@case.Op, read, Expression.Property(row, "Id"), Captured(type), Captured(typeof(int), 0)), tag))), false),
            _                    => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };
    }

    private (Expression, bool) OverParameter(Expression source)
    {
        var row = Expression.Parameter(Source, "r");
        var item = Expression.Parameter(Element, "i");
        var list = Captured(typeof(List<>).MakeGenericType(Element), List(Element, valueObject ? Kind.Wrap(Kind.Values[1]) : Kind.Values[1]));
        var column = Read(Expression.Property(row, "P"));
        var read = Read(item);

        Expression comparison = @case.Op switch
        {
            Op.AnyEquals         => Expression.Equal(read, column),
            Op.AnyGreaterOrEqual => Expression.GreaterThanOrEqual(read, column),
            _                    => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };
        var where = Q("Where", [Source], source, Expression.Lambda(E("Any", [Element], list, Expression.Lambda(comparison, item)), row));
        return (Q("Select", [Source, typeof(int)], where, Expression.Lambda(Expression.Property(row, "Id"), row)), false);
    }

    private (Expression, bool) OverNavigation(Expression source)
    {
        var bag = Expression.Parameter(Source, "b");
        var row = Expression.Parameter(Row, "x");
        var rows = Expression.Property(bag, "Rows");
        var read = Read(Expression.Property(row, "P"));
        var type = read.Type;
        var key = Expression.Lambda(read, row);

        Expression Select(Expression value) => Q("Select", [Source, value.Type], source, Expression.Lambda(value, bag));

        return @case.Op switch
        {
            Op.AnyEquals =>
                (Q("Select", [Source, typeof(int)], Q("Where", [Source], source, Expression.Lambda(E("Any", [Row], rows, Expression.Lambda(Expression.Equal(read, Captured(type)), row)), bag)),
                   Expression.Lambda(Expression.Property(bag, "Id"), bag)), false),
            Op.CountGreaterOrEqual            => (Select(E("Count", [Row], rows, Expression.Lambda(Expression.GreaterThanOrEqual(read, Captured(type)), row))), false),
            Op.NavigationMin                  => (Select(E("Min", [Row, type], rows, key)), false),
            Op.NavigationMax                  => (Select(E("Max", [Row, type], rows, key)), false),
            Op.NavigationOrderFirst           => (Select(E("First", [type], E("Select", [Row, type], E("OrderBy", [Row, type], rows, key), key))), false),
            Op.NavigationOrderDescendingFirst => (Select(E("First", [type], E("Select", [Row, type], E("OrderByDescending", [Row, type], rows, key), key))), false),
            _                                 => throw new ArgumentOutOfRangeException(nameof(@case), @case, null),
        };
    }

    /// <summary>A list of one value, as <paramref name="type"/>.</summary>
    private static object List(Type type, object value)
    {
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type))!;
        list.Add(value);
        return list;
    }

    private static Expression Captured(Type type, object value) =>
        Expression.Field(Expression.Constant(Activator.CreateInstance(typeof(Box<>).MakeGenericType(type), value)), "Value");

    private static Expression Q(string name, Type[] types, params Expression[] arguments) =>
        Expression.Call(typeof(Queryable), name, types, [.. arguments.Select(argument => argument is LambdaExpression lambda ? Expression.Quote(lambda) : argument)]);

    private static Expression E(string name, Type[] types, params Expression[] arguments) => Expression.Call(typeof(Enumerable), name, types, arguments);
}

/// <summary>The rows every query of a kind reads: three, in an order no query states, one with no optional value.</summary>
public static class Seed
{
    private static readonly int[] Order = [1, 2, 0];

    private static object Value(WrappedKind kind, bool valueObject, int index)
    {
        var wrapped = kind.Values[index % kind.Values.Length];
        return valueObject ? kind.Wrap(wrapped) : wrapped;
    }

    /// <summary>The entities of the case's source, with their navigations, as objects.</summary>
    public static System.Collections.IList Entities(QueryBuilder builder, WrappedKind kind, bool valueObject)
    {
        var entities = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(builder.Source))!;
        if (builder.Source == builder.Row)
        {
            foreach (var bag in Graph(builder, kind, valueObject))
                foreach (var row in (System.Collections.IList)bag.GetType().GetProperty("Rows")!.GetValue(bag)!)
                    entities.Add(row);
            var ordered = entities.Cast<object>().OrderBy(row => (int)row.GetType().GetProperty("Id")!.GetValue(row)!).ToList();
            entities.Clear();
            foreach (var row in ordered) entities.Add(row);
        }
        else if (builder.Source == builder.Bag)
        {
            foreach (var bag in Graph(builder, kind, valueObject)) entities.Add(bag);
        }
        else if (builder.Source == builder.ElementRow)
        {
            int[][] tags = [[1, 2], [0], [2, 0, 1]];
            for (var i = 0; i < tags.Length; i++)
            {
                var entity = Activator.CreateInstance(builder.ElementRow)!;
                Set(entity, "Id", i + 1);
                var list = (System.Collections.IList)entity.GetType().GetProperty("Tags")!.GetValue(entity)!;
                foreach (var index in tags[i]) list.Add(Value(kind, valueObject, index));
                entities.Add(entity);
            }
        }
        else
        {
            for (var i = 0; i < Order.Length; i++)
            {
                var entity = Activator.CreateInstance(builder.DocRow)!;
                Set(entity, "Id", i + 1);
                Set(entity.GetType().GetProperty("Doc")!.GetValue(entity)!, "P", Value(kind, valueObject, Order[i]));
                entities.Add(entity);
            }
        }
        return entities;
    }

    /// <summary>What a database is seeded with: the case's own entities, or the bags that hold the rows.</summary>
    public static IEnumerable<object> Stored(QueryBuilder builder, WrappedKind kind, bool valueObject) =>
        builder.Source == builder.Row || builder.Source == builder.Bag ? Graph(builder, kind, valueObject) : Entities(builder, kind, valueObject).Cast<object>();

    private static List<object> Graph(QueryBuilder builder, WrappedKind kind, bool valueObject)
    {
        var rows = Rows(builder, kind, valueObject);
        var bags = new List<object>();
        foreach (var id in new[] { 1, 2 })
        {
            var bag = Activator.CreateInstance(builder.Bag)!;
            Set(bag, "Id", id);
            var children = (System.Collections.IList)bag.GetType().GetProperty("Rows")!.GetValue(bag)!;
            foreach (var row in rows.Where(row => (int)row.GetType().GetProperty("BagId")!.GetValue(row)! == id)) children.Add(row);
            bags.Add(bag);
        }
        return bags;
    }

    private static List<object> Rows(QueryBuilder builder, WrappedKind kind, bool valueObject)
    {
        int?[] optional = [2, null, 0];
        var rows = new List<object>();
        for (var i = 0; i < Order.Length; i++)
        {
            var row = Activator.CreateInstance(builder.Row)!;
            Set(row, "Id", i + 1);
            Set(row, "P", Value(kind, valueObject, Order[i]));
            Set(row, "N", optional[i] is { } index ? Value(kind, valueObject, index) : null);
            Set(row, "BagId", i == 1 ? 2 : 1);
            rows.Add(row);
        }
        return rows;
    }

    private static void Set(object target, string property, object? value) => target.GetType().GetProperty(property)!.SetValue(target, value);
}

/// <summary>
/// A model of one kind's entities for the catalogue, cached by <see cref="Key"/>, in a schema of its own where
/// many models share one database.
/// </summary>
public sealed record QueryModel(string Key, QueryCase Case, bool ValueObject, string? Schema = null);

public sealed class QueryDb(DbContextOptions<QueryDb> options, QueryModel model) : DbContext(options)
{
    public QueryModel Descriptor => model;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (model.ValueObject && model.Case.Conversion == Conversion.ForeignConvention)
            configurationBuilder.Properties(model.Case.Kind.ValueObject).HaveConversion(PrefixedText.ConverterType(model.Case.Kind));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = new QueryBuilder(model.Case, model.ValueObject);
        if (model.Schema is { } schema) modelBuilder.HasDefaultSchema(schema);

        if (builder.Source == builder.ElementRow)
        {
            modelBuilder.Entity(builder.ElementRow).ToTable("ElementRow").Property("Id").ValueGeneratedNever();
            return;
        }

        if (builder.Source == builder.DocRow)
        {
            var doc = modelBuilder.Entity(builder.DocRow).ToTable("DocRow");
            doc.Property("Id").ValueGeneratedNever();
            doc.OwnsOne(typeof(Doc<>).MakeGenericType(builder.Element), "Doc", owned => owned.ToJson().Property("P").IsRequired());
            return;
        }

        // Required on both sides: EF reads a generic T as nullable where T is a class, and a struct value object never is.
        var row = modelBuilder.Entity(builder.Row).ToTable("Row");
        row.Property("Id").ValueGeneratedNever();
        row.Property("P").IsRequired();
        var bag = modelBuilder.Entity(builder.Bag).ToTable("Bag");
        bag.Property("Id").ValueGeneratedNever();
        bag.HasMany(builder.Row, "Rows").WithOne().HasForeignKey("BagId");

        if (model.ValueObject)
            foreach (var property in new[] { "P", "N" })
                switch (model.Case.Conversion)
                {
                    case Conversion.ExplicitTypeKit:
                        row.Property(property).HasConversion(typeof(ValueObjectConverter<,>).MakeGenericType(builder.Element, model.Case.Kind.Wrapped));
                        break;
                    case Conversion.ForeignLambda:
                        row.Property(property).HasConversion(PrefixedText.Converter(model.Case.Kind));
                        break;
                    case Conversion.ForeignConverterType:
                        row.Property(property).HasConversion(PrefixedText.ConverterType(model.Case.Kind));
                        break;
                }

        if (!model.Case.IsFilter) return;

        var filtered = modelBuilder.Entity(builder.Source);
        if (model.Case.Site == Site.NamedQueryFilter) filtered.HasQueryFilter("Variant", builder.Filter());
        else filtered.HasQueryFilter(builder.Filter());
    }

    public static QueryDb Create(QueryModel model, Microsoft.Data.Sqlite.SqliteConnection connection) =>
        Create(model, new DbContextOptionsBuilder<QueryDb>().UseSqlite(connection));

    /// <summary>For SQL text only: <c>ToQueryString</c> translates without opening the connection.</summary>
    public static QueryDb OnPostgreSql(QueryModel model) =>
        Create(model, new DbContextOptionsBuilder<QueryDb>().UseNpgsql("Host=localhost;Database=none"));

    /// <summary>
    /// On SQL Server, at EF's default compatibility level or at <paramref name="compatibilityLevel"/>. The suite's
    /// models need more than the twenty internal service providers EF warns about, all in one process.
    /// </summary>
    public static QueryDb OnSqlServer(QueryModel model, string connectionString, int? compatibilityLevel = null) =>
        Create(model, new DbContextOptionsBuilder<QueryDb>().UseSqlServer(connectionString, sql =>
        {
            if (compatibilityLevel is { } level) sql.UseCompatibilityLevel(level);
        }).ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

    private static QueryDb Create(QueryModel model, DbContextOptionsBuilder<QueryDb> provider)
    {
        var builder = provider.ReplaceService<IModelCacheKeyFactory, QueryModelCacheKey>();
        if (model.ValueObject) builder = builder.UseTypeKit();

        return new QueryDb(builder.Options, model);
    }
}

public sealed class QueryModelCacheKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is QueryDb db ? (db.GetType(), db.Descriptor.Key, designTime) : (context.GetType(), designTime);
}

/// <summary>A converter of the application's own: it stores <c>"C:" + value</c>, which is not what <c>.Value</c> answers.</summary>
public static class PrefixedText
{
    public static Type ConverterType(WrappedKind kind) => typeof(PrefixedTextConverter<,>).MakeGenericType(kind.ValueObject, kind.Wrapped);

    public static ValueConverter Converter(WrappedKind kind) =>
        (ValueConverter)Activator.CreateInstance(typeof(ValueConverter<,>).MakeGenericType(kind.ValueObject, typeof(string)),
                                                 Expressions(kind.ValueObject, kind.Wrapped, out var fromProvider), fromProvider, null)!;

    internal static LambdaExpression Expressions(Type valueObject, Type wrapped, out LambdaExpression fromProvider)
    {
        var instance = Expression.Parameter(valueObject, "v");
        var text = Expression.Parameter(typeof(string), "s");
        var write = typeof(PrefixedText).GetMethod(nameof(Write))!.MakeGenericMethod(wrapped);
        var read = typeof(PrefixedText).GetMethod(nameof(Read))!.MakeGenericMethod(wrapped);
        var from = valueObject.GetMethod("From", [wrapped])!;
        fromProvider = Expression.Lambda(Expression.Call(from, Expression.Call(read, text)), text);
        return Expression.Lambda(Expression.Call(write, Expression.Property(instance, "Value")), instance);
    }

    public static string Write<T>(T value) => "C:" + Convert.ToString(value, CultureInfo.InvariantCulture);

    public static T Read<T>(string text) => (T)Convert.ChangeType(text[2..], typeof(T), CultureInfo.InvariantCulture);
}

public sealed class PrefixedTextConverter<TValueObject, TWrapped>() : ValueConverter<TValueObject, string>(ToProvider, FromProvider)
{
    private static readonly Expression<Func<TValueObject, string>> ToProvider =
        (Expression<Func<TValueObject, string>>)PrefixedText.Expressions(typeof(TValueObject), typeof(TWrapped), out var _);

    private static readonly Expression<Func<string, TValueObject>> FromProvider = Build();

    private static Expression<Func<string, TValueObject>> Build()
    {
        PrefixedText.Expressions(typeof(TValueObject), typeof(TWrapped), out var fromProvider);
        return (Expression<Func<string, TValueObject>>)fromProvider;
    }
}
