using System.Linq.Expressions;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>Whether an expression reads a parameter it does not declare itself, or a query.</summary>
internal sealed class DatabaseReads : ExpressionVisitor
{
    private readonly HashSet<ParameterExpression> _declared = [];
    private bool _found;

    public static bool In(Expression expression)
    {
        var reads = new DatabaseReads();
        reads.Visit(expression);

        return reads._found;
    }

    public override Expression? Visit(Expression? node)
    {
        if (_found || node is null) return node;
        if (typeof(IQueryable).IsAssignableFrom(node.Type)) _found = true;

        return _found ? node : base.Visit(node);
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        _declared.UnionWith(node.Parameters);

        return base.VisitLambda(node);
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (!_declared.Contains(node)) _found = true;

        return node;
    }
}
