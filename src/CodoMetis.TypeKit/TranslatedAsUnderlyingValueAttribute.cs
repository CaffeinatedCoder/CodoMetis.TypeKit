namespace CodoMetis.TypeKit;

/// <summary>
/// Marks a method whose call inside a relational query is translated as its single argument,
/// re-typed to the value object's underlying value: the column the argument already is.
/// </summary>
/// <remarks>
/// The method body must be exactly the unwrap the translation claims (<c>value?.Value</c>). The
/// attribute promises that the query and the in-memory call agree, and a method that does anything
/// more breaks that promise only on the database side, where nothing checks it.
/// CodoMetis.TypeKit.Generators puts it on the <c>GetValue</c>/<c>ValueOrNull</c> companions it
/// generates, and CodoMetis.TypeKit.EntityFrameworkCore translates it.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TranslatedAsUnderlyingValueAttribute : Attribute;
